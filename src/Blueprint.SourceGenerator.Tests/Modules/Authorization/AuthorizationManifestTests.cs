using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.Authorization;

/// <summary>
/// Tests that <see cref="AuthorizationManifestModule"/> contributes per-operation authorization
/// metadata (<c>auth.accessMode</c>, <c>auth.scopePairs</c>, <c>auth.source</c>) to the
/// shared debug manifest.
/// </summary>
public class AuthorizationManifestTests
{
    [Fact]
    public void When_operation_has_Resource_then_manifest_shows_permissioned_access_mode()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.accessMode");
        result.Value.Entries["auth.accessMode"].ToString().Should().Be("permissioned");
    }

    [Fact]
    public void When_operation_has_Resource_then_manifest_has_scope_pairs()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            [Resource("tenant", "manage")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.scopePairs");

        var scopePairs = result.Value.Entries["auth.scopePairs"] as JArray;
        scopePairs.Should().NotBeNull();
        scopePairs.Should().HaveCount(2);
        scopePairs![0]["noun"]!.ToString().Should().Be("item");
        scopePairs[0]["verb"]!.ToString().Should().Be("read");
        scopePairs[1]["noun"]!.ToString().Should().Be("tenant");
        scopePairs[1]["verb"]!.ToString().Should().Be("manage");
    }

    [Fact]
    public void When_operation_has_Resource_then_manifest_source_is_direct()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.source");
        result.Value.Entries["auth.source"].ToString().Should().Be("direct");
    }

    [Fact]
    public void When_operation_has_FromResource_then_manifest_source_is_inherited()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/items/{Id}")]
            [Resource("item", "read")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }

            [RootLink("/items/{Id}/details")]
            [FromResource(typeof(GetItemQuery))]
            public partial class GetItemDetailsQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemDetailsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.accessMode");
        result.Value.Entries["auth.accessMode"].ToString().Should().Be("permissioned");
        result.Value.Entries.Should().ContainKey("auth.source");
        result.Value.Entries["auth.source"].ToString().Should().Contain("inherited:");
        result.Value.Entries["auth.source"].ToString().Should().Contain("GetItemQuery");
    }

    [Fact]
    public void When_operation_has_AllowAnonymous_then_manifest_shows_anonymous_access_mode()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/public")]
            [AllowAnonymous]
            public partial class GetPublicQuery : IQuery<string> { }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetPublicQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.accessMode");
        result.Value.Entries["auth.accessMode"].ToString().Should().Be("anonymous");
    }

    [Fact]
    public void When_Resource_then_manifest_shows_permissioned_even_when_auth_redundant()
    {
        // [Resource] alone is sufficient for "permissioned" — AuthorizationModule projects its
        // metadata and the manifest shows the richer classification.
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [Resource("item", "read")]
            public partial class GetProtectedItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetProtectedItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.accessMode");
        result.Value.Entries["auth.accessMode"].ToString().Should().Be("permissioned");
    }

    [Fact]
    public void When_operation_has_no_auth_attributes_then_manifest_shows_default_access_mode()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;

            namespace TestNamespace;

            public partial class PlainQuery : IQuery<string> { }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.PlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("auth.accessMode");
        result.Value.Entries["auth.accessMode"].ToString().Should().Be("default");
    }

    [Fact]
    public void When_anonymous_then_no_scope_pairs_or_source_in_manifest()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [AllowAnonymous]
            public partial class PublicQuery : IQuery<string> { }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.PublicQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().NotContainKey("auth.scopePairs");
        result.Value.Entries.Should().NotContainKey("auth.source");
    }

    private static BlueprintSourceGenerationConfig CreateConfig()
    {
        // Note: LinkGenerationModule is omitted because BuildDebugManifest does not run
        // PostInitialization which generates the [RootLink] marker attribute source.
        // The auth manifest module reads descriptor plan metadata rather than attributes directly.
        return new BlueprintSourceGenerationConfig(
            [new AuthenticationModule(), new AuthorizationModule(), new AuthorizationManifestModule()]);
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
