using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class DryRunIdempotencyTests
{
    [Xunit.Fact]
    public async Task When_running_dry_run_twice_against_the_same_fixture_copy_then_stdout_artifacts_and_source_remain_stable()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        var outputDirectory = Path.Combine(fixtureRoot, "artifacts");
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");
            var programPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp", "Program.cs");
            var originalSource = await File.ReadAllTextAsync(programPath);

            using var firstStandardOutput = new StringWriter();
            using var firstStandardError = new StringWriter();
            var firstExitCode = await Program.RunAsync(
                [
                    "--solution",
                    solutionPath,
                    "--diagnostic-filter",
                    "BPMIG*",
                    "--output",
                    outputDirectory,
                ],
                RunApplyAsync,
                RunDryRunAsync,
                WriteArtifactsAsync,
                firstStandardOutput,
                firstStandardError,
                CancellationToken.None);

            var jsonArtifactPath = Path.Combine(outputDirectory, "codemod-report.json");
            var markdownArtifactPath = Path.Combine(outputDirectory, "codemod-report.md");
            var sourceAfterFirstRun = await File.ReadAllTextAsync(programPath);
            var jsonAfterFirstRun = await File.ReadAllTextAsync(jsonArtifactPath);
            var markdownAfterFirstRun = await File.ReadAllTextAsync(markdownArtifactPath);

            using var secondStandardOutput = new StringWriter();
            using var secondStandardError = new StringWriter();
            var secondExitCode = await Program.RunAsync(
                [
                    "--solution",
                    solutionPath,
                    "--diagnostic-filter",
                    "BPMIG*",
                    "--output",
                    outputDirectory,
                ],
                RunApplyAsync,
                RunDryRunAsync,
                WriteArtifactsAsync,
                secondStandardOutput,
                secondStandardError,
                CancellationToken.None);

            firstExitCode.Should().Be(0);
            secondExitCode.Should().Be(0);
            firstStandardError.ToString().Should().BeEmpty();
            secondStandardError.ToString().Should().BeEmpty();
            secondStandardOutput.ToString().Should().Be(firstStandardOutput.ToString());
            (await File.ReadAllTextAsync(jsonArtifactPath)).Should().Be(jsonAfterFirstRun);
            (await File.ReadAllTextAsync(markdownArtifactPath)).Should().Be(markdownAfterFirstRun);
            sourceAfterFirstRun.Should().Be(originalSource);
            (await File.ReadAllTextAsync(programPath)).Should().Be(originalSource);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static Task RunApplyAsync(ApplyModeOptions options, CancellationToken cancellationToken)
    {
        var applier = new TransformationApplier();
        return applier.ApplyAsync(options, cancellationToken);
    }

    private static Task<Blueprint.Migration.Analyzers.MigrationReport> RunDryRunAsync(
        SolutionAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var runner = new SolutionAnalysisRunner();
        return runner.DryRunAsync(options, cancellationToken);
    }

    private static Task WriteArtifactsAsync(
        Blueprint.Migration.Analyzers.MigrationReport report,
        string directory,
        CancellationToken cancellationToken)
    {
        var artifactWriter = new CodemodArtifactWriter();
        return artifactWriter.WriteAsync(report, directory, cancellationToken);
    }

    private static string CreateWorkingFixtureRoot()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceFixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            "SimpleBlueprintApp");
        var workingFixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            $"SimpleBlueprintApp.DryRun.{Guid.NewGuid():N}");

        CopyDirectory(sourceFixtureRoot, workingFixtureRoot);
        return workingFixtureRoot;
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root for the dry-run idempotency fixture.");
        }

        return directory.FullName;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, childDestination);
        }
    }
}
