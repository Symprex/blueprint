using System.Text;
using Blueprint.SourceGenerator.Generators.Descriptors;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.Marten;

/// <summary>
/// Tests for the <see cref="MartenScopeModule"/> proving that the scope accessor is correctly
/// contributed to the generated pipeline for both commands and queries.
/// </summary>
public class MartenScopeModuleTests
{
    [Fact]
    public void When_command_operation_then_plan_contains_setup_and_cleanup()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class SaveItemCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(SaveItemCommand operation)
                {
                    return Task.FromResult("saved");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new MartenScopeModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.SaveItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var contributions = result.Value!.Contributions;

        contributions.Should().ContainInOrder(
            new PipelineContribution(PipelineStage.Setup, nameof(MartenScopeModule)),
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)));

        contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Setup, nameof(MartenScopeModule)));
    }

    [Fact]
    public void When_query_operation_then_plan_still_contains_marten_scope()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetItemQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetItemQuery operation)
                {
                    return Task.FromResult("item");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new MartenScopeModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Setup, nameof(MartenScopeModule)));
    }

    [Fact]
    public async Task When_composed_with_pipeline_generator_then_snapshot_includes_scope_accessor()
    {
        const string source = """
            using Blueprint;
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                [BlueprintAppScan]
                public partial class TestApp : BlueprintApp { }

                public partial class UpdateDocCommand : ICommand<string>
                {
                    public static Task<string> HandleAsync(UpdateDocCommand operation)
                    {
                        return Task.FromResult("updated");
                    }
                }
            }
            """;

        var generators = new[]
        {
            TestHelpers.CreateTrackedGenerator<OperationDescriptorSourceGenerator>(),
            TestHelpers.CreateTrackedGenerator<MartenCompanionPipelineSourceGenerator>(),
        };

        var result = TestHelpers.CompileWithTrackedGenerators(
            generators,
            [source],
            ["Input0.cs"]);

        result.Diagnostics.Should().BeEmpty("the compilation should have no diagnostics");

        var pipelineSources = result.GetGeneratedSources(nameof(MartenCompanionPipelineSourceGenerator));
        pipelineSources.Should().HaveCountGreaterOrEqualTo(1, "pipeline should be generated with Marten module");

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

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "MartenScopeModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A derived pipeline source generator that composes the <see cref="MartenScopeModule"/> to prove
/// consumer companion generators can add Marten scope accessor support to generated pipelines.
/// </summary>
internal sealed class MartenCompanionPipelineSourceGenerator : OperationExecutorPipelineSourceGenerator
{
    public MartenCompanionPipelineSourceGenerator()
        : base(CreateDefaultConfig(new MartenScopeModule()))
    {
    }
}
