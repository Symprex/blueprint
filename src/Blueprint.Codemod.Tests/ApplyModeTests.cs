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

            // BPMIG203: the pure auth-claim template is auto-fixed by dropping the resource key
            // template argument, while the non-auth {TenantId} template is left for manual review.
            updatedSource.Should().Contain("[Resource(\"Disclaimer\", \"Update\")]");
            updatedSource.Should().Contain("[Resource(\"Account\", \"Read\", \"/Tenants/{TenantId}\")]");

            // A mixed auth-claim + non-auth template must survive ApplyAsync's real batch fix-all
            // path with its arity untouched: every placeholder must be an auth claim before the
            // resource-key-template argument is dropped, and this template's first placeholder
            // alone would incorrectly pass an "any" predicate. BPMIG201 legitimately normalises the
            // old "{Auth:...}" colon syntax to "{Auth....}" dot syntax in the same pass, but BPMIG203
            // must still decline to drop the argument because of the trailing non-auth "{Id}".
            updatedSource.Should().NotContain("[Resource(\"User\", \"Read\")]");
            updatedSource.Should().Contain("[Resource(\"User\", \"Read\", \"/Tenants/{Auth.AccountId}/Users/{Id}\")]");

            var runner = new SolutionAnalysisRunner();
            var report = await runner.DryRunAsync(
                new SolutionAnalysisOptions(
                    solutionPath: solutionPath,
                    diagnosticFilter: "BPMIG*"));

            report.Report.Findings.Select(static finding => finding.DiagnosticId)
                .Should()
                .Equal("BPMIG100", "BPMIG101", "BPMIG200", "BPMIG203", "BPMIG302");
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
