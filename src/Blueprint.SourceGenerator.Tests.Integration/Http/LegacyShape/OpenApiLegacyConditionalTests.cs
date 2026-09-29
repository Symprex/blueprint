using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming the conditional requirements of <c>[RequiredIf]</c> and <c>[RequiredItemsIf]</c> are
/// exported as legacy's <c>RequiredIfSchemaProcessor</c> exported them, while Default naming exports what the runtime
/// enforces.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.OpenApi/RequiredIfSchemaProcessor.cs</c>) walked the schema type's
/// public properties in declaration order and, for each, its <c>[RequiredIf]</c> attributes and then its
/// <c>[RequiredItemsIf]</c> ones. <c>[RequiredIfNot]</c> and <c>[RequiredItemsIfNot]</c> were not exported, and an attribute
/// whose dependent value is <c>null</c> was skipped. A string target had to be <c>{type: string, minLength: 1}</c>, any other
/// target <c>{not: {type: null}}</c> (whatever its type, a non-nullable value type too), and an items target a non-empty
/// array of <c>{type: string, minLength: 1, pattern: \S}</c> whatever its element type. <c>pattern</c> was only on the items.
/// </para>
/// <para>
/// Every expected value is what Signature365's 18 legacy documents carry: 18 conditionals over 9 components, 9 of them a
/// string target with no <c>pattern</c>, 5 <c>{not: {type: null}}</c> and 4 items targets, the only ones with a
/// <c>pattern</c>, such as <c>SurveyCommentsSettings</c> (<c>prompt</c> before <c>placeholder</c>, both plain strings) and
/// <c>ApplyToDto</c> (a <c>{not: {type: null}}</c> and an items target). The generated client validates <c>*Command</c>
/// schemas with them.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyConditionalTests
{
    private static readonly IReadOnlyDictionary<string, string> JsonNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(LegacyConditionalShape.Enabled)] = "enabled",
        [nameof(LegacyConditionalShape.Mode)] = "mode",
        [nameof(LegacyConditionalShape.Zed)] = "zed",
        [nameof(LegacyConditionalShape.Prompt)] = "prompt",
        [nameof(LegacyConditionalShape.Flag)] = "flag",
        [nameof(LegacyConditionalShape.Forced)] = "forced",
        [nameof(LegacyConditionalShape.Tags)] = "tags",
        [nameof(LegacyConditionalShape.Numbers)] = "numbers",
        [nameof(LegacyConditionalShape.Both)] = "both",
        [nameof(LegacyConditionalShape.Unless)] = "unless",
        [nameof(LegacyConditionalShape.UnlessItems)] = "unlessItems",
        [nameof(LegacyConditionalShape.WhenModeIsNull)] = "whenModeIsNull",
        [nameof(LegacyConditionalShape.Alpha)] = "alpha",
    };

    [Fact]
    public void Under_legacy_naming_conditionals_are_exported_in_declaration_order_without_the_not_variants_or_a_null_dependent()
    {
        var requirements = OpenApiSchemaProjectionContext.ExtractSchemaConditionals(typeof(LegacyConditionalShape), JsonNames, useLegacyRules: true);

        requirements.Select(static r => r.TargetPropertyName).Should().Equal("zed", "prompt", "flag", "forced", "tags", "numbers", "both", "both", "alpha");
    }

    [Fact]
    public void Under_default_naming_conditionals_are_ordered_by_target_and_include_what_the_runtime_enforces()
    {
        var requirements = OpenApiSchemaProjectionContext.ExtractSchemaConditionals(typeof(LegacyConditionalShape), JsonNames);

        requirements.Select(static r => r.TargetPropertyName).Should().Equal(
            "alpha", "both", "both", "flag", "numbers", "prompt", "tags", "unless", "unlessItems", "whenModeIsNull", "zed");
    }

    [Fact]
    public void Under_legacy_naming_a_propertys_required_if_is_exported_before_its_required_items_if()
    {
        var requirements = OpenApiSchemaProjectionContext.ExtractSchemaConditionals(typeof(LegacyConditionalShape), JsonNames, useLegacyRules: true);

        requirements.Where(static r => r.TargetPropertyName == "both").Select(static r => r.TargetKind).Should().Equal(
            OpenApiConditionalTargetKind.NonNull,
            OpenApiConditionalTargetKind.NonBlankStringItems);
    }

    [Fact]
    public void Under_legacy_naming_a_string_target_is_a_non_empty_string_and_any_other_target_is_not_null()
    {
        var conditions = RenderLegacyConditions();

        Then(conditions, "prompt").ToJsonString().Should().Be("""{"required":["prompt"],"properties":{"prompt":{"type":"string","minLength":1}}}""");
        Then(conditions, "flag").ToJsonString().Should().Be("""{"required":["flag"],"properties":{"flag":{"not":{"type":"null"}}}}""");
        Then(conditions, "forced").ToJsonString().Should().Be("""{"required":["forced"],"properties":{"forced":{"not":{"type":"null"}}}}""");
    }

    [Fact]
    public void Under_legacy_naming_an_items_target_is_a_non_empty_array_of_non_blank_strings_whatever_its_element_type()
    {
        var conditions = RenderLegacyConditions();
        const string Expected = """{"type":"array","minItems":1,"items":{"type":"string","minLength":1,"pattern":"\\S"}}""";

        Then(conditions, "tags")["properties"]!["tags"]!.ToJsonString().Should().Be(Expected);
        Then(conditions, "numbers")["properties"]!["numbers"]!.ToJsonString().Should().Be(Expected);
    }

    [Fact]
    public void Under_legacy_naming_the_condition_tests_the_dependent_by_const_or_enum()
    {
        var conditions = RenderLegacyConditions();

        conditions.Single(c => c!["then"]!["required"]![0]!.GetValue<string>() == "prompt")!["if"]!.ToJsonString()
            .Should().Be("""{"required":["enabled"],"properties":{"enabled":{"const":true}}}""");
        conditions.Single(c => c!["then"]!["required"]![0]!.GetValue<string>() == "alpha")!["if"]!.ToJsonString()
            .Should().Be("""{"required":["mode"],"properties":{"mode":{"enum":["Advanced","Expert"]}}}""");
    }

    [Fact]
    public void Under_legacy_naming_the_reference_apps_export_command_is_exported_as_legacy_exported_it()
    {
        using var document = RenderSample(OpenApiSchemaNaming.Legacy);
        var allOf = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ExportTemplates").GetProperty("allOf");

        JsonNode.DeepEquals(JsonNode.Parse(allOf.GetRawText()), JsonNode.Parse(
            """
            [
              {
                "if": { "required": [ "notifyOnCompletion" ], "properties": { "notifyOnCompletion": { "const": true } } },
                "then": { "required": [ "notifyEmail" ], "properties": { "notifyEmail": { "type": "string", "minLength": 1 } } }
              },
              {
                "if": { "required": [ "restrictFormats" ], "properties": { "restrictFormats": { "const": true } } },
                "then": {
                  "required": [ "formats" ],
                  "properties": { "formats": { "type": "array", "minItems": 1, "items": { "type": "string", "minLength": 1, "pattern": "\\S" } } }
                }
              }
            ]
            """)).Should().BeTrue(allOf.GetRawText());
    }

    [Fact]
    public void Under_legacy_naming_a_component_reached_as_a_response_is_exported_in_declaration_order()
    {
        // TemplateNotificationRuleApiResource declares RecipientEmail (as "recipient") before Subject, which Default orders
        // by target name and so writes the other way round.
        using var document = RenderSample(OpenApiSchemaNaming.Legacy);
        var allOf = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("TemplateNotificationRuleApiResource").GetProperty("allOf");

        allOf.EnumerateArray().Select(static c => c.GetProperty("then").GetProperty("required")[0].GetString()).Should().Equal("recipient", "Subject");
        JsonNode.Parse(allOf[0].GetProperty("then").GetProperty("properties").GetProperty("recipient").GetRawText())!.ToJsonString().Should().Be("""{"type":"string","minLength":1}""");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Legacy, new[] { "name" })]
    [InlineData(OpenApiSchemaNaming.Default, new[] { "unlessEnabled", "name" })]
    public void A_commands_body_exports_the_conditionals_its_naming_exports(OpenApiSchemaNaming naming, string[] expectedTargets)
    {
        // A request body's conditionals are gathered from the operation's own properties, not from its schema type.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions { SchemaNaming = naming });
        using var document = JsonDocument.Parse(rendered.Json);
        var allOf = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateLegacyConditionalBody").GetProperty("allOf");

        allOf.EnumerateArray().Select(static c => c.GetProperty("then").GetProperty("required")[0].GetString()).Should().Equal(expectedTargets);
    }

    private static JsonDocument RenderSample(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new SampleBlueprintApp(), new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonNode Then(JsonArray conditions, string target)
    {
        return conditions.Single(c => c!["then"]!["required"]![0]!.GetValue<string>() == target)!["then"]!;
    }

    /// <summary>
    /// Extracts the conditionals of <see cref="LegacyConditionalShape"/> under Legacy rules and renders them through the
    /// document writer under Legacy naming, so the assertions are about the JSON written.
    /// </summary>
    private static JsonArray RenderLegacyConditions()
    {
        var requirements = OpenApiSchemaProjectionContext.ExtractSchemaConditionals(typeof(LegacyConditionalShape), JsonNames, useLegacyRules: true);
        var component = new OpenApiSchemaComponentModel(
            "Conditioned",
            "object",
            [],
            [new OpenApiSchemaComponentPropertyModel("enabled", "boolean", IsExtensionData: false, IsNullable: false)])
        {
            Conditionals = requirements,
        };

        var documentBuilderContext = new OpenApiDocumentBuilderContext(new BlueprintApp(), null, [], []);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, [], FrozenDictionary<string, string>.Empty) { Naming = OpenApiSchemaNaming.Legacy };
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, [component]);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(schemaProjectionContext, []);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(responseSchemaProjectionContext, schemaComponentContext, []);
        var operationContext = new OpenApiOperationRenderingContext(responseObjectContext, []);
        var pathItemContext = new OpenApiPathItemRenderingContext(operationContext, []);
        var json = OpenApiOutputRenderingContext.Create(
            OpenApiDocumentRenderingContext.Create(pathItemContext),
            new OpenApiInfoOptions { SchemaNaming = OpenApiSchemaNaming.Legacy }).Json;

        return JsonNode.Parse(json)!["components"]!["schemas"]!["Conditioned"]!["allOf"]!.AsArray();
    }

    /// <summary>Conditionals of each kind, declared with the alphabetically later target first.</summary>
    private sealed class LegacyConditionalShape
    {
        public enum Level
        {
            Basic,
            Advanced,
            Expert,
        }

        public bool Enabled { get; set; }

        public string? Mode { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public string? Zed { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public string? Prompt { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public bool? Flag { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public bool Forced { get; set; }

        [RequiredItemsIf(nameof(Enabled), true)]
        public List<string>? Tags { get; set; }

        [RequiredItemsIf(nameof(Enabled), true)]
        public int[]? Numbers { get; set; }

        // Declared items attribute first: legacy exported a property's [RequiredIf] attributes before its [RequiredItemsIf].
        [RequiredItemsIf(nameof(Enabled), true)]
        [RequiredIf(nameof(Mode), "Basic")]
        public List<string>? Both { get; set; }

        [RequiredIfNot(nameof(Enabled), true)]
        public string? Unless { get; set; }

        [RequiredItemsIfNot(nameof(Enabled), true)]
        public List<string>? UnlessItems { get; set; }

        [RequiredIf(nameof(Mode), null)]
        public string? WhenModeIsNull { get; set; }

        [RequiredIf(nameof(Mode), new[] { Level.Advanced, Level.Expert })]
        public string? Alpha { get; set; }
    }
}

/// <summary>A command whose body declares a <c>[RequiredIf]</c> and a <c>[RequiredIfNot]</c>, which legacy exported only one of.</summary>
[AllowAnonymous]
[RootLink("/legacy-conditional-bodies")]
[HttpPost]
public partial class CreateLegacyConditionalBodyCommand : ICommand<string>
{
    public bool Enabled { get; set; }

    [RequiredIfNot(nameof(Enabled), true)]
    public string? UnlessEnabled { get; set; }

    [RequiredIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    public static string Handle(CreateLegacyConditionalBodyCommand command)
    {
        return "created";
    }
}