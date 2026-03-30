using Blueprint.SourceGenerator.Generators.Descriptors;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Generators.OperationDescriptor;

public class OperationDescriptorSourceGeneratorTestsContractOnlyCompilationTests
{
    [Fact]
    public void When_contract_only_compilation_lacks_operation_result_then_descriptor_generator_still_succeeds_for_plain_contracts()
    {
        var compilation = CreateContractOnlyCompilation("""
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class IncludedOperation : IQuery<string> {}
            """);
        var generator = new OperationDescriptorSourceGenerator();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var descriptorSource = outputCompilation.SyntaxTrees
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static source => source.Contains("IncludedOperationDescriptor"));

        descriptorSource.Should().NotBeNull();
        descriptorSource!.Should().Contain("OperationDiscoveryFeatureData");
        descriptorSource.Should().Contain("OperationFailureOutcomesFeatureData");
        descriptorSource.Should().NotContain("OperationResultOutcomesFeatureData");
    }

    private static CSharpCompilation CreateContractOnlyCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var runtimeAssemblyPath = typeof(Blueprint.OperationResult).Assembly.Location;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Where(assembly => !string.Equals(assembly.Location, runtimeAssemblyPath, StringComparison.OrdinalIgnoreCase))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "OperationDescriptorSourceGeneratorContractOnlyTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
