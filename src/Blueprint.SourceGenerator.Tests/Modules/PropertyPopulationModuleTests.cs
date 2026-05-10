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

        // PropertyPopulationModule resolves all IPropertyPopulator registrations from DI
        pipelineSource.Should().Contain("IPropertyPopulator");
        pipelineSource.Should().Contain("propertyPopulators");
        pipelineSource.Should().Contain("PopulateAsync");
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

        pipelineSource.Should().Contain("IPropertyPopulator");
        pipelineSource.Should().Contain("PopulateAsync");
    }

    [Fact]
    public void When_pipeline_generated_then_populators_guarded_by_length_check()
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

        // The generated code caches populators at construction time and guards with a length check
        pipelineSource.Should().Contain("_propertyPopulators.Length > 0");
    }
}
