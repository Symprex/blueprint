using Blueprint.SourceGenerator.Planning;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Planning;

public class CompilationOperationLookupTests
{
    [Fact]
    public void When_operation_symbol_used_then_returns_operation_model()
    {
        var compilation = CreateCompilation();
        var lookup = new CompilationOperationLookup(compilation);
        var symbol = compilation.GetTypeByMetadataName("TestNamespace.SampleOperation");

        var result = lookup.Find(symbol!);

        result.Should().NotBeNull();
        result!.FullTypeName.Should().Be("TestNamespace.SampleOperation");
    }

    [Fact]
    public void When_operation_found_by_metadata_name_then_returns_operation_model()
    {
        var compilation = CreateCompilation();
        var lookup = new CompilationOperationLookup(compilation);

        var result = lookup.FindByMetadataName("TestNamespace.SampleOperation");

        result.Should().NotBeNull();
        result!.TypeName.Should().Be("SampleOperation");
        result.Namespace.Should().Be("TestNamespace");
    }

    [Fact]
    public void When_non_operation_symbol_used_then_returns_null()
    {
        var compilation = CreateCompilation();
        var lookup = new CompilationOperationLookup(compilation);
        var symbol = compilation.GetTypeByMetadataName("TestNamespace.NotAnOperation");

        var result = lookup.Find(symbol!);

        result.Should().BeNull();
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              using Blueprint.Model;

                              namespace TestNamespace;

                              public partial class SampleOperation : IOperation
                              {
                              }

                              public class NotAnOperation
                              {
                              }
                              """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "CompilationOperationLookupTests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
