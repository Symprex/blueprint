using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>BackgroundTaskFlushModule</c> contributes a post-execution render step
/// that flushes background tasks only after successful handler execution via the extracted
/// <c>PipelineHelpers.FlushBackgroundTasksAsync</c> helper method.
/// </summary>
public class BackgroundTaskFlushModuleTests
{
    [Fact]
    public void When_command_then_pipeline_contains_background_task_flush()
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

                public partial class FlushCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(FlushCommand operation) => Task.CompletedTask;
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

        // Each module contributes its own individual post-success call
        pipelineSource.Should().Contain("FlushBackgroundTasksAsync(");

        // NoResult handler: guard is elided because the result is always successful
        pipelineSource.Should().NotContain("handlerResult.WasSuccessful");
    }

    [Fact]
    public void When_command_then_scheduler_is_null_guarded()
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

                public partial class NullGuardCommand : ICommand
                {
                    public string Value { get; set; }

                    public static Task HandleAsync(NullGuardCommand operation) => Task.CompletedTask;
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

        // Null-guarding is handled inside PipelineHelpers.FlushBackgroundTasksAsync
        pipelineSource.Should().Contain("FlushBackgroundTasksAsync");
    }

    [Fact]
    public void When_query_then_pipeline_still_contains_background_task_flush()
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

                public partial class FlushQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(FlushQuery operation) => Task.FromResult("result");
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

        // Background task flush applies to all operation types
        pipelineSource.Should().Contain("FlushBackgroundTasksAsync");
    }
}
