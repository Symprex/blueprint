using FluentAssertions;
using FluentAssertions.Execution;

namespace Blueprint.SourceGenerator.Tests.Documentation;

public sealed class BenchmarkLaneGuidanceTests
{
    [Fact]
    public void When_reading_performance_guidance_then_contributor_docs_surface_pull_request_and_scheduled_manual_lanes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var performancePlan = File.ReadAllText(Path.Combine(repositoryRoot, "migration", "performance-and-scale", "plan.md"));
        var implementationChecklist = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "implementation-checklists.md"));
        var observabilityGuidance = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "observability-and-dev-experience.md"));
        var contributorSkill = File.ReadAllText(Path.Combine(repositoryRoot, ".agents", "skills", "extending-blueprint", "SKILL.md"));

        using var _ = new AssertionScope();

        performancePlan.Should().Contain("### Pull-request lane");
        performancePlan.Should().Contain("### Scheduled or manually-invoked lane");

        implementationChecklist.Should().Contain("pull-request lane");
        implementationChecklist.Should().Contain("scheduled/manual");

        observabilityGuidance.Should().Contain("pull-request lane");
        observabilityGuidance.Should().Contain("scheduled/manual");

        contributorSkill.Should().Contain("pull-request lane");
        contributorSkill.Should().Contain("scheduled/manual");
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "Blueprint.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root from the current test execution directory.");
    }
}
