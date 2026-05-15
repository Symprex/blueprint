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
public sealed class TransformationApplier
{
    private const int MaxFixIterations = 512;

    private static readonly ImmutableArray<string> SupportedDiagnosticIds =
    [
        "BPMIG001",
        "BPMIG002",
        "BPMIG003",
        "BPMIG400",
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

        EnsureMsBuildRegistered();

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

        var solutionPath = Path.GetFullPath(options.SolutionPath);
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        var analyzers = CreateAnalyzers();
        var codeFixProviders = CreateCodeFixProviders();
        var updatedSolution = solution;
        var iteration = 0;

        while (true)
        {
            if (iteration++ >= MaxFixIterations)
            {
                throw new InvalidOperationException(
                    $"The codemod apply mode exceeded {MaxFixIterations} fix iterations while applying supported migration diagnostics.");
            }

            var nextSolution = await ApplyNextSupportedCodeFixAsync(
                updatedSolution,
                analyzers,
                codeFixProviders,
                options.DiagnosticFilter,
                cancellationToken);
            if (nextSolution is null)
            {
                break;
            }

            updatedSolution = nextSolution;
        }

        if (!workspace.TryApplyChanges(updatedSolution))
        {
            throw new InvalidOperationException(
                $"The codemod apply mode could not write the supported transformations back to '{solutionPath}'.");
        }
    }

    private static async Task<Solution?> ApplyNextSupportedCodeFixAsync(
        Solution solution,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        ImmutableArray<CodeFixProvider> codeFixProviders,
        string diagnosticFilter,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                continue;
            }

            var diagnostics = await compilation
                .WithAnalyzers(analyzers)
                .GetAnalyzerDiagnosticsAsync(cancellationToken);
            var diagnostic = diagnostics
                .Where(static candidate => candidate.Location.IsInSource)
                .Where(candidate => SolutionAnalysisRunner.MatchesDiagnosticFilter(candidate.Id, diagnosticFilter))
                .Where(static candidate => SupportedDiagnosticIds.Contains(candidate.Id))
                .OrderBy(static candidate => candidate.Location.SourceTree?.FilePath, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.Location.SourceSpan.Start)
                .FirstOrDefault();

            if (diagnostic is null || diagnostic.Location.SourceTree is null)
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
                throw new InvalidOperationException(
                    $"The codemod apply mode could not register a code fix for supported diagnostic '{diagnostic.Id}'.");
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
