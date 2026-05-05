using Blueprint.SourceGenerator.Modules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.ResultModel;

/// <summary>
/// Tests that <see cref="ResultContractManifestModule"/> contributes per-operation result contract metadata
/// (<c>resultContract.type</c>, <c>resultContract.isUnion</c>, <c>resultContract.members</c>) to the
/// shared debug manifest.
/// </summary>
public class ResultContractManifestModuleTests
{
    [Fact]
    public void When_operation_has_simple_result_type_then_manifest_shows_type()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public class ItemDto { public string Id { get; set; } }

            public partial class GetItemQuery : IQuery<ItemDto> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResultContractManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resultContract.type"].ToString().Should().Contain("ItemDto");
        result.Value.Entries["resultContract.isUnion"].ToString().Should().Be("False");
        result.Value.Entries.Should().NotContainKey("resultContract.members");
    }

    [Fact]
    public void When_operation_has_Results_union_then_manifest_shows_members()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Runtime.Exec;

            namespace TestNamespace;

            public class ItemDto { public string Id { get; set; } }

            public partial class UpsertCommand : ICommand<Results<OkResult<ItemDto>, NotFoundResult>> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResultContractManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.UpsertCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resultContract.isUnion"].ToString().Should().Be("True");

        var members = result.Value.Entries["resultContract.members"] as JArray;
        members.Should().NotBeNull();
        members!.Count.Should().Be(2);

        var memberStrings = members.Select(static m => m.ToString()).ToList();
        memberStrings.Should().Contain(s => s.Contains("OkResult"));
        memberStrings.Should().Contain(s => s.Contains("NotFoundResult"));
    }

    [Fact]
    public void When_operation_has_no_generic_interface_then_manifest_shows_none()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class FireAndForgetCommand : ICommand { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResultContractManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.FireAndForgetCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resultContract.type"].ToString().Should().Be("none");
    }

    [Fact]
    public void When_operation_returns_string_then_manifest_shows_primitive()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class GetNameQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new ResultContractManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.GetNameQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries["resultContract.type"].ToString().Should().Be("string");
        result.Value.Entries["resultContract.isUnion"].ToString().Should().Be("False");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Runtime.Exec.OkResult<>).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
