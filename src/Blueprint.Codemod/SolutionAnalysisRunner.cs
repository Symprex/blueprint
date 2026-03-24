using System.Diagnostics;
using System.Text.Json;
using Blueprint.Migration.Analyzers;
using Microsoft.CodeAnalysis;

namespace Blueprint.Codemod;

/// <summary>
/// Builds a target solution in dry-run mode and collects the current activation diagnostics into the shared
/// <see cref="MigrationReport"/> contract so later apply-mode work can reuse the same deterministic report shape.
/// </summary>
public sealed class SolutionAnalysisRunner
{
    /// <summary>
    /// Runs a dry-run analysis for a solution and returns the activation-diagnostics report.
    /// </summary>
    /// <param name="options">The solution-analysis options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collected activation-diagnostics report.</returns>
    public async Task<MigrationReport> DryRunAsync(SolutionAnalysisOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var solutionPath = Path.GetFullPath(options.SolutionPath);
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException("The codemod dry run requires a solution file that exists on disk.", solutionPath);
        }

        var sarifPath = Path.Combine(
            Path.GetTempPath(),
            $"blueprint-codemod-{Path.GetFileNameWithoutExtension(solutionPath)}-{Guid.NewGuid():N}.sarif");

        try
        {
            var buildResult = await this.BuildSolutionWithSarifAsync(solutionPath, sarifPath, cancellationToken);

            var allDiagnosticIds = await this.ReadDiagnosticIdsAsync(sarifPath, cancellationToken);
            var diagnosticIds = allDiagnosticIds
                .Where(diagnosticId => MatchesFilter(diagnosticId, options.DiagnosticFilter))
                .ToArray();

            if (buildResult.ExitCode != 0)
            {
                var nonMigrationDiagnosticIds = allDiagnosticIds
                    .Where(diagnosticId => !MatchesFilter(diagnosticId, options.DiagnosticFilter))
                    .ToArray();

                if (diagnosticIds.Length == 0 || nonMigrationDiagnosticIds.Length > 0)
                {
                    var unexpectedDiagnostics = nonMigrationDiagnosticIds.Length == 0
                        ? "none"
                        : string.Join(", ", nonMigrationDiagnosticIds);

                    throw new InvalidOperationException(
                        $"The codemod dry run could not build '{solutionPath}'. Exit code: {buildResult.ExitCode}. Non-migration diagnostics: {unexpectedDiagnostics}.{Environment.NewLine}{buildResult.StandardOutput}{Environment.NewLine}{buildResult.StandardError}");
                }
            }

            var descriptors = diagnosticIds
                .Select(static diagnosticId => ResolveDescriptor(diagnosticId))
                .ToArray();

            return MigrationReport.CreateActivationDiagnostics(descriptors);
        }
        finally
        {
            if (File.Exists(sarifPath))
            {
                File.Delete(sarifPath);
            }
        }
    }

    private async Task<BuildResult> BuildSolutionWithSarifAsync(string solutionPath, string sarifPath, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory,
        };

        processStartInfo.ArgumentList.Add("build");
        processStartInfo.ArgumentList.Add(solutionPath);
        processStartInfo.ArgumentList.Add("--nologo");
        processStartInfo.ArgumentList.Add($"/p:ErrorLog={sarifPath};version=2");

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (!File.Exists(sarifPath))
        {
            throw new InvalidOperationException($"The codemod dry run did not produce the expected SARIF output at '{sarifPath}'.");
        }

        return new BuildResult(process.ExitCode, standardOutput, standardError);
    }

    private async Task<IReadOnlyList<string>> ReadDiagnosticIdsAsync(string sarifPath, CancellationToken cancellationToken)
    {
        await using var sarifStream = File.OpenRead(sarifPath);
        using var document = await JsonDocument.ParseAsync(sarifStream, cancellationToken: cancellationToken);

        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("runs", out var runsElement))
        {
            return Array.Empty<string>();
        }

        foreach (var runElement in runsElement.EnumerateArray())
        {
            if (!runElement.TryGetProperty("results", out var resultsElement))
            {
                continue;
            }

            foreach (var resultElement in resultsElement.EnumerateArray())
            {
                if (!resultElement.TryGetProperty("ruleId", out var ruleIdElement))
                {
                    continue;
                }

                var diagnosticId = ruleIdElement.GetString();
                if (diagnosticId is null)
                {
                    continue;
                }

                diagnosticIds.Add(diagnosticId);
            }
        }

        return diagnosticIds.ToArray();
    }

    private static bool MatchesFilter(string diagnosticId, string diagnosticFilter)
    {
        if (string.IsNullOrWhiteSpace(diagnosticFilter))
        {
            return true;
        }

        if (diagnosticFilter.EndsWith('*'))
        {
            return diagnosticId.StartsWith(
                diagnosticFilter.Substring(0, diagnosticFilter.Length - 1),
                StringComparison.Ordinal);
        }

        return string.Equals(diagnosticId, diagnosticFilter, StringComparison.Ordinal);
    }

    private static DiagnosticDescriptor ResolveDescriptor(string diagnosticId)
    {
        return diagnosticId switch
        {
            "BPMIG001" => MigrationDiagnosticDescriptors.RedundantAddOpenApiActivation,
            "BPMIG002" => MigrationDiagnosticDescriptors.RedundantAddHateoasLinksActivation,
            "BPMIG003" => MigrationDiagnosticDescriptors.RedundantAddAuthorisationActivation,
            "BPMIG100" => MigrationDiagnosticDescriptors.ConfiguredAddOpenApiRequiresMigration,
            "BPMIG101" => MigrationDiagnosticDescriptors.ConfiguredAddAuthenticationRequiresMigration,
            _ => throw new InvalidOperationException($"The codemod dry run does not yet know how to project '{diagnosticId}' into the activation-diagnostics report."),
        };
    }

    private sealed record BuildResult(int ExitCode, string StandardOutput, string StandardError);
}
