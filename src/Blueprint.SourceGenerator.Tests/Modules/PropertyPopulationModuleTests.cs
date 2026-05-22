using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>PropertyPopulationModule</c> contributes the IPropertyPopulator resolution
/// and iteration code to every generated pipeline, even when no populators are registered.
/// The module runs at the Population stage after PropertyOwnership has bound HTTP data.
/// </summary>
public class PropertyPopulationModuleTests
{
    [Fact]
    public void When_command_then_pipeline_contains_property_populator_resolution()
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

                public partial class SimpleCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(SimpleCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().NotBeEmpty();

        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // PropertyPopulationModule uses base class PropertyPopulators property (no per-pipeline field)
        pipelineSource.Should().Contain("PropertyPopulators");

        // Delegates to PipelineHelpers.PopulatePropertiesAsync instead of inline code
        pipelineSource.Should().Contain("PipelineHelpers.PopulatePropertiesAsync");
    }

    [Fact]
    public void When_query_then_pipeline_contains_property_populator_resolution()
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

                public partial class SimpleQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(SimpleQuery operation) => Task.FromResult("result");
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        pipelineSource.Should().Contain("PropertyPopulators");
        pipelineSource.Should().Contain("PipelineHelpers.PopulatePropertiesAsync");
    }

    [Fact]
    public void When_pipeline_generated_then_populators_delegate_to_pipeline_helpers()
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

                public partial class GuardedCommand : ICommand
                {
                    public string Value { get; set; }

                    public static Task HandleAsync(GuardedCommand operation) => Task.CompletedTask;
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty();

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        var pipelineSource = string.Join("\n", pipelineSources.Select(static s => s.SourceText));

        // Pipelines delegate to the shared PipelineHelpers using pre-filtered populator array
        pipelineSource.Should().Contain("PipelineHelpers.PopulatePropertiesAsync(applicablePopulators, context)");

        // The inline Length == 1 / Length > 1 pattern should no longer appear in generated code
        pipelineSource.Should().NotContain("PropertyPopulators.Length == 1");
        pipelineSource.Should().NotContain("foreach (var populator");
    }
}
