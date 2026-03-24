using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class ActivationDiagnosticsReportCollectionTests
{
    [Xunit.Fact]
    public async Task When_dry_running_against_a_fixture_solution_then_emits_the_activation_diagnostics_report()
    {
        var runner = new SolutionAnalysisRunner();

        var report = await runner.DryRunAsync(
            new SolutionAnalysisOptions(
                solutionPath: GetFixtureSolutionPath(),
                diagnosticFilter: "BPMIG*"));

        report.SchemaVersion.Should().Be(1);
        report.ReportType.Should().Be("migration-diagnostics");
        report.Findings.Should().HaveCount(5);
        report.Findings.Select(static finding => (finding.DiagnosticId, finding.Category))
            .Should()
            .Equal(
                ("BPMIG001", "auto-fixable"),
                ("BPMIG100", "configuration required"),
                ("BPMIG101", "configuration required"),
                ("BPMIG200", "consumer-extension rewrite required"),
                ("BPMIG201", "consumer-extension rewrite required"));
    }

    [Xunit.Fact]
    public async Task When_dry_running_against_a_solution_with_non_migration_build_errors_then_throws()
    {
        var runner = new SolutionAnalysisRunner();

        var act = async () => await runner.DryRunAsync(
            new SolutionAnalysisOptions(
                solutionPath: GetBrokenFixtureSolutionPath(),
                diagnosticFilter: "BPMIG*"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not build*");
    }

    private static string GetFixtureSolutionPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root for the codemod fixture solution.");
        }

        return Path.Combine(
            directory.FullName,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            "SimpleBlueprintApp",
            "SimpleBlueprintApp.sln");
    }

    private static string GetBrokenFixtureSolutionPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Blueprint.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate the repository root for the codemod fixture solution.");
        }

        return Path.Combine(
            directory.FullName,
            "src",
            "Blueprint.Codemod.Tests",
            "TestFixtures",
            "BrokenBlueprintApp",
            "BrokenBlueprintApp.sln");
    }
}
