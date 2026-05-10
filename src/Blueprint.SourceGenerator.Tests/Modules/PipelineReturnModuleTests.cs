using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>PipelineReturnModule</c> contributes the final <c>return handlerResult</c>
/// statement at the Cleanup stage, ensuring the pipeline method has a well-formed return path.
/// </summary>
public class PipelineReturnModuleTests
{
    [Fact]
    public void When_command_then_pipeline_ends_with_return()
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

                public partial class ReturnCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(ReturnCommand operation) => Task.CompletedTask;
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

        // Pipeline returns the handler result wrapped for async
        pipelineSource.Should().Contain("return");
        pipelineSource.Should().Contain("handlerResult");
    }

    [Fact]
    public void When_query_then_pipeline_returns_operation_result()
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

                public partial class ReturnQuery : IQuery<int>
                {
                    public string Id { get; set; }

                    public static Task<int> HandleAsync(ReturnQuery operation) => Task.FromResult(42);
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

        // Queries also return via the PipelineReturnModule
        pipelineSource.Should().Contain("return");
        pipelineSource.Should().Contain("handlerResult");
    }
}
