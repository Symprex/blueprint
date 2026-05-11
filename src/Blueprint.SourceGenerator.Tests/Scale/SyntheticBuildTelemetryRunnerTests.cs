using System.Diagnostics;
using Blueprint.SourceGenerator.Artifacts;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Scale;

public class SyntheticBuildTelemetryRunnerTests
{
    [Fact]
    public async Task When_running_capture_script_then_all_scenarios_emit_binlogs_and_manifest_contracts()
    {
        var scriptPath = FindCaptureScriptPath();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Blueprint-SyntheticBuildTelemetry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await RunCaptureScriptAsync(scriptPath, outputDirectory, operationCount: 5);

            AssertCaptureDirectory(outputDirectory, "cold-build");
            AssertCaptureDirectory(outputDirectory, "no-op-build");
            AssertCaptureDirectory(outputDirectory, "design-time-build", expectedIsDesignTimeBuild: true);
            AssertCaptureDirectory(outputDirectory, "single-edit-build", expectedMutationSourcePath: @"Operations\SyntheticOperation005.cs");
        }
        finally
        {
            TryDeleteOutputDirectory(outputDirectory);
        }
    }

    private static void AssertCaptureDirectory(
        string outputDirectory,
        string captureName,
        string? expectedMutationSourcePath = null,
        bool expectedIsDesignTimeBuild = false)
    {
        var captureDirectory = Path.Combine(outputDirectory, captureName);
        File.Exists(Path.Combine(captureDirectory, "msbuild.binlog")).Should().BeTrue();
        File.Exists(Path.Combine(captureDirectory, "timing-summary.json")).Should().BeTrue();

        var manifestPath = Path.Combine(captureDirectory, ".blueprint", "manifests", "build-telemetry.manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        var manifest = GeneratedManifestEnvelope.FromJson(File.ReadAllText(manifestPath));
        manifest.ArtifactName.Should().Be("build-telemetry");
        manifest.RelativePath.Should().Be(".blueprint/manifests/build-telemetry.manifest.json");

        var payload = (JObject)manifest.Sections["synthetic-build-harness"].Payload;
        ((JObject)payload["capture"]!)["captureName"]!.Value<string>().Should().Be(captureName);
        ((JObject)payload["capture"]!)["isDesignTimeBuild"]!.Value<bool>().Should().Be(expectedIsDesignTimeBuild);
        ((JObject)payload["capture"]!)["durationMs"]!.Value<long>().Should().BeGreaterThanOrEqualTo(0);

        var timingSummary = JObject.Parse(File.ReadAllText(Path.Combine(captureDirectory, "timing-summary.json")));
        timingSummary["captureName"]!.Value<string>().Should().Be(captureName);
        timingSummary["durationMs"]!.Value<long>().Should().BeGreaterThanOrEqualTo(0);

        if (expectedMutationSourcePath is null)
        {
            payload["mutation"].Should().BeNull();
        }
        else
        {
            ((JObject)payload["mutation"]!)["sourcePath"]!.Value<string>().Should().Be(expectedMutationSourcePath);
        }
    }

    private static async Task RunCaptureScriptAsync(string scriptPath, string outputDirectory, int operationCount)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -OutputDirectory \"{outputDirectory}\" -OperationCount {operationCount}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationSource.Token);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationSource.Token);

        try
        {
            await process.WaitForExitAsync(cancellationSource.Token);
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

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        process.ExitCode.Should().Be(0, $"runner output:{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static void TryDeleteOutputDirectory(string outputDirectory)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(outputDirectory))
                {
                    return;
                }

                Directory.Delete(outputDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(200);
            }
            catch (IOException exception)
            {
                Console.WriteLine($"Could not delete synthetic telemetry output '{outputDirectory}': {exception.Message}");
                return;
            }
            catch (UnauthorizedAccessException exception)
            {
                Console.WriteLine($"Could not delete synthetic telemetry output '{outputDirectory}': {exception.Message}");
                return;
            }
        }
    }

    private static string FindCaptureScriptPath()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            var candidatePath = Path.Combine(currentDirectory.FullName, "migration", "performance-and-scale", "Capture-SyntheticBuildTelemetry.ps1");
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not find Capture-SyntheticBuildTelemetry.ps1 from the test output directory.");
    }
}
