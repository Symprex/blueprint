using System.Collections.Immutable;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace Blueprint.Codemod;

/// <summary>
/// Applies the currently supported activation codemod transformations by reusing the dry-run report to decide which
/// diagnostics are safe to hand off to the existing migration analyzer code-fix implementations.
/// </summary>
/// <remarks>
/// The apply strategy processes one project at a time, exhausting all fixable diagnostics within
/// that project before moving on. This avoids re-scanning earlier projects after they are fully
/// fixed, making the approach scale to solutions with thousands of diagnostics (e.g. S365 has
/// 2,784 auto-fixable instances across 158 projects).
/// </remarks>
public sealed class TransformationApplier
{
    /// <summary>
    /// Safety limit per project to guard against infinite loops where a code fix continuously
    /// re-introduces the same diagnostic. Sized above Signature365's largest single service:
    /// Portal is estimated at 900-1,000+ auto-fixable instances once BPMIG402 is included
    /// (~660 legacy namespace usings, ~66 <c>[FromResource(...)]</c> sites, ~96
    /// <c>[Resource(...)]</c> sites, and ~161 operation classes needing <c>partial</c>). A limit at
    /// or below that count would silently leave the largest service partially migrated inside a PR
    /// that cannot be staged, printing only a stderr warning rather than failing loudly. Declined
    /// diagnostics (e.g. BPMIG203 findings the code fix leaves for manual review) do not consume
    /// this counter, since no fix is applied for them.
    /// </summary>
    private const int MaxFixesPerProject = 5_000;

    /// <summary>
    /// Caps the outer convergence loop. A round is one batched pass over every supported
    /// diagnostic id in a project, so rounds only continue while a previous round changed
    /// something. Interacting fixes (BPMIG200/201/203 on one attribute argument list) settle in
    /// two or three rounds; a higher count means a pair of fixes is undoing each other, which is
    /// a defect to surface loudly rather than spin on.
    /// </summary>
    private const int MaxRoundsPerProject = 10;

    private static readonly ImmutableArray<string> SupportedDiagnosticIds =
    [
        "BPMIG001",
        "BPMIG002",
        "BPMIG003",
        "BPMIG200",
        "BPMIG201",
        "BPMIG203",
        "BPMIG300",
        "BPMIG303",
        "BPMIG400",
        "BPMIG401",
        "BPMIG402",
    ];

    private static int _msbuildRegistered;

    /// <summary>
    /// Gets the number of compile-and-analyse passes the last <see cref="ApplyAsync"/> call made.
    /// Exposed for tests: it is the cost driver of apply mode, and a regression here is invisible
    /// in the output but turns a minutes-long run on a large solution into an hours-long one.
    /// </summary>
    internal int AnalyzerPasses { get; private set; }

    /// <summary>
    /// Applies the currently supported codemod transformations for a solution.
    /// </summary>
    /// <param name="options">The apply-mode options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the currently supported transformations have been applied.</returns>
    public async Task ApplyAsync(ApplyModeOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var runner = new SolutionAnalysisRunner();
        var analysisResult = await runner.DryRunAsync(
            new SolutionAnalysisOptions(options.SolutionPath, options.DiagnosticFilter),
            cancellationToken);
        var diagnosticsToApply = analysisResult.Report.Findings
            .Select(static finding => finding.DiagnosticId)
            .Where(static diagnosticId => SupportedDiagnosticIds.Contains(diagnosticId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static diagnosticId => diagnosticId, StringComparer.Ordinal)
            .ToArray();

        if (diagnosticsToApply.Length == 0)
        {
            return;
        }

        // Register MSBuild only when we need the Roslyn workspace, after the dotnet-build dry run
        // has completed. Earlier registration would poison the child process environment variables.
        EnsureMsBuildRegistered();

        var solutionPath = Path.GetFullPath(options.SolutionPath);
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        var analyzers = CreateAnalyzers();
        var codeFixProviders = CreateCodeFixProviders();
        var totalFixes = 0;
        this.AnalyzerPasses = 0;

        // Process projects one at a time, exhausting all fixes within each project before
        // moving on. This means each project's compilation is rebuilt only for its own fixes,
        // not for unrelated fixes in other projects.
        //
        // Within a project, fixes are batched per diagnostic id through the provider's own
        // FixAllProvider, and the project is re-analysed between ids. That split is deliberate:
        // batching is what removes the per-fix compilation (the cost driver - a project with a
        // thousand fixable sites paid a thousand full compile-and-analyse passes), while
        // re-analysing between ids preserves the interaction the per-fix loop got for free.
        // BPMIG200, BPMIG201 and BPMIG203 all edit the same attribute argument list, so a fix
        // for one changes what the others see; batching them against a single stale snapshot
        // would produce conflicting edits. The outer round loop then repeats until a whole pass
        // over the ids changes nothing, which is what makes the result independent of id order.
        foreach (var projectId in solution.ProjectIds)
        {
            var projectFixes = 0;
            var rounds = 0;

            while (rounds < MaxRoundsPerProject)
            {
                var roundFixes = 0;

                foreach (var diagnosticId in diagnosticsToApply)
                {
                    var project = solution.GetProject(projectId);
                    if (project is null)
                    {
                        break;
                    }

                    var (nextSolution, fixCount) = await this.ApplyAllForDiagnosticAsync(
                        solution,
                        project,
                        analyzers,
                        codeFixProviders,
                        diagnosticId,
                        options.DiagnosticFilter,
                        cancellationToken);

                    if (nextSolution is null)
                    {
                        continue;
                    }

                    solution = nextSolution;
                    roundFixes += fixCount;
                }

                if (roundFixes == 0)
                {
                    break;
                }

                projectFixes += roundFixes;
                totalFixes += roundFixes;
                rounds++;

                var currentName = solution.GetProject(projectId)?.Name ?? projectId.Id.ToString();
                Console.Error.WriteLine(
                    $"[codemod] {currentName}: round {rounds} applied {roundFixes} fixes ({totalFixes} total).");

                if (projectFixes >= MaxFixesPerProject)
                {
                    break;
                }
            }

            if (projectFixes >= MaxFixesPerProject || rounds >= MaxRoundsPerProject)
            {
                var projectName = solution.GetProject(projectId)?.Name ?? projectId.Id.ToString();
                Console.Error.WriteLine(
                    $"[codemod] WARNING: Project '{projectName}' stopped at {projectFixes} fixes after {rounds} round(s) " +
                    $"(limits: {MaxFixesPerProject} fixes, {MaxRoundsPerProject} rounds). Some diagnostics may remain unfixed.");
            }
        }

        Console.Error.WriteLine($"[codemod] Total fixes applied: {totalFixes}");

        if (totalFixes > 0 && !workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException(
                $"The codemod apply mode could not write the supported transformations back to '{solutionPath}'.");
        }
    }

    /// <summary>
    /// Applies every fixable occurrence of one diagnostic id across a project in a single
    /// compile-and-analyse pass, using the provider's own <see cref="FixAllProvider"/>. Returns
    /// the updated solution and the number of occurrences batched, or <c>(null, 0)</c> when the
    /// id has no fixable occurrence left.
    /// </summary>
    private async Task<(Solution? Solution, int FixCount)> ApplyAllForDiagnosticAsync(
        Solution solution,
        Project project,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableArray<CodeFixProvider> codeFixProviders,
        string diagnosticId,
        string diagnosticFilter,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            return (null, 0);
        }

        this.AnalyzerPasses++;
        var diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
        var candidates = diagnostics
            .Where(static candidate => candidate.Location.IsInSource)
            .Where(candidate => string.Equals(candidate.Id, diagnosticId, StringComparison.Ordinal))
            .Where(candidate => SolutionAnalysisRunner.MatchesDiagnosticFilter(candidate.Id, diagnosticFilter))
            .OrderBy(static candidate => candidate.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Location.SourceSpan.Start)
            .ToImmutableArray();

        if (candidates.Length == 0)
        {
            return (null, 0);
        }

        var codeFixProvider = codeFixProviders.SingleOrDefault(
            provider => provider.FixableDiagnosticIds.Contains(diagnosticId));
        if (codeFixProvider is null)
        {
            throw new InvalidOperationException(
                $"The codemod apply mode does not have a code-fix provider for supported diagnostic '{diagnosticId}'.");
        }

        // The equivalence key comes from asking the provider to register against a real
        // diagnostic rather than being hard-coded: it is the provider's own identifier for the
        // action, and FixAllProvider selects which registered action to batch by it.
        //
        // The anchor must be a candidate the provider actually fixes, not simply the first one.
        // Several providers decline per site - BPMIG203 declines any template with a non-auth
        // placeholder - and declining is a correct outcome, not an error. Anchoring on a
        // declining candidate yields no equivalence key and would skip every fixable occurrence
        // of the id in the project along with it.
        Document? anchorDocument = null;
        CodeAction? anchorAction = null;

        foreach (var candidate in candidates)
        {
            var document = solution.GetDocument(candidate.Location.SourceTree);
            if (document is null)
            {
                continue;
            }

            var registeredActions = new List<CodeAction>();
            var registrationContext = new CodeFixContext(
                document,
                candidate,
                (action, _) => registeredActions.Add(action),
                cancellationToken);
            await codeFixProvider.RegisterCodeFixesAsync(registrationContext);

            if (registeredActions.Count > 0)
            {
                anchorDocument = document;
                anchorAction = registeredActions[0];
                break;
            }
        }

        if (anchorDocument is null || anchorAction is null)
        {
            return (null, 0);
        }

        var fixAllProvider = codeFixProvider.GetFixAllProvider();
        if (fixAllProvider is null)
        {
            throw new InvalidOperationException(
                $"The code-fix provider for '{diagnosticId}' does not supply a FixAllProvider, so apply mode "
                + "cannot batch it. Declare one (WellKnownFixAllProviders.BatchFixer is usually correct).");
        }

        var fixAllContext = new FixAllContext(
            anchorDocument,
            codeFixProvider,
            FixAllScope.Project,
            anchorAction.EquivalenceKey,
            [diagnosticId],
            new BatchedDiagnosticProvider(candidates),
            cancellationToken);

        var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext);
        if (fixAllAction is null)
        {
            return (null, 0);
        }

        var operations = await fixAllAction.GetOperationsAsync(cancellationToken);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
        if (applyChangesOperation is null)
        {
            return (null, 0);
        }

        EnsureCodeFixMadeProgress(solution, applyChangesOperation.ChangedSolution, diagnosticId);
        return (applyChangesOperation.ChangedSolution, candidates.Length);
    }

    /// <summary>
    /// Feeds a fixed set of already-computed diagnostics to <see cref="FixAllContext"/>, so the
    /// batch runs against the analyser pass that has just been paid for rather than triggering
    /// another one per document.
    /// </summary>
    private sealed class BatchedDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> diagnostics;

        public BatchedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            this.diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(this.diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            var tree = document.GetSyntaxTreeAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult<IEnumerable<Diagnostic>>(
                this.diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == tree));
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(
                this.diagnostics.Where(static diagnostic => !diagnostic.Location.IsInSource));
        }
    }

    private static ImmutableArray<DiagnosticAnalyzer> CreateAnalyzers()
    {
        return typeof(Blueprint.Migration.Analyzers.MigrationDiagnosticDescriptors).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(static type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();
    }

    private static ImmutableArray<CodeFixProvider> CreateCodeFixProviders()
    {
        return typeof(Blueprint.Migration.Analyzers.CodeFixes.LegacyActivationCodeFixProvider).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(type))
            .Select(static type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .Where(static provider => provider.FixableDiagnosticIds.Any(static diagnosticId => SupportedDiagnosticIds.Contains(diagnosticId)))
            .ToImmutableArray();
    }

    private static void EnsureMsBuildRegistered()
    {
        if (Interlocked.Exchange(ref _msbuildRegistered, 1) == 1)
        {
            return;
        }

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static void EnsureCodeFixMadeProgress(
        Solution originalSolution,
        Solution changedSolution,
        string diagnosticId)
    {
        var changes = changedSolution.GetChanges(originalSolution);
        var hasChanges = changes.GetAddedProjects().Any()
            || changes.GetRemovedProjects().Any()
            || changes.GetProjectChanges().Any(static projectChange =>
                projectChange.GetAddedDocuments().Any()
                || projectChange.GetChangedDocuments().Any()
                || projectChange.GetRemovedDocuments().Any()
                || projectChange.GetAddedAdditionalDocuments().Any()
                || projectChange.GetChangedAdditionalDocuments().Any()
                || projectChange.GetRemovedAdditionalDocuments().Any()
                || projectChange.GetAddedAnalyzerConfigDocuments().Any()
                || projectChange.GetChangedAnalyzerConfigDocuments().Any()
                || projectChange.GetRemovedAnalyzerConfigDocuments().Any());

        if (!hasChanges)
        {
            throw new InvalidOperationException(
                $"The codemod apply mode code fix for '{diagnosticId}' did not change the solution.");
        }
    }
}
