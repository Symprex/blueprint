using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>FailureOutcomesModule</c> projects exception translator policy information
/// into the generated descriptor as <c>OperationFailureOutcomesFeatureData</c>.
/// </summary>
public class FailureOutcomesModuleTests
{
    [Fact]
    public void When_default_policy_then_descriptor_uses_well_known_feature_data()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class DefaultPolicyCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(DefaultPolicyCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // Default policy uses the well-known shared static instance to avoid per-descriptor allocation
        descriptorSource.Should().Contain("WellKnownFeatureData.DefaultFailureOutcomes");
    }

    [Fact]
    public void When_command_then_descriptor_has_failure_outcomes()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class OutcomeCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(OutcomeCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var descriptorSources = result.GetGeneratedSources(nameof(OperationDescriptorSourceGenerator));
        var descriptorSource = string.Join("\n", descriptorSources.Select(static s => s.SourceText));

        // All operations should have failure outcomes registered
        descriptorSource.Should().Contain("FailureOutcome");
    }
}
