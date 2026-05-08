using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.ErrorModel;

/// <summary>
/// Tests that <see cref="FailureOutcomesModule"/> correctly projects exception-translator policy
/// into descriptor feature data, including the well-known default set optimization.
/// </summary>
public class FailureOutcomesModuleTests
{
    [Fact]
    public void When_default_exception_translators_then_descriptor_plan_includes_failure_outcomes()
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
            new FailureOutcomesModule(),
            new DefaultExceptionTranslationModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        var policyResult = DescriptorPlanBuilder.CreateExceptionTranslatorPolicy(executionPlan);
        var descriptorResult = DescriptorPlanBuilder.Build("TestNamespace.GetOrderQuery", compilation, executionPlan, policyResult);

        descriptorResult.Value.Should().NotBeNull();
        descriptorResult.Value!.Metadata.Should().Contain(
            static m => m is FailureOutcomeRegistration);
    }

    [Fact]
    public void When_default_translators_then_metadata_includes_three_default_outcomes()
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
            new FailureOutcomesModule(),
            new DefaultExceptionTranslationModule(),
            new HandlerExecutionModule(),
            new PipelineReturnModule(),
        ]);

        var executionPlan = config.BuildExecutionPlan();
        var policyResult = DescriptorPlanBuilder.CreateExceptionTranslatorPolicy(executionPlan);
        var descriptorResult = DescriptorPlanBuilder.Build("TestNamespace.GetOrderQuery", compilation, executionPlan, policyResult);

        descriptorResult.Value.Should().NotBeNull();

        var outcomes = descriptorResult.Value!.GetMetadata<FailureOutcomeRegistration>().ToArray();
        outcomes.Should().HaveCount(3);
        outcomes.Should().Contain(static o => o.TranslatorId == "legacy-validation-exception");
        outcomes.Should().Contain(static o => o.TranslatorId == "data-annotations-validation-exception");
        outcomes.Should().Contain(static o => o.TranslatorId == "default-unhandled-exception");
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
            assemblyName: "FailureOutcomesModuleTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
