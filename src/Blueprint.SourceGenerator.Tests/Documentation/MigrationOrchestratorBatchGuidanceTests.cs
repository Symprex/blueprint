using FluentAssertions;
using FluentAssertions.Execution;

namespace Blueprint.SourceGenerator.Tests.Documentation;

public sealed class MigrationOrchestratorBatchGuidanceTests
{
    [Fact]
    public void When_reading_migration_orchestrator_guidance_then_launcher_and_docs_require_multi_slice_batches()
    {
        var repositoryRoot = FindRepositoryRoot();
        var launcherScript = File.ReadAllText(Path.Combine(repositoryRoot, "Start-NextBlueprintMigrationPhase.ps1"));
        var orchestratorPrompt = File.ReadAllText(Path.Combine(repositoryRoot, "migration", "next-phase-orchestrator-prompt.md"));
        var agentPlaybook = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "ai-agent-playbook.md"));
        var copilotInstructions = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "copilot-instructions.md"));
        var migrationPlan = File.ReadAllText(Path.Combine(repositoryRoot, "migration", "plan.md"));

        using var _ = new AssertionScope();

        launcherScript.Should().Contain("max_related_slices_per_invocation: 5");
        launcherScript.Should().Contain("incorporate any review feedback");

        orchestratorPrompt.Should().Contain("up to 5 related slices");
        orchestratorPrompt.Should().Contain("Implement, verify, produce a visual review document, incorporate any review feedback, and commit each completed slice before moving to the next one");
        orchestratorPrompt.Should().Contain("incorporate any review feedback");
        orchestratorPrompt.Should().Contain("absolutely CRITICAL");
        orchestratorPrompt.Should().Contain("different sub-agents");

        agentPlaybook.Should().Contain("up to 5 related slices");
        copilotInstructions.Should().Contain("up to 5 related slices");
        migrationPlan.Should().Contain("up to 5 related slices");
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
