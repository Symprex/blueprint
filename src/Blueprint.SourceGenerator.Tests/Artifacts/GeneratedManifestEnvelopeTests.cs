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

    [Fact]
    public void FromJson_Should_Reject_Sections_Property_That_Is_Not_An_Object()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["sections"] = "invalid";

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sections*object*");
    }

    [Fact]
    public void FromJson_Should_Reject_Operations_Property_That_Is_Not_An_Object()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["operations"] = new JArray("invalid");

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*operations*object*");
    }

    [Fact]
    public void FromJson_Should_Reject_SchemaVersion_That_Is_Not_An_Integer_Token()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["schemaVersion"] = "1";

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*schemaVersion*integer*");
    }

    [Fact]
    public void FromJson_Should_Reject_SchemaVersion_That_Exceeds_Int32_Range()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["schemaVersion"] = 2147483648L;

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*schemaVersion*integer*");
    }

    [Fact]
    public void FromJson_Should_Reject_ArtifactName_That_Is_Not_A_String_Token()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["artifactName"] = 123;

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*artifactName*string*");
    }

    [Theory]
    [InlineData("typeName")]
    [InlineData("assemblyName")]
    public void FromJson_Should_Report_App_Property_Path_When_Nested_String_Field_Has_Invalid_Token(string propertyName)
    {
        var manifestJson = CreateValidManifestJson();
        ((JObject)manifestJson["app"]!)[propertyName] = 123;

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*app.{propertyName}*string*");
    }

    [Theory]
    [InlineData("productName")]
    [InlineData("version")]
    public void FromJson_Should_Report_Generator_Property_Path_When_Nested_String_Field_Has_Invalid_Token(string propertyName)
    {
        var manifestJson = CreateValidManifestJson();
        ((JObject)manifestJson["generator"]!)[propertyName] = 123;

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*generator.{propertyName}*string*");
    }

    [Fact]
    public void FromJson_Should_Report_Section_Contributor_Path_When_Nested_String_Field_Has_Invalid_Token()
    {
        var manifestJson = CreateValidManifestJson();
        manifestJson["sections"] = new JObject
        {
            ["operations-discovery"] = new JObject
            {
                ["contributorId"] = 123,
                ["payload"] = new JObject(),
            },
        };

        var act = () => GeneratedManifestEnvelope.FromJson(manifestJson.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sections.operations-discovery.contributorId*string*");
    }

    private static JObject CreateValidManifestJson()
    {
        return new JObject
        {
            ["schemaVersion"] = 1,
            ["artifactName"] = "operations",
            ["artifactFamily"] = "build-time-manifest",
            ["relativePath"] = ".blueprint/manifests/operations.manifest.json",
            ["app"] = new JObject
            {
                ["typeName"] = "TestNamespace.TestBlueprintApp",
                ["assemblyName"] = "Test.Assembly",
            },
            ["generator"] = new JObject
            {
                ["productName"] = "Blueprint.SourceGenerator",
                ["version"] = "1.2.3",
            },
            ["sections"] = new JObject(),
            ["operations"] = new JObject(),
        };
    }
}
