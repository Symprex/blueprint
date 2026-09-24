using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Messaging;

/// <summary>
/// Tests that <see cref="MessageOutboxModule"/> correctly generates message-outbox commit
/// code for command pipelines and does not contribute to query or background-task pipelines.
/// </summary>
public class MessageOutboxModuleTests
{
    // ── Pipeline plan tests ───────────────────────────────────────────

    [Fact]
    public void When_command_operation_then_pipeline_has_post_execution_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class PublishOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(PublishOrderCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.PublishOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(MessageOutboxModule)));
    }

    [Fact]
    public void When_query_operation_then_pipeline_has_no_message_outbox_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrdersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrdersQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrdersQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(MessageOutboxModule));
    }

    [Fact]
    public void When_background_task_then_pipeline_has_post_execution_contribution()
    {
        // Legacy gave background tasks the same outbox as commands. (This fixture used to import only Blueprint.Model,
        // where IBackgroundTask does not live, so it proved nothing about tasks.)
        var compilation = CreateCompilation(
            """
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class ProcessOrderTask : IBackgroundTask
            {
                public static Task HandleAsync(ProcessOrderTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessOrderTask", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(MessageOutboxModule)));
    }

    [Fact]
    public void When_command_has_DoNotPublishMessages_then_pipeline_has_no_outbox_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Messaging;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotPublishMessages]
            public partial class SilentCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(SilentCommand operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SilentCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(MessageOutboxModule));
    }

    [Fact]
    public void When_query_has_DoNotPublishMessages_then_reports_BPM001_diagnostic()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Messaging;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotPublishMessages]
            public partial class GetOrdersQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrdersQuery operation)
                {
                    return Task.FromResult("result");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrdersQuery", compilation);

        result.Diagnostics.Should().ContainSingle()
            .Which.Descriptor.Id.Should().Be("BPM001");
    }

    [Fact]
    public void When_background_task_has_DoNotPublishMessages_then_it_opts_out_without_a_diagnostic()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Messaging;
            using Blueprint.Tasks;
            using System.Threading.Tasks;

            namespace TestNamespace;

            [DoNotPublishMessages]
            public partial class ProcessOrderTask : IBackgroundTask
            {
                public static Task HandleAsync(ProcessOrderTask operation)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new MessageOutboxModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessOrderTask", compilation);

        result.Diagnostics.Should().BeEmpty("the attribute now has an effect on a background task");
        result.Value!.Contributions.Should().NotContain(
            static c => c.ContributorId == nameof(MessageOutboxModule));
    }

    // ── Pipeline snapshot test ────────────────────────────────────────

    [Fact]
    public async Task When_command_with_handler_then_pipeline_snapshot_includes_message_outbox_commit()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class PublishOrderCommand : ICommand<string>
                {
                    public string OrderId { get; set; }

                    public static Task<string> HandleAsync(PublishOrderCommand operation)
                    {
                        return Task.FromResult("published");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<OperationExecutorPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(OperationExecutorPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated");

        var outputText = new StringBuilder();
        foreach (var s in pipelineSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        await Verify(outputText)
            .UseDirectory("Snapshots");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "MessageOutboxModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
