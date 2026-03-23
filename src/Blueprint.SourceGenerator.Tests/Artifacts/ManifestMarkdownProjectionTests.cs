using Blueprint.SourceGenerator.Artifacts;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class ManifestMarkdownProjectionTests
{
    private const string SnapshotsFolder = "ManifestSnapshots/";

    [Fact]
    public async Task When_operations_manifest_projected_then_markdown_summary_is_stable()
    {
        var manifest = CreateOperationsManifest();

        await Verify(manifest.ToMarkdown())
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public void When_markdown_projected_then_header_and_section_metadata_are_included()
    {
        var manifest = CreateOperationsManifest();

        var markdown = manifest.ToMarkdown();

        markdown.Should().Contain("# operations");
        markdown.Should().Contain("Blueprint.SourceGenerator v1.2.3");
        markdown.Should().Contain("TestNamespace.TestBlueprintApp");
        markdown.Should().Contain("operations-discovery");
        markdown.Should().Contain("OperationDiscoveryManifestModule");
    }

    [Fact]
    public void When_markdown_projected_then_projection_uses_only_envelope_data()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "EnvelopeOnly.BlueprintApp",
            "Envelope.Assembly",
            "9.9.9");
        manifest.AddOperation(
            "EnvelopeOnly.CustomOperation",
            new JObject
            {
                ["kind"] = "envelope-owned-kind",
                ["namespace"] = "EnvelopeOnly.Namespace",
                ["typeName"] = "EnvelopeOnlyType",
                ["reason"] = "from-envelope-only",
            });

        var markdown = manifest.ToMarkdown();

        markdown.Should().Contain("envelope-owned-kind");
        markdown.Should().Contain("EnvelopeOnly.Namespace");
        markdown.Should().Contain("from-envelope-only");
    }

    [Fact]
    public void When_markdown_projected_then_nested_payloads_are_rendered_deterministically()
    {
        var firstManifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");
        firstManifest.AddOperation(
            "TestNamespace.NestedOperation",
            new JObject
            {
                ["kind"] = "query",
                ["details"] = new JObject
                {
                    ["zeta"] = "last",
                    ["alpha"] = "first",
                },
            });

        var secondManifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");
        secondManifest.AddOperation(
            "TestNamespace.NestedOperation",
            new JObject
            {
                ["kind"] = "query",
                ["details"] = new JObject
                {
                    ["alpha"] = "first",
                    ["zeta"] = "last",
                },
            });

        firstManifest.ToMarkdown().Should().Be(secondManifest.ToMarkdown());
    }

    private static GeneratedManifestEnvelope CreateOperationsManifest()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");
        manifest.AddSection(
            "operations-discovery",
            "OperationDiscoveryManifestModule",
            new JObject
            {
                ["operationCount"] = 2,
            });
        manifest.AddOperation(
            "TestNamespace.CreateUserCommand",
            new JObject
            {
                ["kind"] = "command",
                ["namespace"] = "TestNamespace",
                ["typeName"] = "CreateUserCommand",
            });
        manifest.AddOperation(
            "TestNamespace.ListUsersQuery",
            new JObject
            {
                ["kind"] = "query",
                ["namespace"] = "TestNamespace",
                ["typeName"] = "ListUsersQuery",
            });

        return manifest;
    }
}
