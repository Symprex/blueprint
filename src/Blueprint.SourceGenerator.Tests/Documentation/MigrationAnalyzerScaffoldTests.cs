using FluentAssertions;
using FluentAssertions.Execution;

namespace Blueprint.SourceGenerator.Tests.Documentation;

public sealed class MigrationAnalyzerScaffoldTests
{
    [Fact]
    public void When_reading_repo_layout_then_migration_analyzer_scaffold_and_bpmig_reservation_exist()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionContents = File.ReadAllText(Path.Combine(repositoryRoot, "Blueprint.sln"));
        var compileTimeValidationGuidance = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "compile-time-validation.md"));

        using var _ = new AssertionScope();

        File.Exists(Path.Combine(repositoryRoot, "src", "Blueprint.Migration.Analyzers", "Blueprint.Migration.Analyzers.csproj"))
            .Should()
            .BeTrue();
        File.Exists(Path.Combine(repositoryRoot, "src", "Blueprint.Migration.Analyzers.Tests", "Blueprint.Migration.Analyzers.Tests.csproj"))
            .Should()
            .BeTrue();
        solutionContents.Should().Contain("Blueprint.Migration.Analyzers");
        solutionContents.Should().Contain("Blueprint.Migration.Analyzers.Tests");
        compileTimeValidationGuidance.Should().Contain("Current reservation:");
        compileTimeValidationGuidance.Should().Contain("`BPMIG001`-`BPMIG099`");
        compileTimeValidationGuidance.Should().Contain("`BPMIG300`-`BPMIG399`");
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
