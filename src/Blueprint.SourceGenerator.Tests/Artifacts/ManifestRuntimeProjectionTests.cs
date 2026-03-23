using System.Reflection;
using Blueprint.Runtime.Artifacts;
using Blueprint.SourceGenerator.Artifacts;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class ManifestRuntimeProjectionTests
{
    private const string ResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.operations.manifest.json";
    private const string SnapshotsFolder = "ManifestSnapshots/";

    [Fact]
    public async Task When_manifest_loaded_from_embedded_resource_then_operations_project_correctly()
    {
        var json = ManifestResourceLoader.LoadRequiredString(typeof(ManifestRuntimeProjectionTests).Assembly, ResourceName);
        var manifest = GeneratedManifestEnvelope.FromJson(json);
        var projection = $$"""
                           Resource: {{ResourceName}}
                           Operation count: {{manifest.Operations.Count}}

                           {{manifest.ToMarkdown()}}
                           """;

        await Verify(projection)
            .UseDirectory(SnapshotsFolder);
    }

    [Fact]
    public void When_manifest_roundtripped_through_json_then_output_stays_stable()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "Roundtrip.BlueprintApp",
            "Roundtrip.Assembly",
            "9.9.9");
        manifest.AddSection(
            "operations-discovery",
            "OperationDiscoveryManifestModule",
            new JObject
            {
                ["metadata"] = new JObject
                {
                    ["zeta"] = "last",
                    ["alpha"] = "first",
                },
            });
        manifest.AddOperation(
            "Roundtrip.NestedOperation",
            new JObject
            {
                ["kind"] = "query",
                ["details"] = new JObject
                {
                    ["zeta"] = "last",
                    ["alpha"] = "first",
                },
            });

        var roundtripped = GeneratedManifestEnvelope.FromJson(manifest.ToJson());

        roundtripped.ToJson().Should().Be(manifest.ToJson());
    }

    [Fact]
    public void When_manifest_loaded_from_embedded_resource_then_runtime_reads_embedded_values_without_recomputing_discovery()
    {
        var json = ManifestResourceLoader.LoadRequiredString(Assembly.GetExecutingAssembly(), ResourceName);
        var manifest = GeneratedManifestEnvelope.FromJson(json);

        manifest.ArtifactName.Should().Be("operations");
        manifest.Sections["operations-discovery"].Payload["operationCount"]!.Value<int>().Should().Be(2);
        manifest.Operations["TestNamespace.CreateUserCommand"]["kind"]!.Value<string>().Should().Be("command");
        manifest.Operations["TestNamespace.ListUsersQuery"]["typeName"]!.Value<string>().Should().Be("ListUsersQuery");
    }
}
