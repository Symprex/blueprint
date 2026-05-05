using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Modules.LinkGeneration;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Modules.LinkGeneration;

/// <summary>
/// Tests that <see cref="LinkManifestModule"/> contributes per-operation URL and self-link metadata
/// (<c>links.urls</c>, <c>links.selfLinks</c>) to the shared debug manifest.
/// </summary>
public class LinkManifestModuleTests
{
    [Fact]
    public void When_operation_has_RootLink_then_manifest_has_links_urls()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            [RootLink("/items")]
            public partial class GetItemsQuery : IQuery<string> { }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemsQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("links.urls");

        var urls = result.Value.Entries["links.urls"] as JArray;
        urls.Should().NotBeNull();
        urls!.Count.Should().Be(1);
        urls[0]!.ToString().Should().Be("/items");
    }

    [Fact]
    public void When_operation_has_SelfLink_then_manifest_has_links_selfLinks()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class ItemResource : ILinkableResource
            {
                public string Id { get; set; }
                public void AddLink(string rel, Link link) { }
            }

            [SelfLink(typeof(ItemResource), "/items/{Id}")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("links.selfLinks");

        var selfLinks = result.Value.Entries["links.selfLinks"] as JArray;
        selfLinks.Should().NotBeNull();
        selfLinks!.Count.Should().Be(1);

        var entry = selfLinks[0] as JObject;
        entry.Should().NotBeNull();
        entry!["url"]!.ToString().Should().Be("/items/{Id}");
        entry["resourceType"]!.ToString().Should().Be("ItemResource");
    }

    [Fact]
    public void When_operation_has_both_RootLink_and_SelfLink_then_manifest_has_both()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class ItemResource : ILinkableResource
            {
                public string Id { get; set; }
                public void AddLink(string rel, Link link) { }
            }

            [RootLink("/items/{Id}")]
            [SelfLink(typeof(ItemResource), "/items/{Id}")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("links.urls");
        result.Value!.Entries.Should().ContainKey("links.selfLinks");
    }

    [Fact]
    public void When_operation_has_no_link_attributes_then_manifest_has_no_link_entries()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public partial class PlainQuery : IQuery<string> { }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.PlainQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().NotContainKey("links.urls");
        result.Value!.Entries.Should().NotContainKey("links.selfLinks");
    }

    [Fact]
    public void When_operation_has_multiple_SelfLinks_then_manifest_lists_all()
    {
        var compilation = CreateCompilation(
            """
            using Blueprint.Model;
            using Blueprint.Model.Attributes;

            namespace TestNamespace;

            public class ItemResource : ILinkableResource
            {
                public string Id { get; set; }
                public string Slug { get; set; }
                public void AddLink(string rel, Link link) { }
            }

            [SelfLink(typeof(ItemResource), "/items/{Id}")]
            [SelfLink(typeof(ItemResource), "/items/by-slug/{Slug}")]
            public partial class GetItemQuery : IQuery<string>
            {
                public string Id { get; set; }
                public string Slug { get; set; }
            }
            """);

        var config = CreateConfig();
        var result = config.BuildDebugManifest("TestNamespace.GetItemQuery", compilation);

        result.Diagnostics.Should().BeEmpty();
        result.Value.Should().NotBeNull();
        result.Value!.Entries.Should().ContainKey("links.selfLinks");

        var selfLinks = result.Value.Entries["links.selfLinks"] as JArray;
        selfLinks.Should().NotBeNull();
        selfLinks!.Count.Should().Be(2);
    }

    private static BlueprintSourceGenerationConfig CreateConfig()
    {
        // LinkGenerationModule must be included because it adds UrlRegistration/LinkRegistration
        // metadata that LinkManifestModule reads from the descriptor plan.
        // Note: BuildDebugManifest does not run PostInitialization, but the test compilations
        // include the marker attribute source directly to simulate the same environment.
        return new BlueprintSourceGenerationConfig(
            [new LinkGenerationModule(), new LinkManifestModule()]);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        // Include the marker attribute source to simulate PostInitialization output
        var markerSource = GetMarkerAttributeSource();
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)),
            CSharpSyntaxTree.ParseText(markerSource, new CSharpParseOptions(LanguageVersion.Preview)),
        };

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(Blueprint.Model.IOperation).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string GetMarkerAttributeSource()
    {
        return """
            namespace Blueprint.Model.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
                internal sealed class RootLinkAttribute : System.Attribute
                {
                    public RootLinkAttribute(string url) { Url = url; }
                    public string Url { get; }
                }

                [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                internal sealed class SelfLinkAttribute : System.Attribute
                {
                    public SelfLinkAttribute(System.Type resourceType, string url) { ResourceType = resourceType; Url = url; }
                    public System.Type ResourceType { get; }
                    public string Url { get; }
                }
            }
            """;
    }
}
