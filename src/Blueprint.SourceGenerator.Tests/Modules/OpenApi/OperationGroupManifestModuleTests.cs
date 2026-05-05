using Blueprint.SourceGenerator.Modules.OpenApi;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Blueprint.SourceGenerator.Tests.Modules.OpenApi;

/// <summary>
/// Tests that <see cref="OperationGroupManifestModule"/> contributes per-operation group/tag metadata
/// (<c>openApi.group</c>, <c>openApi.groupSource</c>) to the shared debug manifest.
/// </summary>
public class OperationGroupManifestModuleTests
{
    [Fact]
    public void When_operation_has_explicit_ApiOperations_then_manifest_shows_explicit_group()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace.Items;

            [ApiOperations("Inventory")]
            public partial class GetItemsQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new OperationGroupManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.Items.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["openApi.group"].ToString().Should().Be("Inventory");
        result.Value.Entries["openApi.groupSource"].ToString().Should().Be("explicit");
    }

    [Fact]
    public void When_operation_has_no_ApiOperations_then_manifest_shows_namespace_derived_group()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace.Items;

            public partial class GetItemsQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new OperationGroupManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.Items.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["openApi.group"].ToString().Should().Be("Items");
        result.Value.Entries["openApi.groupSource"].ToString().Should().Be("namespace");
    }

    [Fact]
    public void When_operation_in_root_namespace_then_manifest_shows_namespace_as_group()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace Api;

            public partial class GetStatusQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new OperationGroupManifestModule()]);
        var result = config.BuildDebugManifest("Api.GetStatusQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["openApi.group"].ToString().Should().Be("Api");
        result.Value.Entries["openApi.groupSource"].ToString().Should().Be("namespace");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
