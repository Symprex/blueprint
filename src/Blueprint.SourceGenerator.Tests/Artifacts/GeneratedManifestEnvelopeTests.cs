using System.Text.RegularExpressions;
using Blueprint.Runtime.Artifacts;
using Blueprint.SourceGenerator.Artifacts;
using FluentAssertions;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Artifacts;

public class GeneratedManifestEnvelopeTests
{
    private const string ManifestResourceName = "Blueprint.SourceGenerator.Tests.Artifacts.ManifestResources.operations.manifest.json";

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

    [Fact]
    public void ManifestSchema_Should_Validate_Embedded_Operations_Manifest()
    {
        var schema = LoadManifestSchema();
        var manifest = JObject.Parse(ManifestResourceLoader.LoadRequiredString(typeof(GeneratedManifestEnvelopeTests).Assembly, ManifestResourceName));

        var errors = ValidateAgainstSchema(schema, manifest);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ManifestSchema_Should_Reject_Invalid_App_TypeName_Token()
    {
        var schema = LoadManifestSchema();
        var manifest = JObject.Parse(ManifestResourceLoader.LoadRequiredString(typeof(GeneratedManifestEnvelopeTests).Assembly, ManifestResourceName));
        ((JObject)manifest["app"]!)["typeName"] = 123;

        var errors = ValidateAgainstSchema(schema, manifest);

        errors.Should().ContainSingle()
            .Which.Should().Contain("$.app.typeName");
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

    private static JObject LoadManifestSchema()
    {
        var schemaPath = FindManifestSchemaPath();

        return JObject.Parse(File.ReadAllText(schemaPath));
    }

    private static string FindManifestSchemaPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Blueprint.SourceGenerator",
                "Artifacts",
                "GeneratedManifestEnvelope.schema.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The checked-in manifest schema artifact could not be located from the current test output path.");
    }

    private static IReadOnlyList<string> ValidateAgainstSchema(JObject schema, JToken instance)
    {
        var errors = new List<string>();
        ValidateAgainstSchemaCore(instance, schema, schema, "$", errors);
        return errors;
    }

    private static void ValidateAgainstSchemaCore(
        JToken instance,
        JToken schemaToken,
        JObject rootSchema,
        string path,
        List<string> errors)
    {
        if (schemaToken is not JObject schema)
        {
            return;
        }

        if (schema["$ref"] is JValue { Type: JTokenType.String } refValue &&
            refValue.Value<string>() is { } referencePath)
        {
            ValidateAgainstSchemaCore(instance, ResolveSchemaReference(rootSchema, referencePath), rootSchema, path, errors);
            return;
        }

        if (schema["type"] is JValue { Type: JTokenType.String } typeValue &&
            typeValue.Value<string>() is { } expectedType &&
            !MatchesType(instance, expectedType))
        {
            errors.Add($"{path} must be a JSON {expectedType}.");
            return;
        }

        if (schema["const"] is { } constantToken && !JToken.DeepEquals(instance, constantToken))
        {
            errors.Add($"{path} must equal {constantToken.ToString(Newtonsoft.Json.Formatting.None)}.");
        }

        if (schema["minLength"] is JValue { Type: JTokenType.Integer } minLengthValue &&
            instance is JValue { Type: JTokenType.String } stringValue &&
            stringValue.Value<string>() is { } stringContent &&
            stringContent.Length < minLengthValue.Value<int>())
        {
            errors.Add($"{path} must be at least {minLengthValue.Value<int>()} characters long.");
        }

        if (schema["pattern"] is JValue { Type: JTokenType.String } patternValue &&
            instance is JValue { Type: JTokenType.String } patternStringValue &&
            patternStringValue.Value<string>() is { } patternString &&
            !Regex.IsMatch(patternString, patternValue.Value<string>()!, RegexOptions.CultureInvariant))
        {
            errors.Add($"{path} must match regex '{patternValue.Value<string>()}'.");
        }

        if (instance is not JObject instanceObject)
        {
            return;
        }

        var properties = schema["properties"] as JObject;
        var additionalProperties = schema["additionalProperties"];

        if (schema["required"] is JArray requiredProperties)
        {
            foreach (var requiredProperty in requiredProperties.Values<string>())
            {
                if (requiredProperty is null)
                {
                    continue;
                }

                if (!instanceObject.ContainsKey(requiredProperty))
                {
                    errors.Add($"{path}.{requiredProperty} is required.");
                }
            }
        }

        foreach (var property in instanceObject.Properties())
        {
            if (properties is not null && properties.TryGetValue(property.Name, out var propertySchema))
            {
                ValidateAgainstSchemaCore(property.Value, propertySchema, rootSchema, $"{path}.{property.Name}", errors);
                continue;
            }

            if (additionalProperties is JValue { Type: JTokenType.Boolean } allowAdditionalValue &&
                !allowAdditionalValue.Value<bool>())
            {
                errors.Add($"{path}.{property.Name} is not allowed.");
                continue;
            }

            if (additionalProperties is JObject additionalSchema)
            {
                ValidateAgainstSchemaCore(property.Value, additionalSchema, rootSchema, $"{path}.{property.Name}", errors);
            }
        }
    }

    private static JObject ResolveSchemaReference(JObject rootSchema, string referencePath)
    {
        if (!referencePath.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported schema reference '{referencePath}'.");
        }

        JToken current = rootSchema;
        foreach (var segment in referencePath[2..].Split('/'))
        {
            current = current[segment]
                ?? throw new InvalidOperationException($"Schema reference '{referencePath}' could not be resolved.");
        }

        return current as JObject
            ?? throw new InvalidOperationException($"Schema reference '{referencePath}' did not resolve to an object.");
    }

    private static bool MatchesType(JToken instance, string expectedType)
    {
        return expectedType switch
        {
            "object" => instance.Type == JTokenType.Object,
            "string" => instance.Type == JTokenType.String,
            "integer" => instance.Type == JTokenType.Integer,
            "number" => instance.Type is JTokenType.Integer or JTokenType.Float,
            "boolean" => instance.Type == JTokenType.Boolean,
            "array" => instance.Type == JTokenType.Array,
            "null" => instance.Type == JTokenType.Null,
            _ => throw new InvalidOperationException($"Unsupported schema type '{expectedType}'."),
        };
    }
}
