using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.HandlerExecution;

/// <summary>
/// Tests that <see cref="HandlerExecutionModule"/> correctly contributes handler invocation
/// to the Execution pipeline stage and registers the handler-result variable.
/// </summary>
public class HandlerExecutionModuleTests
{
    [Fact]
    public void When_operation_has_static_handler_then_pipeline_has_execution_contribution()
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
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)));
    }

    [Fact]
    public void When_operation_has_static_handler_then_handler_result_variable_is_registered()
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
        result.Value!.VariableRegistry.Registrations.Should().Contain(
            static v => v.Key.Purpose == HandlerExecutionModule.HandlerResultPurpose);
    }

    [Fact]
    public void When_command_operation_then_pipeline_has_execution_contribution()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using System.Threading.Tasks;

            namespace TestNamespace;

            public partial class CreateOrderCommand : ICommand<string>
            {
                public static Task<string> HandleAsync(CreateOrderCommand operation)
                {
                    return Task.FromResult("created");
                }
            }
            """);

        var config = new BlueprintSourceGenerationConfig(
        [
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);
        var result = config.BuildPipelinePlan("TestNamespace.CreateOrderCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Contributions.Should().Contain(
            new PipelineContribution(PipelineStage.Execution, nameof(HandlerExecutionModule)));
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
            assemblyName: "HandlerExecutionModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
