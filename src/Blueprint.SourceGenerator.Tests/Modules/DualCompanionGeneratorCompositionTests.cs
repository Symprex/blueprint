using System.Text;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves that a consumer package shipping both a companion descriptor generator and a companion pipeline generator
/// can compose them in the same compilation without conflicts. The descriptor generator adds "messaging" host exposure
/// while the pipeline generator conditionally adds messaging-specific pipeline steps for <c>IMessageConsumer</c>
/// operations only. This validates the full consumer-package story at compile time.
/// </summary>
public class DualCompanionGeneratorCompositionTests
{
    // ── Plan tests ────────────────────────────────────────────────────

    [Fact]
    public void When_messaging_pipeline_module_sees_IMessageConsumer_then_contributes_messaging_steps()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
            {
                public static Task<string> HandleAsync(ProcessOrderMessage operation)
                {
                    return Task.FromResult("processed");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingPipelineModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.ProcessOrderMessage", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Setup, nameof(TestMessagingPipelineModule)),
            "messaging pipeline module should contribute a Setup step for IMessageConsumer operations");
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.PostExecution, nameof(TestMessagingPipelineModule)),
            "messaging pipeline module should contribute a PostExecution step for IMessageConsumer operations");
    }

    [Fact]
    public void When_messaging_pipeline_module_sees_non_IMessageConsumer_then_does_not_contribute()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public interface IMessageConsumer { }

            public partial class GetOrderStatus : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderStatus operation)
                {
                    return Task.FromResult("status");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestMessagingPipelineModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrderStatus", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().NotContain(
            c => c.ContributorId == nameof(TestMessagingPipelineModule),
            "messaging pipeline module should NOT contribute for non-IMessageConsumer operations");
    }

    // ── Dual generator compilation test ──────────────────────────────

    [Fact]
    public async Task When_dual_companion_generators_run_on_same_compilation_then_no_conflicts_and_correct_output()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public interface IMessageConsumer { }

                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class ProcessOrderMessage : ICommand<string>, IMessageConsumer
                {
                    public static Task<string> HandleAsync(ProcessOrderMessage operation)
                    {
                        return Task.FromResult("processed");
                    }
                }

                public partial class GetOrderStatus : IQuery<string>
                {
                    public static Task<string> HandleAsync(GetOrderStatus operation)
                    {
                        return Task.FromResult("status");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<TestMessagingCompanionDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<TestMessagingCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics from either generator");

        // Verify descriptor generator produced output
        var descriptorSources = result.GetGeneratedSources(nameof(TestMessagingCompanionDescriptorSourceGenerator));
        descriptorSources.Should().HaveCountGreaterOrEqualTo(1,
            "the companion descriptor generator should produce output");

        // Verify pipeline generator produced output
        var pipelineSources = result.GetGeneratedSources(nameof(TestMessagingCompanionPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1,
            "the companion pipeline generator should produce output");

        // Verify messaging steps only appear in the IMessageConsumer operation pipeline
        var messagingPipeline = pipelineSources
            .FirstOrDefault(s => s.HintName.Contains("ProcessOrderMessage"));
        messagingPipeline.Should().NotBeNull("pipeline should be generated for ProcessOrderMessage");
        messagingPipeline!.SourceText.Should().Contain("[MessagingPipeline]",
            "ProcessOrderMessage (IMessageConsumer) should have messaging pipeline steps");

        var nonMessagingPipeline = pipelineSources
            .FirstOrDefault(s => s.HintName.Contains("GetOrderStatus"));
        nonMessagingPipeline.Should().NotBeNull("pipeline should be generated for GetOrderStatus");
        nonMessagingPipeline!.SourceText.Should().NotContain("[MessagingPipeline]",
            "GetOrderStatus (non-IMessageConsumer) should NOT have messaging pipeline steps");

        // Build combined snapshot of all generated sources from both generators
        var outputText = new StringBuilder();
        outputText.AppendLine("=== Descriptor Generator Output ===");
        outputText.AppendLine();
        foreach (var s in descriptorSources.OrderBy(static s => s.HintName))
        {
            outputText.AppendLine(s.HintName + ":");
            outputText.AppendLine();
            outputText.AppendLine(s.SourceText);
            outputText.AppendLine();
        }

        outputText.AppendLine("=== Pipeline Generator Output ===");
        outputText.AppendLine();
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
            assemblyName: "DualCompanionGeneratorCompositionTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A consumer-space pipeline module that conditionally contributes messaging-specific pipeline steps
/// for operations implementing <c>IMessageConsumer</c>. Proves that companion pipeline generators
/// can add targeted behavior based on marker interfaces, following the <see cref="TasksHostModule"/> pattern
/// for conditional contribution.
/// </summary>
internal sealed class TestMessagingPipelineModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    private const string MessageConsumerInterfaceName = "IMessageConsumer";

    /// <inheritdoc />
    public string ModuleId => nameof(TestMessagingPipelineModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

    /// <inheritdoc />
    public string ContributorId => nameof(TestMessagingPipelineModule);

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        if (!IsMessageConsumer(context.Operation.TypeSymbol))
        {
            return;
        }

        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();
        context.AddRenderStep(PipelineStage.Setup, RenderMessageDeserialization);
        context.AddRenderStep(PipelineStage.PostExecution, RenderMessageAcknowledgment);
    }

    private static bool IsMessageConsumer(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == MessageConsumerInterfaceName);
    }

    private static void RenderMessageDeserialization(PipelineRenderContext context)
    {
        context.WriteStatement("// [MessagingPipeline] Deserialize message envelope");
        context.WriteStatement("context.Activity?.AddTag(\"messaging.deserialized\", true);");
    }

    private static void RenderMessageAcknowledgment(PipelineRenderContext context)
    {
        context.WriteBlankLine();
        context.If(
            "handlerResult.WasSuccessful",
            branch =>
            {
                branch.WriteStatement("// [MessagingPipeline] Acknowledge message on success");
                branch.WriteStatement("context.Activity?.AddTag(\"messaging.acknowledged\", true);");
            });
    }
}

/// <summary>
/// A derived pipeline source generator that composes the consumer messaging pipeline module into the default
/// pipeline configuration. Combined with <see cref="TestMessagingCompanionDescriptorSourceGenerator"/>, this proves
/// the full dual-generator consumer package pattern.
/// </summary>
internal sealed class TestMessagingCompanionPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
{
    public TestMessagingCompanionPipelineSourceGenerator()
        : base(CreateDefaultConfig(new TestMessagingPipelineModule()))
    {
    }
}
