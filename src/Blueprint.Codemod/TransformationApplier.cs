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
    /// re-introduces the same diagnostic.
    /// </summary>
    private const int MaxFixesPerProject = 1_000;

    private static readonly ImmutableArray<string> SupportedDiagnosticIds =
    [
        "BPMIG001",
        "BPMIG002",
        "BPMIG003",
        "BPMIG200",
        "BPMIG201",
        "BPMIG300",
        "BPMIG303",
        "BPMIG400",
        "BPMIG401",
        "BPMIG402",
    ];

    private static int _msbuildRegistered;

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

        // Process projects one at a time, exhausting all fixes within each project before
        // moving on. This means each project's compilation is rebuilt only for its own fixes,
        // not for unrelated fixes in other projects.
        foreach (var projectId in solution.ProjectIds)
        {
            var projectFixes = 0;

            while (projectFixes < MaxFixesPerProject)
            {
                var project = solution.GetProject(projectId);
                if (project is null)
                {
                    break;
                }

                var nextSolution = await ApplyNextFixForProjectAsync(
                    solution,
                    project,
                    analyzers,
                    codeFixProviders,
                    options.DiagnosticFilter,
                    cancellationToken);

                if (nextSolution is null)
                {
                    break;
                }

                solution = nextSolution;
                projectFixes++;
                totalFixes++;

                if (totalFixes % 100 == 0)
                {
                    Console.Error.WriteLine($"[codemod] Applied {totalFixes} fixes so far (current project: {project.Name})...");
                }
            }

            if (projectFixes >= MaxFixesPerProject)
            {
                var projectName = solution.GetProject(projectId)?.Name ?? projectId.Id.ToString();
                Console.Error.WriteLine(
                    $"[codemod] WARNING: Project '{projectName}' hit the {MaxFixesPerProject}-fix safety limit. " +
                    "Some diagnostics may remain unfixed.");
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
    /// Tries to apply the next supported code fix within a single project. Returns the updated
    /// solution if a fix was applied, or <c>null</c> if no more fixable diagnostics remain.
    /// </summary>
    private static async Task<Solution?> ApplyNextFixForProjectAsync(
        Solution solution,
        Project project,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableArray<CodeFixProvider> codeFixProviders,
        string diagnosticFilter,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
        {
            return null;
        }

        var diagnostics = await compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
        var candidates = diagnostics
            .Where(static candidate => candidate.Location.IsInSource)
            .Where(candidate => SolutionAnalysisRunner.MatchesDiagnosticFilter(candidate.Id, diagnosticFilter))
            .Where(static candidate => SupportedDiagnosticIds.Contains(candidate.Id))
            .OrderBy(static candidate => candidate.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Location.SourceSpan.Start);

        foreach (var diagnostic in candidates)
        {
            if (diagnostic.Location.SourceTree is null)
            {
                continue;
            }

            var document = solution.GetDocument(diagnostic.Location.SourceTree);
            if (document is null)
            {
                continue;
            }

            var codeFixProvider = codeFixProviders.SingleOrDefault(
                provider => provider.FixableDiagnosticIds.Contains(diagnostic.Id));
            if (codeFixProvider is null)
            {
                throw new InvalidOperationException(
                    $"The codemod apply mode does not have a code-fix provider for supported diagnostic '{diagnostic.Id}'.");
            }

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken);
            await codeFixProvider.RegisterCodeFixesAsync(context);

            if (actions.Count == 0)
            {
                // The code fix provider may decline to register a fix when the diagnostic's
                // target has already been partially fixed by another provider (e.g. BPMIG201
                // stripped the template arg, so BPMIG200 no longer has a second arg to remove).
                continue;
            }

            var operations = await actions[0].GetOperationsAsync(cancellationToken);
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().Single();
            EnsureCodeFixMadeProgress(solution, applyChangesOperation.ChangedSolution, diagnostic.Id);
            return applyChangesOperation.ChangedSolution;
        }

        return null;
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
