using System.Diagnostics;
using Blueprint.Migration.Analyzers;
using Microsoft.CodeAnalysis;

namespace Blueprint.Codemod;

/// <summary>
/// Builds a target solution in dry-run mode and collects the current migration diagnostics into the shared
/// <see cref="MigrationReport"/> contract so later apply-mode work can reuse the same deterministic report shape.
/// </summary>
public sealed class SolutionAnalysisRunner
{
    /// <summary>
    /// Runs a dry-run analysis for a solution and returns the current migration report.
    /// </summary>
    /// <param name="options">The solution-analysis options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collected migration report.</returns>
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
                .Where(diagnosticId => MatchesDiagnosticFilter(diagnosticId, options.DiagnosticFilter))
                .ToArray();

            if (buildResult.ExitCode != 0)
            {
                var nonMigrationDiagnosticIds = allDiagnosticIds
                    .Where(diagnosticId => !MatchesDiagnosticFilter(diagnosticId, options.DiagnosticFilter))
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

            return MigrationReport.CreateMigrationDiagnostics(descriptors);
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

        var buildOutput = await WaitForProcessOutputAsync(process, cancellationToken);

        if (!File.Exists(sarifPath))
        {
            throw new InvalidOperationException($"The codemod dry run did not produce the expected SARIF output at '{sarifPath}'.");
        }

        return new BuildResult(process.ExitCode, buildOutput.StandardOutput, buildOutput.StandardError);
    }

    private static async Task<BuildOutput> ReadProcessOutputAsync(Process process, CancellationToken cancellationToken)
    {
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        return new BuildOutput(
            await standardOutputTask,
            await standardErrorTask);
    }

    private static async Task<BuildOutput> WaitForProcessOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            var readOutputTask = ReadProcessOutputAsync(process, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return await readOutputTask;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<string>> ReadDiagnosticIdsAsync(string sarifPath, CancellationToken cancellationToken)
    {
        await using var sarifStream = File.OpenRead(sarifPath);
        return await SarifDiagnosticReader.ReadDiagnosticIdsAsync(sarifStream, cancellationToken);
    }

    /// <summary>
    /// Determines whether a diagnostic identifier matches the configured codemod filter.
    /// </summary>
    /// <param name="diagnosticId">The candidate diagnostic identifier.</param>
    /// <param name="diagnosticFilter">The configured filter such as <c>BPMIG*</c>.</param>
    /// <returns><see langword="true"/> when the diagnostic matches the filter; otherwise, <see langword="false"/>.</returns>
    internal static bool MatchesDiagnosticFilter(string diagnosticId, string diagnosticFilter)
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
            "BPMIG200" => MigrationDiagnosticDescriptors.LegacyFromResourceUsageRequiresMigration,
            "BPMIG201" => MigrationDiagnosticDescriptors.LegacyAuthPlaceholderUsageRequiresMigration,
            _ => throw new InvalidOperationException($"The codemod dry run does not yet know how to project '{diagnosticId}' into the migration report."),
        };
    }

    private sealed record BuildResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record BuildOutput(string StandardOutput, string StandardError);
}
