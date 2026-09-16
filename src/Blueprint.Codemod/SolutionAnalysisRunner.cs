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
    /// <returns>The collected analysis result with migration report and instance counts.</returns>
    public async Task<AnalysisResult> DryRunAsync(SolutionAnalysisOptions options, CancellationToken cancellationToken = default)
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

            var sarifFileCount = Directory.GetFiles(sarifDir, "*.sarif").Length;
            if (sarifFileCount == 0)
            {
                throw new InvalidOperationException(
                    $"The codemod dry run for '{solutionPath}' produced no SARIF output. " +
                    $"The analyzer injection may have failed. Exit code: {buildResult.ExitCode}.{Environment.NewLine}" +
                    $"{buildResult.StandardOutput}{Environment.NewLine}{buildResult.StandardError}");
            }

            var allDiagnosticIds = await ReadDiagnosticIdsFromDirectoryAsync(sarifDir, cancellationToken);
            var allDiagnosticCounts = await ReadDiagnosticCountsFromDirectoryAsync(sarifDir, cancellationToken);
            var diagnosticIds = allDiagnosticIds
                .Where(diagnosticId => MatchesDiagnosticFilter(diagnosticId, options.DiagnosticFilter))
                .ToArray();

            if (buildResult.ExitCode != 0 && diagnosticIds.Length == 0)
            {
                var nonMigrationDiagnosticIds = allDiagnosticIds
                    .Where(diagnosticId => !MatchesDiagnosticFilter(diagnosticId, options.DiagnosticFilter))
                    .ToArray();

                var unexpectedDiagnostics = nonMigrationDiagnosticIds.Length == 0
                    ? "none"
                    : string.Join(", ", nonMigrationDiagnosticIds);

                throw new InvalidOperationException(
                    $"The codemod dry run could not build '{solutionPath}'. Exit code: {buildResult.ExitCode}. Non-migration diagnostics: {unexpectedDiagnostics}.{Environment.NewLine}{buildResult.StandardOutput}{Environment.NewLine}{buildResult.StandardError}");
            }

            var descriptors = diagnosticIds
                .Select(static diagnosticId => ResolveDescriptor(diagnosticId))
                .Where(static descriptor => descriptor is not null)
                .Cast<DiagnosticDescriptor>()
                .ToArray();

            var report = MigrationReport.CreateMigrationDiagnostics(descriptors);

            // Only include counts for diagnostics that passed the filter
            var filteredCounts = allDiagnosticCounts
                .Where(kvp => MatchesDiagnosticFilter(kvp.Key, options.DiagnosticFilter))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return new AnalysisResult(report, filteredCounts);
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

        // MSBuildLocator.RegisterDefaults() sets MSBUILD_EXE_PATH, MSBuildExtensionsPath, and
        // MSBuildSDKsPath in the current process. If the apply-mode workflow registered MSBuild
        // before this dry-run, those variables would poison the child dotnet build process and
        // cause it to fail silently (0 errors, 0 warnings, exit code 1 in seconds).
        processStartInfo.Environment.Remove("MSBUILD_EXE_PATH");
        processStartInfo.Environment.Remove("MSBuildExtensionsPath");
        processStartInfo.Environment.Remove("MSBuildSDKsPath");

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

    private static async Task<Dictionary<string, int>> ReadDiagnosticCountsFromDirectoryAsync(string sarifDir, CancellationToken cancellationToken)
    {
        var sarifFiles = Directory.GetFiles(sarifDir, "*.sarif");
        var aggregated = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sarifFile in sarifFiles)
        {
            try
            {
                await using var stream = File.OpenRead(sarifFile);
                var fileCounts = await SarifDiagnosticReader.ReadDiagnosticCountsAsync(stream, cancellationToken);
                foreach (var (id, count) in fileCounts)
                {
                    aggregated[id] = aggregated.TryGetValue(id, out var current) ? current + count : count;
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                // Skip malformed SARIF files from projects that failed to compile cleanly.
            }
        }

        return aggregated;
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

    /// <summary>
    /// Maps a diagnostic ID collected from SARIF output back to its <see cref="DiagnosticDescriptor"/> so it can be
    /// projected into the shared <see cref="MigrationReport"/>. Unknown diagnostics resolve to <see langword="null"/>
    /// and are silently skipped, because they may come from the target solution's own analyzers — but every ID
    /// exposed by <see cref="MigrationDiagnosticDescriptors"/> must have an arm here, or that diagnostic family
    /// never reaches the dry-run report, <c>--mode review</c> output, or <c>codemod-report.json</c>/<c>.md</c>,
    /// no matter how many findings it produces. <c>internal</c> for direct exhaustiveness testing from
    /// <c>Blueprint.Codemod.Tests</c>.
    /// </summary>
    /// <param name="diagnosticId">The reserved BPMIG diagnostic ID collected from SARIF output.</param>
    /// <returns>The matching descriptor, or <see langword="null"/> for an unrecognised ID.</returns>
    internal static DiagnosticDescriptor? ResolveDescriptor(string diagnosticId)
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
            "BPMIG202" => MigrationDiagnosticDescriptors.AbstractGenericBaseImplementsOperation,
            "BPMIG203" => MigrationDiagnosticDescriptors.LegacyResourceArityRequiresMigration,
            "BPMIG300" => MigrationDiagnosticDescriptors.LegacyNamespaceUsing,
            "BPMIG301" => MigrationDiagnosticDescriptors.LegacyMiddlewareBuilderUsage,
            "BPMIG302" => MigrationDiagnosticDescriptors.LegacyBlueprintApiBuilderUsage,
            "BPMIG303" => MigrationDiagnosticDescriptors.LegacyNamespaceRemoved,
            "BPMIG400" => MigrationDiagnosticDescriptors.OperationMissingPartialKeyword,
            "BPMIG401" => MigrationDiagnosticDescriptors.ResourceMissingPartialKeyword,
            "BPMIG402" => MigrationDiagnosticDescriptors.RelocatedTypeNeedUsing,
            _ => null, // Unknown diagnostics are silently skipped — they may come from the target solution's own analyzers.
        };
    }

    private sealed record BuildResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record BuildOutput(string StandardOutput, string StandardError);

    /// <summary>
    /// Contains the migration report together with per-diagnostic instance counts collected from SARIF output.
    /// </summary>
    /// <param name="Report">The canonical migration report with distinct diagnostic findings.</param>
    /// <param name="InstanceCounts">
    /// A dictionary mapping diagnostic IDs to their total occurrence count across all projects.
    /// Only includes diagnostics that passed the codemod filter.
    /// </param>
    public sealed record AnalysisResult(MigrationReport Report, IReadOnlyDictionary<string, int> InstanceCounts);
}
