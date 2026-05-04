using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules;

/// <summary>
/// Proves that companion generator packages can compose <see cref="BlueprintSourceGenerationConfig"/> with
/// custom <see cref="IBlueprintSourceGeneratorModule"/> implementations and have their pipeline contributions
/// appear in the generated output. This validates the consumer-space extensibility seam.
/// </summary>
public class ConsumerModuleCompositionTests
{
    // ── Plan tests ────────────────────────────────────────────────────

    [Fact]
    public void When_custom_module_contributes_setup_stage_then_plan_contains_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class CustomCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(CustomCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestTenantEnrichmentModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.CustomCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Setup, nameof(TestTenantEnrichmentModule)));
    }

    [Fact]
    public void When_custom_module_has_dependency_then_ordering_is_respected()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class OrderedCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(OrderedCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new TestPostHandlerModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.OrderedCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var contributions = result.Value!.Contributions;

        contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)),
            new PipelineContribution(PipelineStage.PostExecution, nameof(TestPostHandlerModule)));
    }

    [Fact]
    public void When_custom_module_missing_dependency_then_diagnostic_is_reported()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class OrphanCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(OrphanCommand operation)
                {
                    return Task.FromResult("done");
                }
            }
            """);

        // TestPostHandlerModule depends on HandlerExecutionModule, but we don't include it
        var config = new BlueprintSourceGenerationConfig(
        [
            new TestPostHandlerModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.OrphanCommand", compilation);

        result.Diagnostics.Should().NotBeEmpty("missing dependency should report BPMOD002");
        result.Diagnostics.Should().Contain(
            static d => d.Descriptor.Id == "BPMOD002");
    }

    // ── Snapshot test ─────────────────────────────────────────────────

    [Fact]
    public async Task When_custom_module_contributes_then_generated_pipeline_includes_custom_code()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class EnrichedCommand : ICommand<string>
                {
                    public static Task<string> HandleAsync(EnrichedCommand operation)
                    {
                        return Task.FromResult("enriched");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<TestCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(TestCompanionPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated with custom module");

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
            assemblyName: "ConsumerModuleCompositionTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A custom module that simulates a consumer-space tenant enrichment step contributed at Setup stage.
/// Proves that companion generators can inject setup-time service resolution and context population.
/// </summary>
internal sealed class TestTenantEnrichmentModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(TestTenantEnrichmentModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

    /// <inheritdoc />
    public string ContributorId => nameof(TestTenantEnrichmentModule);

    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        context.AddRenderStep(PipelineStage.Setup, RenderTenantEnrichment);
    }

    private static void RenderTenantEnrichment(PipelineRenderContext context)
    {
        context.WriteStatement("// Tenant enrichment contributed by consumer companion module");
        context.DeclareVariable(
            "tenantId",
            "context.Descriptor.OperationType.Name");
    }
}

/// <summary>
/// A custom module that simulates a consumer-space post-handler step that depends on handler execution.
/// Proves that consumer modules can declare dependency ordering on built-in modules.
/// </summary>
internal sealed class TestPostHandlerModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(TestPostHandlerModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Consumer;

    /// <inheritdoc />
    public string ContributorId => nameof(TestPostHandlerModule);

    /// <inheritdoc />
    public int Order => 800;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.AddRenderStep(PipelineStage.PostExecution, RenderPostHandler);
    }

    private static void RenderPostHandler(PipelineRenderContext context)
    {
        context.If(
            "handlerResult.WasSuccessful",
            branch =>
            {
                branch.WriteStatement("// Consumer post-handler enrichment");
                branch.WriteStatement("context.Activity?.AddTag(\"consumer.enriched\", true);");
            });
    }
}

/// <summary>
/// A derived pipeline source generator that proves the companion-package extensibility pattern:
/// a consumer-owned generator package can inherit from <see cref="OperationExecutorPipelineSourceGenerator"/>
/// and compose additional modules without copying the execution-generation logic.
/// </summary>
internal sealed class TestCompanionPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
{
    public TestCompanionPipelineSourceGenerator()
        : base(CreateDefaultConfig(new TestTenantEnrichmentModule()))
    {
    }
}
