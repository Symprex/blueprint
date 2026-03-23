using Blueprint.SourceGenerator.Artifacts;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class GeneratedManifestEnvelopeTests
{
    [Fact]
    public async Task CreateBuildManifest_Should_Serialize_Stable_Envelope()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");

        manifest.AddSection(
            "links",
            "links-module",
            new JObject
            {
                ["resourceCount"] = 2,
            });
        manifest.AddOperation(
            "TestNamespace.GetUserQuery",
            new JObject
            {
                ["route"] = "/users/{id}",
            });

        await Verify(manifest.ToJson());
    }

    [Fact]
    public void AddSection_Should_Reject_Conflicting_Owner_For_Same_Section()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");

        manifest.AddSection("links", "links-module", new JObject());

        var act = () => manifest.AddSection("links", "resources-module", new JObject());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*links*links-module*resources-module*");
    }

    [Fact]
    public void AddOperation_Should_Reject_Duplicate_Operation_Key()
    {
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");

        manifest.AddOperation("TestNamespace.GetUserQuery", new JObject());

        var act = () => manifest.AddOperation("TestNamespace.GetUserQuery", new JObject());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TestNamespace.GetUserQuery*");
    }

    [Fact]
    public void ToJson_Should_Not_Depend_On_Contributor_Insertion_Order()
    {
        var firstManifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");
        firstManifest.AddSection("resources", "resources-module", new JObject());
        firstManifest.AddSection("links", "links-module", new JObject());
        firstManifest.AddOperation("TestNamespace.GetUserQuery", new JObject());
        firstManifest.AddOperation("TestNamespace.ListUsersQuery", new JObject());

        var secondManifest = GeneratedManifestEnvelope.CreateBuildManifest(
            "operations",
            "TestNamespace.TestBlueprintApp",
            "Test.Assembly",
            "1.2.3");
        secondManifest.AddSection("links", "links-module", new JObject());
        secondManifest.AddSection("resources", "resources-module", new JObject());
        secondManifest.AddOperation("TestNamespace.ListUsersQuery", new JObject());
        secondManifest.AddOperation("TestNamespace.GetUserQuery", new JObject());

        firstManifest.ToJson().Should().Be(secondManifest.ToJson());
    }
}
