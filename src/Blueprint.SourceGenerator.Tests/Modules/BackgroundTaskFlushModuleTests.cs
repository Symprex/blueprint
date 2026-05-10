using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>BackgroundTaskFlushModule</c> contributes a post-execution render step
/// that flushes background tasks only after successful handler execution, with graceful
/// null-guarding when no <c>IBackgroundTaskScheduler</c> is registered.
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

        // BackgroundTaskFlushModule resolves the scheduler from DI
        pipelineSource.Should().Contain("IBackgroundTaskScheduler");

        // Only flushes when handler result was successful
        pipelineSource.Should().Contain("handlerResult.WasSuccessful");

        // Calls RunNowAsync to dispatch queued tasks
        pipelineSource.Should().Contain("RunNowAsync");
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

        // Scheduler is optional — null-guarded before calling
        pipelineSource.Should().Contain("backgroundTaskScheduler != null");
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

        // Background task flush applies to all operation types, not just commands
        pipelineSource.Should().Contain("IBackgroundTaskScheduler");
        pipelineSource.Should().Contain("RunNowAsync");
    }
}
