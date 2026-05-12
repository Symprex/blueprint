using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves the <c>MessageOutboxModule</c> contributes a PostExecution render step for commands
/// that commits the message outbox on success, skips for queries, and respects
/// <c>[DoNotPublishMessages]</c> opt-out.
/// </summary>
public class MessageOutboxModuleTests
{
    [Fact]
    public void When_command_then_pipeline_contains_outbox_commit()
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

                public partial class OutboxCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(OutboxCommand operation) => Task.CompletedTask;
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

        // Delegates to extracted PipelineHelpers.CommitMessageOutboxAsync
        pipelineSource.Should().Contain("PipelineHelpers.CommitMessageOutboxAsync(context.ServiceProvider, context.OperationCancelled)");

        // Only commits when handler result was successful
        pipelineSource.Should().Contain("handlerResult.WasSuccessful");
    }

    [Fact]
    public void When_query_then_pipeline_does_not_contain_outbox_commit()
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

                public partial class OutboxQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(OutboxQuery operation) => Task.FromResult("result");
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

        // Queries should NOT have outbox commit — messaging is command-only
        pipelineSource.Should().NotContain("IMessageOutbox");
        pipelineSource.Should().NotContain("CommitAsync");
    }

    [Fact]
    public void When_command_has_do_not_publish_messages_then_pipeline_skips_outbox()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Messaging;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [DoNotPublishMessages]
                public partial class SilentCommand : ICommand
                {
                    public string Name { get; set; }

                    public static Task HandleAsync(SilentCommand operation) => Task.CompletedTask;
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

        // [DoNotPublishMessages] should suppress the outbox commit
        pipelineSource.Should().NotContain("IMessageOutbox");
        pipelineSource.Should().NotContain("CommitAsync");
    }

    [Fact]
    public void When_query_has_do_not_publish_messages_then_diagnostic_reported()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Messaging;
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                [DoNotPublishMessages]
                public partial class BadQuery : IQuery<string>
                {
                    public string Id { get; set; }

                    public static Task<string> HandleAsync(BadQuery operation) => Task.FromResult("result");
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(generators, [source], ["Input0.cs"]);

        // Should report BPM001 diagnostic for [DoNotPublishMessages] on non-command
        result.Diagnostics.Should().Contain(d => d.Id == "BPM001");
    }
}
