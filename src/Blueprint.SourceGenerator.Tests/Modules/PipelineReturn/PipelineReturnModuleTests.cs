using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.PipelineReturn;

/// <summary>
/// Tests that <see cref="PipelineReturnModule"/> contributes the final return statement
/// to the Cleanup pipeline stage and correctly depends on handler-result.
/// </summary>
public class PipelineReturnModuleTests
{
    [Fact]
    public void When_handler_exists_then_pipeline_has_cleanup_return_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrderQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Cleanup, nameof(PipelineReturnModule)));
    }

    [Fact]
    public void When_handler_exists_then_pipeline_return_depends_on_handler_execution()
    {
        var module = new PipelineReturnModule();

        module.DependsOn.Should().Contain(nameof(HandlerExecutionModule),
            "PipelineReturnModule must run after HandlerExecutionModule produces handler-result");
    }

    [Fact]
    public void When_handler_exists_then_pipeline_return_requires_handler_result_variable()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class GetOrderQuery : IQuery<string>
            {
                public static Task<string> HandleAsync(GetOrderQuery operation)
                {
                    return Task.FromResult("ok");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.GetOrderQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        result.Value!.VariableRegistry.Requirements.Should().Contain(
            static v => v.Key.Purpose == HandlerExecutionModule.HandlerResultPurpose);
    }

    [Fact]
    public void When_pipeline_return_is_at_max_order_then_it_runs_last()
    {
        var module = new PipelineReturnModule();
        module.Order.Should().Be(int.MaxValue, "PipelineReturnModule must run last in the Cleanup stage");
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
            assemblyName: "PipelineReturnModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
