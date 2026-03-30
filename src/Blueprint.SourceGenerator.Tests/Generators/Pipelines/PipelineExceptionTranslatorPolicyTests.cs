using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.Pipelines;

public class PipelineExceptionTranslatorPolicyTests
{
    [Fact]
    public void When_specific_and_fallback_translators_used_then_policy_orders_specific_translators_before_fallback()
    {
        var result = PipelineExceptionTranslatorPolicy.Create(
        [
            new TestExceptionTranslator("fallback", "System.Exception", 0, "System.Exception", isFallback: true),
            new TestExceptionTranslator("invalid-operation", "System.InvalidOperationException", 20, "System.InvalidOperationException"),
            new TestExceptionTranslator("argument", "System.ArgumentException", 10, "System.ArgumentException"),
        ]);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Translators
            .Select(static translator => translator.TranslatorId)
            .Should()
            .Equal("argument", "invalid-operation", "fallback");
    }

    [Fact]
    public void When_translators_overlap_on_same_exception_type_then_reports_conflict_diagnostic()
    {
        var result = PipelineExceptionTranslatorPolicy.Create(
        [
            new TestExceptionTranslator("first", "System.InvalidOperationException", 0, "System.InvalidOperationException"),
            new TestExceptionTranslator("second", "System.InvalidOperationException", 10, "System.InvalidOperationException"),
        ]);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BP113");
    }

    [Fact]
    public void When_translators_overlap_on_base_and_derived_exception_types_then_reports_conflict_diagnostic()
    {
        var result = PipelineExceptionTranslatorPolicy.Create(
        [
            new TestExceptionTranslator("base", "System.Exception", 0, "System.Exception"),
            new TestExceptionTranslator(
                "derived",
                "System.InvalidOperationException",
                10,
                "System.InvalidOperationException",
                ownedExceptionTypeNames: ["System.InvalidOperationException", "System.Exception"]),
        ]);

        result.Value.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].ToDiagnostic().Id.Should().Be("BP113");
    }

    [Fact]
    public void Pipeline_code_generator_orders_exception_catches_using_policy_order()
    {
        const string source = """
                               using Blueprint.Model;
                               using System.Threading.Tasks;

                               namespace TestNamespace;

                               public partial class TestQuery : IQuery<string>
                               {
                                   public static Task<string> InvokeAsync()
                                   {
                                       return Task.FromResult("Hello World");
                                   }
                               }
                               """;

        var (compilation, operationType) = CompileAndGetOperationType(source, "TestQuery");
        var handlerResult = HandlerDiscovery.FindHandler(operationType, compilation);
        var operation = OperationModel.Create(operationType);
        var plan = new PipelinePlan(operation, new VariableRegistry());
        var module = new HandlerExecutionModule();

        handlerResult.Diagnostics.Should().BeEmpty();
        handlerResult.Value.Should().NotBeNull();

        module.ContributeToPipeline(new PipelineContributorContext(module.ContributorId, operation, plan, new CompilationOperationLookup(compilation), handlerResult.Value!));

        var policyResult = PipelineExceptionTranslatorPolicy.Create(
        [
            new TestExceptionTranslator("fallback", "System.Exception", 0, "System.Exception", isFallback: true),
            new TestExceptionTranslator("invalid-operation", "System.InvalidOperationException", 20, "System.InvalidOperationException"),
            new TestExceptionTranslator("argument", "System.ArgumentException", 10, "System.ArgumentException"),
        ]);

        policyResult.Diagnostics.Should().BeEmpty();
        policyResult.Value.Should().NotBeNull();

        var pipelineSource = PipelineCodeGenerator.GeneratePipeline(handlerResult.Value!, plan, policyResult.Value!);
        var argumentCatch = pipelineSource.IndexOf("catch (System.ArgumentException e)", StringComparison.Ordinal);
        var invalidOperationCatch = pipelineSource.IndexOf("catch (System.InvalidOperationException e)", StringComparison.Ordinal);
        var fallbackCatch = pipelineSource.IndexOf("catch (System.Exception e)", StringComparison.Ordinal);

        argumentCatch.Should().BeGreaterThan(-1);
        invalidOperationCatch.Should().BeGreaterThan(argumentCatch);
        fallbackCatch.Should().BeGreaterThan(invalidOperationCatch);
    }

    private static (Compilation Compilation, INamedTypeSymbol OperationType) CompileAndGetOperationType(
        string source,
        string typeName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.ICommand).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "PipelineExceptionTranslatorPolicyTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var operationType = compilation.GetTypeByMetadataName($"TestNamespace.{typeName}");

        operationType.Should().NotBeNull($"Type {typeName} should exist in compilation");

        return (compilation, operationType!);
    }

    private sealed class TestExceptionTranslator(
        string translatorId,
        string handledExceptionTypeName,
        int order,
        string catchDeclaration,
        bool isFallback = false,
        params string[] ownedExceptionTypeNames)
        : IPipelineExceptionTranslator
    {
        public string TranslatorId { get; } = translatorId;

        public string HandledExceptionTypeName { get; } = handledExceptionTypeName;

        public IReadOnlyList<string> OwnedExceptionTypeNames { get; } =
            ownedExceptionTypeNames.Length == 0 ? [handledExceptionTypeName] : ownedExceptionTypeNames;

        public int Order { get; } = order;

        public string ResultTypeName => "Blueprint.Runtime.Exec.UnhandledExceptionResult";

        public bool IsFallback { get; } = isFallback;

        public void WriteCatchBlock(ICodeWriter writer, bool isHandlerAsync)
        {
            writer.Catch(
                $"{catchDeclaration} e",
                catchWriter =>
                {
                    catchWriter.Return(
                        isHandlerAsync
                            ? "new Blueprint.Runtime.Exec.UnhandledExceptionResult(e)"
                            : "System.Threading.Tasks.Task.FromResult<Blueprint.OperationResult>(new Blueprint.Runtime.Exec.UnhandledExceptionResult(e))");
                });
        }
    }
}
