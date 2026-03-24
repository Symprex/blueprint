using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class ApplyModeTests
{
    [Xunit.Fact]
    public async Task When_applying_against_a_fixture_copy_then_supported_auto_fixable_activations_are_removed_and_configuration_findings_remain()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");
            var programPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp", "Program.cs");
            var applier = new TransformationApplier();

            await applier.ApplyAsync(
                new ApplyModeOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            var updatedSource = await File.ReadAllTextAsync(programPath);
            updatedSource.Should().NotContain("builder.AddOpenApi();");
            updatedSource.Should().Contain("builder.AddOpenApi(options => options.Title = \"Legacy\");");
            updatedSource.Should().Contain("builder.AddAuthentication(_ => { });");

            var runner = new SolutionAnalysisRunner();
            var report = await runner.DryRunAsync(
                new SolutionAnalysisOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            report.Findings.Select(static finding => finding.DiagnosticId)
                .Should()
                .Equal("BPMIG100", "BPMIG101", "BPMIG200", "BPMIG201");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Xunit.Fact]
    public async Task When_applying_twice_against_the_same_fixture_copy_then_the_second_run_is_idempotent()
    {
        var fixtureRoot = CreateWorkingFixtureRoot();
        try
        {
            var solutionPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp.sln");
            var programPath = Path.Combine(fixtureRoot, "SimpleBlueprintApp", "Program.cs");
            var applier = new TransformationApplier();
            var options = new ApplyModeOptions(
                solutionPath: solutionPath,
                diagnosticFilter: "BPMIG*");

            await applier.ApplyAsync(options);
            var sourceAfterFirstApply = await File.ReadAllTextAsync(programPath);

            await applier.ApplyAsync(options);
            var sourceAfterSecondApply = await File.ReadAllTextAsync(programPath);

            sourceAfterSecondApply.Should().Be(sourceAfterFirstApply);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
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
            $"SimpleBlueprintApp.Apply.{Guid.NewGuid():N}");

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
            throw new InvalidOperationException("Could not locate the repository root for the apply-mode fixture.");
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
