using System.Diagnostics;
using Blueprint.Migration.Analyzers;
using Microsoft.CodeAnalysis;

namespace Blueprint.Codemod;

/// <summary>
/// Builds a target solution in dry-run mode and collects the current migration diagnostics into the shared
/// <see cref="MigrationReport"/> contract so later apply-mode work can reuse the same deterministic report shape.
/// The runner injects the Blueprint migration analyzer DLL into the build so target solutions do not need to
/// reference it directly, and uses per-project SARIF output to avoid file corruption from parallel compilation.
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

        var sarifDir = Path.Combine(
            Path.GetTempPath(),
            $"blueprint-codemod-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(sarifDir);

            var buildResult = await this.BuildSolutionWithSarifAsync(solutionPath, sarifDir, cancellationToken);

            var allDiagnosticIds = await ReadDiagnosticIdsFromDirectoryAsync(sarifDir, cancellationToken);
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
                .Where(static descriptor => descriptor is not null)
                .Cast<DiagnosticDescriptor>()
                .ToArray();

            return MigrationReport.CreateMigrationDiagnostics(descriptors);
        }
        finally
        {
            if (Directory.Exists(sarifDir))
            {
                Directory.Delete(sarifDir, recursive: true);
            }
        }
    }

    private async Task<BuildResult> BuildSolutionWithSarifAsync(string solutionPath, string sarifDir, CancellationToken cancellationToken)
    {
        var analyzerDllPath = FindMigrationAnalyzerDllPath();
        var injectTargetsPath = Path.Combine(sarifDir, "blueprint-inject.targets");

        // The .targets file sets ErrorLog per-project (using $(MSBuildProjectName) for unique paths)
        // and injects the migration analyzer DLL so the target solution does not need to reference it.
        var targetsContent = $"""
                              <Project>
                                <PropertyGroup>
                                  <ErrorLog>{sarifDir}\$(MSBuildProjectName).sarif;version=2</ErrorLog>
                                </PropertyGroup>
                                <ItemGroup>
                                  <Analyzer Include="{analyzerDllPath}" />
                                </ItemGroup>
                              </Project>
                              """;

        await File.WriteAllTextAsync(injectTargetsPath, targetsContent, cancellationToken);

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
        processStartInfo.ArgumentList.Add("--no-incremental");
        processStartInfo.ArgumentList.Add($"/p:CustomAfterMicrosoftCSharpTargets={injectTargetsPath}");

        // Suppress TreatWarningsAsErrors so existing analyzer warnings (CA*, ASP*, etc.)
        // in the target solution do not cause a false build failure.
        processStartInfo.ArgumentList.Add("/p:TreatWarningsAsErrors=false");

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var buildOutput = await WaitForProcessOutputAsync(process, cancellationToken);

        return new BuildResult(process.ExitCode, buildOutput.StandardOutput, buildOutput.StandardError);
    }

    private static string FindMigrationAnalyzerDllPath()
    {
        var codemodAssemblyDir = Path.GetDirectoryName(typeof(SolutionAnalysisRunner).Assembly.Location)
                                 ?? throw new InvalidOperationException("Could not determine the codemod assembly directory.");

        var analyzerPath = Path.Combine(codemodAssemblyDir, "Blueprint.Migration.Analyzers.dll");
        if (File.Exists(analyzerPath))
        {
            return analyzerPath;
        }

        throw new FileNotFoundException(
            "Could not find the Blueprint.Migration.Analyzers.dll alongside the codemod assembly. " +
            "Ensure the Blueprint.Migration.Analyzers project is built before running the codemod.",
            analyzerPath);
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

    private static async Task<IReadOnlyList<string>> ReadDiagnosticIdsFromDirectoryAsync(string sarifDir, CancellationToken cancellationToken)
    {
        var sarifFiles = Directory.GetFiles(sarifDir, "*.sarif");
        if (sarifFiles.Length == 0)
        {
            return Array.Empty<string>();
        }

        var allIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var sarifFile in sarifFiles)
        {
            try
            {
                await using var stream = File.OpenRead(sarifFile);
                var ids = await SarifDiagnosticReader.ReadDiagnosticIdsAsync(stream, cancellationToken);
                foreach (var id in ids)
                {
                    allIds.Add(id);
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                // Skip malformed SARIF files from projects that failed to compile cleanly.
            }
        }

        return allIds.ToArray();
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

    private static DiagnosticDescriptor? ResolveDescriptor(string diagnosticId)
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
            "BPMIG300" => MigrationDiagnosticDescriptors.LegacyNamespaceUsing,
            "BPMIG301" => MigrationDiagnosticDescriptors.LegacyMiddlewareBuilderUsage,
            "BPMIG302" => MigrationDiagnosticDescriptors.LegacyBlueprintApiBuilderUsage,
            _ => null, // Unknown diagnostics are silently skipped — they may come from the target solution's own analyzers.
        };
    }

    private sealed record BuildResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record BuildOutput(string StandardOutput, string StandardError);
}
