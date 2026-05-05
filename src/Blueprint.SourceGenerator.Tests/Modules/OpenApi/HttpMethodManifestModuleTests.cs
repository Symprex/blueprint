using Blueprint.SourceGenerator.Modules.OpenApi;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.OpenApi;

/// <summary>
/// Tests that <see cref="HttpMethodManifestModule"/> contributes per-operation HTTP method metadata
/// (<c>httpRouting.methods</c>, <c>httpRouting.explicit</c>) to the shared debug manifest.
/// </summary>
public class HttpMethodManifestModuleTests
{
    [Fact]
    public void When_query_has_no_explicit_method_then_manifest_shows_conventional_GET()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class GetItemsQuery : IQuery<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new HttpMethodManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var methods = result.Value!.Entries["httpRouting.methods"] as JArray;
        methods.Should().NotBeNull();
        methods!.Count.Should().Be(1);
        methods[0]!.ToString().Should().Be("GET");

        result.Value.Entries["httpRouting.explicit"].ToString().Should().Be("False");
    }

    [Fact]
    public void When_command_has_no_explicit_method_then_manifest_shows_conventional_POST()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class CreateItemCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new HttpMethodManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.CreateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var methods = result.Value!.Entries["httpRouting.methods"] as JArray;
        methods.Should().NotBeNull();
        methods!.Count.Should().Be(1);
        methods[0]!.ToString().Should().Be("POST");

        result.Value.Entries["httpRouting.explicit"].ToString().Should().Be("False");
    }

    [Fact]
    public void When_operation_has_explicit_HttpDelete_then_manifest_shows_DELETE()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [HttpDelete]
            public partial class DeleteItemCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new HttpMethodManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.DeleteItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var methods = result.Value!.Entries["httpRouting.methods"] as JArray;
        methods.Should().NotBeNull();
        methods!.Count.Should().Be(1);
        methods[0]!.ToString().Should().Be("DELETE");

        result.Value.Entries["httpRouting.explicit"].ToString().Should().Be("True");
    }

    [Fact]
    public void When_operation_has_multiple_explicit_methods_then_manifest_lists_all()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [HttpPut]
            [HttpPatch]
            public partial class UpdateItemCommand : ICommand<string> { }
            """);

        var config = new BlueprintSourceGenerationConfig([new HttpMethodManifestModule()]);
        var result = config.BuildDebugManifest("TestNamespace.UpdateItemCommand", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();

        var methods = result.Value!.Entries["httpRouting.methods"] as JArray;
        methods.Should().NotBeNull();
        methods!.Count.Should().Be(2);

        var methodStrings = methods.Select(static m => m.ToString()).ToList();
        methodStrings.Should().Contain("PATCH");
        methodStrings.Should().Contain("PUT");

        result.Value.Entries["httpRouting.explicit"].ToString().Should().Be("True");
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
