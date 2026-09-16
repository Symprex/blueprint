using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the conditional validation attributes (<c>RequiredIf</c>, <c>RequiredIfNot</c>,
/// <c>RequiredItemsIf</c>, <c>RequiredItemsIfNot</c>) are projected into the OpenAPI document as
/// JSON Schema <c>allOf</c>/<c>if</c>/<c>then</c> conditionals, so generated client validators enforce
/// the same cross-property requirements the server does.
/// </summary>
/// <remarks>
/// The expected schemas here are written out by hand from the attribute declarations on the reference
/// app's operations, not derived from the projection, so agreeing with them is a claim about the
/// contract rather than about the code.
/// </remarks>
public class OpenApiConditionalValidationTests
{
    /// <summary>
    /// The conditions declared by <c>ExportTemplatesCommand</c>:
    /// <c>[RequiredIf(nameof(NotifyOnCompletion), true)]</c> on the <c>NotifyEmail</c> string, and
    /// <c>[RequiredItemsIf(nameof(RestrictFormats), true)]</c> on the <c>Formats</c> string collection.
    /// The string constraints carry <c>pattern</c> as well as <c>minLength</c> because both the runtime
    /// attribute and the generated fast path reject a whitespace-only value.
    /// </summary>
    private const string ExpectedExportTemplatesAllOf =
        """
        [
          {
            "if": {
              "required": [ "notifyOnCompletion" ],
              "properties": { "notifyOnCompletion": { "const": true } }
            },
            "then": {
              "required": [ "notifyEmail" ],
              "properties": { "notifyEmail": { "type": "string", "minLength": 1, "pattern": "\\S" } }
            }
          },
          {
            "if": {
              "required": [ "restrictFormats" ],
              "properties": { "restrictFormats": { "const": true } }
            },
            "then": {
              "required": [ "formats" ],
              "properties": {
                "formats": {
                  "type": "array",
                  "minItems": 1,
                  "items": { "type": "string", "minLength": 1, "pattern": "\\S" }
                }
              }
            }
          }
        ]
        """;

    /// <summary>
    /// The conditions declared by <c>TemplateNotificationRuleApiResource</c>: two
    /// <c>[RequiredIf(nameof(Enabled), true)]</c> declarations, on the <c>RecipientEmail</c> and <c>Subject</c>
    /// strings. The type is not an operation — it reaches the document only as the element type of a response, so its
    /// component is built from serializer metadata rather than from a request body.
    /// </summary>
    /// <remarks>
    /// <c>RecipientEmail</c> carries <c>[JsonPropertyName("recipient")]</c>, so the condition must name
    /// <c>recipient</c>: a validator names the CLR property, and only the JSON name means anything to a schema whose
    /// <c>additionalProperties</c> is false. The pair is ordered by target name, which puts <c>Subject</c> first under
    /// ordinal comparison and so disagrees with declaration order.
    /// </remarks>
    private const string ExpectedNotificationRuleAllOf =
        """
        [
          {
            "if": {
              "required": [ "Enabled" ],
              "properties": { "Enabled": { "const": true } }
            },
            "then": {
              "required": [ "Subject" ],
              "properties": { "Subject": { "type": "string", "minLength": 1, "pattern": "\\S" } }
            }
          },
          {
            "if": {
              "required": [ "Enabled" ],
              "properties": { "Enabled": { "const": true } }
            },
            "then": {
              "required": [ "recipient" ],
              "properties": { "recipient": { "type": "string", "minLength": 1, "pattern": "\\S" } }
            }
          }
        ]
        """;

    [Fact]
    public async Task When_body_property_has_required_if_then_containing_schema_carries_conditional()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var document = JsonDocument.Parse(json);

        var exportSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ExportTemplates");

        exportSchema.TryGetProperty("allOf", out var allOf).Should().BeTrue(
            "ExportTemplatesCommand declares [RequiredIf] and [RequiredItemsIf], which must reach the document as conditional schemas");

        var actual = JsonNode.Parse(allOf.GetRawText());
        var expected = JsonNode.Parse(ExpectedExportTemplatesAllOf);

        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            $"the emitted conditionals must match the attribute declarations exactly.{Environment.NewLine}"
            + $"actual:   {actual?.ToJsonString()}{Environment.NewLine}"
            + $"expected: {expected?.ToJsonString()}");
    }

    [Fact]
    public async Task When_conditional_declared_on_nested_schema_component_then_that_component_carries_it()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var document = JsonDocument.Parse(json);

        var ruleSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("TemplateNotificationRuleApiResource");

        ruleSchema.TryGetProperty("allOf", out var allOf).Should().BeTrue(
            "TemplateNotificationRuleApiResource declares two [RequiredIf], and a component built from serializer "
            + "metadata must carry its conditionals just as a request-body component does");

        var actual = JsonNode.Parse(allOf.GetRawText());
        var expected = JsonNode.Parse(ExpectedNotificationRuleAllOf);

        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            $"the emitted conditionals must match the attribute declarations exactly.{Environment.NewLine}"
            + $"actual:   {actual?.ToJsonString()}{Environment.NewLine}"
            + $"expected: {expected?.ToJsonString()}");
    }

    [Fact]
    public async Task When_nested_component_carries_conditional_then_the_body_that_refs_it_does_not()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var document = JsonDocument.Parse(json);

        var responseSchema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/templates/{Id}/notification-rules")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        // The conditional belongs to the type that declares it. The body reaches that type by $ref, so it must
        // carry no conditional of its own — a copy on the referencing schema would apply the requirement to a
        // different instance than the one whose properties it names.
        responseSchema.GetProperty("items").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/TemplateNotificationRuleApiResource");
        responseSchema.TryGetProperty("allOf", out _).Should().BeFalse();

        var carriers = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(static schema => schema.Value.TryGetProperty("allOf", out var allOf)
                && allOf.GetRawText().Contains("recipient", StringComparison.Ordinal))
            .Select(static schema => schema.Name);

        carriers.Should().Equal(
            ["TemplateNotificationRuleApiResource"],
            "only the declaring component may carry the conditional");
    }

    [Fact]
    public async Task When_conditional_emitted_then_target_and_dependent_are_declared_properties_of_the_schema()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var document = JsonDocument.Parse(json);

        var conditionedSchemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Where(static schema => schema.Value.TryGetProperty("allOf", out _))
            .ToList();

        // The loop below is vacuous unless a conditional reaches the document by both routes: the request-body
        // component and the serializer-metadata component.
        conditionedSchemas.Select(static schema => schema.Name).Should().BeEquivalentTo(
            ["ExportTemplates", "TemplateNotificationRuleApiResource"]);

        foreach (var schema in conditionedSchemas)
        {
            // The schema renders "additionalProperties": false, which rejects any instance member absent
            // from the outer "properties" map regardless of what an allOf branch declares. A condition
            // naming an undeclared property would therefore be unsatisfiable rather than merely unhelpful.
            schema.Value.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();

            var declaredProperties = schema.Value.GetProperty("properties")
                .EnumerateObject()
                .Select(static property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var condition in schema.Value.GetProperty("allOf").EnumerateArray())
            {
                foreach (var branch in new[] { "if", "then" })
                {
                    foreach (var required in condition.GetProperty(branch).GetProperty("required").EnumerateArray())
                    {
                        declaredProperties.Should().Contain(
                            required.GetString()!,
                            $"a conditional may only reference properties schema '{schema.Name}' declares");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task When_conditional_declared_on_query_property_then_no_conditional_is_emitted()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync();
        using var document = JsonDocument.Parse(json);

        // GetTemplateHistoryQuery.EndDate declares [RequiredIfNot(nameof(StartDate), null)], but its
        // properties bind from the query string and render into the operation's "parameters" array,
        // which has no containing object schema to hang an if/then off. The condition is skipped
        // rather than mis-attached to an unrelated schema.
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var schema in schemas.EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("allOf", out var allOf))
            {
                continue;
            }

            foreach (var condition in allOf.EnumerateArray())
            {
                condition.GetProperty("then").GetProperty("required").EnumerateArray()
                    .Select(static target => target.GetString())
                    .Should().NotContain(
                        "endDate",
                        $"the query-bound conditional on GetTemplateHistoryQuery must not surface on schema '{schema.Name}'");
            }
        }
    }

    [Fact]
    public void When_conditional_target_is_a_non_nullable_value_type_then_no_conditional_is_extracted()
    {
        // HasRequiredValue returns true for any non-null, non-string value and the generated fast path produces no rule
        // for a non-nullable value type, so the requirement can never fail server-side. Emitting one would reject a
        // payload omitting the property that the server accepts — the over-enforcement this projection must never ship.
        ExtractFor(nameof(ConditionalTargetShapes.RequiredBool)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(ConditionalTargetShapes.RequiredString), OpenApiConditionalTargetKind.NonBlankString)]
    [InlineData(nameof(ConditionalTargetShapes.RequiredNullableInt), OpenApiConditionalTargetKind.NonNull)]
    [InlineData(nameof(ConditionalTargetShapes.RequiredReference), OpenApiConditionalTargetKind.NonNull)]
    [InlineData(nameof(ConditionalTargetShapes.RequiredStringItems), OpenApiConditionalTargetKind.NonBlankStringItems)]
    [InlineData(nameof(ConditionalTargetShapes.RequiredIntItems), OpenApiConditionalTargetKind.NonNullItems)]
    public void When_conditional_extracted_then_target_kind_follows_the_clr_type(string propertyName, OpenApiConditionalTargetKind expected)
    {
        // RequiredItemsIfAttribute.HasItems tests IsNullOrWhiteSpace(item.ToString()) whatever the element type, so a
        // List<int> of [1, 2] satisfies it. Legacy Blueprint hardcodes items: { "type": "string" }, which would reject
        // that payload client-side; the element type has to decide the item constraint.
        SingleRequirementFor(propertyName).TargetKind.Should().Be(expected);
    }

    [Fact]
    public void When_item_target_has_no_single_element_type_then_no_conditional_is_extracted()
    {
        ExtractFor(nameof(ConditionalTargetShapes.RequiredMapItems)).Should().BeEmpty(
            "without an element type there is no item constraint certain not to over-reach");
    }

    [Fact]
    public void When_dependent_value_is_null_then_the_conditional_is_still_extracted()
    {
        // Legacy Blueprint skips a null dependent value as a server-side no-op. That is untrue here: DependentValues is
        // compared through value?.ToString(), so a null dependent value matches exactly when the dependent property is
        // null and the requirement genuinely activates. Skipping it would drop a constraint the runtime enforces.
        var requirement = SingleRequirementFor(nameof(ConditionalTargetShapes.RequiredWhenDependentIsNull));

        requirement.DependentValues.Should().Equal([null]);
        requirement.DependentValueNegated.Should().BeFalse();
    }

    [Fact]
    public void When_attribute_is_the_not_variant_then_the_dependent_test_is_negated()
    {
        SingleRequirementFor(nameof(ConditionalTargetShapes.RequiredUnlessDependentMatches))
            .DependentValueNegated.Should().BeTrue();
    }

    [Fact]
    public void When_attribute_is_required_items_if_not_then_it_is_negated_and_keeps_the_item_constraint()
    {
        // The fourth attribute is the one most easily left out of the switch, or wired to the scalar target kind: both
        // mistakes compile, and both would go unnoticed because the reference app does not declare it.
        var requirement = SingleRequirementFor(nameof(ConditionalTargetShapes.ItemsRequiredUnlessDependentMatches));

        requirement.DependentValueNegated.Should().BeTrue();
        requirement.TargetKind.Should().Be(OpenApiConditionalTargetKind.NonBlankStringItems);
    }

    [Fact]
    public void When_dependent_values_include_enums_then_they_are_normalised_to_member_names()
    {
        // Enums render as type: string in the document, so a dependent condition has to compare against the member name
        // rather than the boxed enum's numeric value.
        SingleRequirementFor(nameof(ConditionalTargetShapes.RequiredForSeveralModes))
            .DependentValues.Should().Equal(["Advanced", "Expert"]);
    }

    [Fact]
    public void When_a_property_declares_several_conditionals_then_their_order_is_deterministic()
    {
        // Reflection does not order repeated attributes, and the rendered document carries an ETag over its bytes.
        ExtractFor(nameof(ConditionalTargetShapes.RequiredByTwoDependents))
            .Select(static requirement => requirement.DependentPropertyName)
            .Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void When_a_property_declares_a_scalar_and_an_items_conditional_on_the_same_dependent_then_target_kind_breaks_the_tie()
    {
        // RequiredIf and RequiredItemsIf on the one List<string>? property share both target (this property) and
        // dependent (Dependent), so ordering by dependent name alone ties. Reflection does not order repeated
        // attributes on one member, and the rendered document carries an ETag over its bytes, so TargetKind — not
        // reflection's unordered attribute sequence — must be what closes the tie.
        ExtractFor(nameof(ConditionalTargetShapes.RequiredByScalarAndItemsOnSameDependent))
            .Select(static requirement => requirement.TargetKind)
            .Should().Equal(OpenApiConditionalTargetKind.NonNull, OpenApiConditionalTargetKind.NonBlankStringItems);
    }

    [Theory]
    [InlineData(nameof(ConditionalTargetShapes.RequiredWhenUnsignedDependentMatches), """{"if":{"required":["mode"],"properties":{"mode":{"const":5}}},"then":{"required":["note"],"properties":{"note":{"type":"string","minLength":1,"pattern":"\\S"}}}}""")]
    [InlineData(nameof(ConditionalTargetShapes.RequiredWhenSignedByteDependentMatches), """{"if":{"required":["mode"],"properties":{"mode":{"const":-3}}},"then":{"required":["note"],"properties":{"note":{"type":"string","minLength":1,"pattern":"\\S"}}}}""")]
    public void When_dependent_value_is_an_unmapped_integral_then_it_is_written_as_a_number(string propertyName, string expected)
    {
        // These properties render as "type": "integer" in the document, so a string const could never match one.
        RenderedConditionFor(propertyName).Should().Be(expected);
    }

    [Fact]
    public void When_dependent_value_is_unsigned_then_the_negated_form_still_writes_a_number()
    {
        // This is the branch where the "never matches, so fails safe" reasoning inverts. RequiredIfNot wraps the test in
        // "not": a const that can never match makes the not always true, so the then always applies and the generated
        // client blocks a payload the server accepts — the over-enforcement this projection must never ship.
        RenderedConditionFor(nameof(ConditionalTargetShapes.RequiredUnlessUnsignedDependentMatches))
            .Should().Be("""{"if":{"required":["mode"],"properties":{"mode":{"not":{"const":5}}}},"then":{"required":["note"],"properties":{"note":{"type":"string","minLength":1,"pattern":"\\S"}}}}""");
    }

    [Fact]
    public void When_a_dependent_value_cannot_be_represented_faithfully_then_the_conditional_is_skipped()
    {
        // Guessing at a JSON representation is only safe while the condition can merely fail to match. Under "not" an
        // unmatchable condition is always true, so a value that cannot be rendered faithfully must drop the whole
        // requirement instead: that under-enforces, which is the safe direction.
        ExtractFor(nameof(ConditionalTargetShapes.RequiredWhenDependentIsAType)).Should().BeEmpty();
    }

    [Fact]
    public void When_dependent_value_is_a_char_then_it_is_written_as_a_string()
    {
        // char renders as "type": "string" in the document, so a string const is right — deliberately, rather than by
        // falling through an untyped default.
        RenderedConditionFor(nameof(ConditionalTargetShapes.RequiredWhenCharDependentMatches))
            .Should().Be("""{"if":{"required":["mode"],"properties":{"mode":{"const":"y"}}},"then":{"required":["note"],"properties":{"note":{"type":"string","minLength":1,"pattern":"\\S"}}}}""");
    }

    [Fact]
    public void When_schema_has_no_conditionals_then_no_allOf_is_written()
    {
        // Schemas that declare no conditional validator must stay byte-identical, so an empty collection emits nothing
        // rather than an empty array.
        var json = RenderComponent(component => component);

        json.Should().NotContain("allOf");
    }

    [Fact]
    public void When_conditional_is_negated_then_dependent_test_is_wrapped_in_not()
    {
        var json = RenderComponent(component => component with
        {
            Conditionals = [new OpenApiConditionalRequirement("mode", [true], DependentValueNegated: true, "note", OpenApiConditionalTargetKind.NonBlankString)],
        });

        ConditionOf(json).Should().Be(
            """{"if":{"required":["mode"],"properties":{"mode":{"not":{"const":true}}}},"then":{"required":["note"],"properties":{"note":{"type":"string","minLength":1,"pattern":"\\S"}}}}""");
    }

    [Fact]
    public void When_conditional_has_several_dependent_values_then_an_enum_test_is_written()
    {
        var json = RenderComponent(component => component with
        {
            Conditionals = [new OpenApiConditionalRequirement("mode", ["Advanced", "Expert"], DependentValueNegated: false, "note", OpenApiConditionalTargetKind.NonNull)],
        });

        // A NonNull target contributes no property constraint: "required" is the whole of it, and the stronger
        // { "not": { "type": "null" } } is not legal in the OpenAPI 3.0 dialect this document declares.
        ConditionOf(json).Should().Be(
            """{"if":{"required":["mode"],"properties":{"mode":{"enum":["Advanced","Expert"]}}},"then":{"required":["note"]}}""");
    }

    [Fact]
    public void When_dependent_value_is_null_then_a_null_const_test_is_written()
    {
        var json = RenderComponent(component => component with
        {
            Conditionals = [new OpenApiConditionalRequirement("mode", [null], DependentValueNegated: true, "note", OpenApiConditionalTargetKind.NonNull)],
        });

        ConditionOf(json).Should().Be(
            """{"if":{"required":["mode"],"properties":{"mode":{"not":{"const":null}}}},"then":{"required":["note"]}}""");
    }

    [Fact]
    public void When_item_target_is_not_a_string_collection_then_items_are_not_narrowed_to_string()
    {
        var json = RenderComponent(component => component with
        {
            Conditionals = [new OpenApiConditionalRequirement("mode", [true], DependentValueNegated: false, "note", OpenApiConditionalTargetKind.NonNullItems)],
        });

        ConditionOf(json).Should().Be(
            """{"if":{"required":["mode"],"properties":{"mode":{"const":true}}},"then":{"required":["note"],"properties":{"note":{"type":"array","minItems":1}}}}""");
    }

    [Fact]
    public void When_schema_conditional_is_resolved_then_it_names_the_json_properties_the_schema_declares()
    {
        // The reference app's serializer context applies no naming policy, so its JSON names match its CLR names and
        // would agree with a projection that never resolved them at all. This map does not.
        ExtractSchemaConditionalsForShape()
            .Select(static requirement => (requirement.DependentPropertyName, requirement.TargetPropertyName))
            .Should().Equal([("enabled", "placeholder"), ("enabled", "prompt")]);
    }

    [Fact]
    public void When_schema_conditional_names_a_property_the_schema_does_not_declare_then_it_is_dropped()
    {
        // The rendered component carries "additionalProperties": false, so a condition naming a member the schema
        // does not declare is unsatisfiable rather than merely unhelpful — and under the negated form it would apply
        // to every payload, blocking client-side what the server accepts.
        var requirements = ExtractSchemaConditionalsForShape();

        requirements.Select(static requirement => requirement.DependentPropertyName)
            .Should().NotContain(
                "ServerOnly",
                "a conditional whose dependent the schema does not declare must be dropped, not carried over as its CLR name");

        requirements.Select(static requirement => requirement.TargetPropertyName)
            .Should().NotContain(
                "Undeclared",
                "a conditional whose target the schema does not declare must be dropped, not carried over as its CLR name");
    }

    [Fact]
    public void When_schema_declares_several_conditionals_then_their_order_is_deterministic()
    {
        // GetProperties guarantees no order and the rendered document carries an ETag over its bytes. Prompt is
        // declared before Placeholder, so declaration order and the ordered result disagree.
        ExtractSchemaConditionalsForShape()
            .Select(static requirement => requirement.TargetPropertyName)
            .Should().Equal("placeholder", "prompt");
    }

    [Fact]
    public void When_an_ignored_member_s_computed_json_name_collides_with_a_declared_property_then_the_conditional_is_dropped_not_repointed()
    {
        // Owner is [JsonIgnore]d, so the serializer never declares a JSON property for it: STJ's own typeInfo.Properties
        // omits it entirely. OwnerId carries [JsonPropertyName("Owner")], so Owner's *computed* JSON name (its own CLR
        // name, since JsonIgnore does not touch naming) collides with the name OwnerId actually declares. A map built by
        // testing that computed name for membership in the declared set would wrongly admit Owner and re-point the
        // RequiredIfNot below — written against the ignored Owner — onto OwnerId. Keying on the serializer's own
        // declared properties cannot make that substitution, so the conditional must be dropped instead.
        var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var typeInfo = options.GetTypeInfo(typeof(AliasCollisionShape));

        var declaredJsonNamesByClrName = OpenApiSchemaProjectionContext.CreateDeclaredJsonNamesByClrName(typeInfo);
        var requirements = OpenApiSchemaProjectionContext.ExtractSchemaConditionals(
            typeof(AliasCollisionShape),
            declaredJsonNamesByClrName);

        requirements.Should().BeEmpty(
            "Owner is JsonIgnore'd and so declares no JSON property for the conditional to bind to; it must be "
            + "dropped rather than silently re-pointed onto the unrelated OwnerId property that happens to share its "
            + "computed JSON name");
    }

    /// <summary>
    /// Projects <see cref="NestedSettingsShape"/>'s conditionals against a schema that declares only some of its
    /// properties, and under JSON names that differ from the CLR ones.
    /// </summary>
    private static ImmutableArray<OpenApiConditionalRequirement> ExtractSchemaConditionalsForShape()
    {
        var declaredJsonNamesByClrName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Enabled"] = "enabled",
            ["Prompt"] = "prompt",
            ["Placeholder"] = "placeholder",
            ["DependsOnUndeclared"] = "dependsOnUndeclared",
        };

        return OpenApiSchemaProjectionContext.ExtractSchemaConditionals(
            typeof(NestedSettingsShape),
            declaredJsonNamesByClrName);
    }

    private static ImmutableArray<OpenApiConditionalRequirement> ExtractFor(string propertyName)
    {
        var property = typeof(ConditionalTargetShapes).GetProperty(propertyName);
        property.Should().NotBeNull($"the fixture should declare '{propertyName}'");

        return OpenApiValidationConstraints.ExtractConditionalRequirements(property);
    }

    /// <summary>
    /// Asserts a property yields exactly one conditional before returning it. Reaching for <c>Single()</c> directly
    /// turns an unprojected attribute into "Sequence contains no elements", which names the harness rather than the
    /// behaviour that went missing.
    /// </summary>
    private static OpenApiConditionalRequirement SingleRequirementFor(string propertyName)
    {
        var requirements = ExtractFor(propertyName);

        requirements.Should().HaveCount(
            1,
            $"'{propertyName}' declares exactly one conditional validator, which must be projected");

        return requirements[0];
    }

    /// <summary>
    /// Extracts the single conditional declared on a fixture property, renames its properties onto the rendering
    /// fixture's schema, and renders it through the real document writer — so the assertion covers extraction and
    /// writing together rather than only one of them.
    /// </summary>
    private static string RenderedConditionFor(string propertyName)
    {
        var requirement = SingleRequirementFor(propertyName) with
        {
            DependentPropertyName = "mode",
            TargetPropertyName = "note",
        };

        return ConditionOf(RenderComponent(component => component with { Conditionals = [requirement] }));
    }

    /// <summary>
    /// Renders a single schema component through the real document writer so the assertions below are about the emitted
    /// JSON rather than about the model that produced it.
    /// </summary>
    private static string RenderComponent(Func<OpenApiSchemaComponentModel, OpenApiSchemaComponentModel> configure)
    {
        var component = configure(new OpenApiSchemaComponentModel(
            "Conditioned",
            "object",
            [],
            [new OpenApiSchemaComponentPropertyModel("mode", "boolean", IsExtensionData: false, IsNullable: false),
             new OpenApiSchemaComponentPropertyModel("note", "string", IsExtensionData: false, IsNullable: true)]));

        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            [],
            []);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, [], FrozenDictionary<string, string>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, [component]);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(schemaProjectionContext, []);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(responseSchemaProjectionContext, schemaComponentContext, []);
        var operationContext = new OpenApiOperationRenderingContext(responseObjectContext, []);
        var pathItemContext = new OpenApiPathItemRenderingContext(operationContext, []);

        return OpenApiOutputRenderingContext.Create(OpenApiDocumentRenderingContext.Create(pathItemContext)).Json;
    }

    /// <summary>
    /// Extracts the single rendered condition from the <c>Conditioned</c> component, compacted so the expected JSON in
    /// each test can be written out by hand.
    /// </summary>
    private static string ConditionOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        var allOf = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Conditioned")
            .GetProperty("allOf");

        return JsonSerializer.Serialize(allOf.EnumerateArray().Single());
    }

    /// <summary>
    /// A DTO where an ignored navigation property's computed JSON name collides with a different, genuinely declared
    /// property's <c>[JsonPropertyName]</c>. <c>Owner</c> is never serialised; <c>OwnerId</c> is the property that
    /// actually renders under the name <c>"Owner"</c>.
    /// </summary>
    private sealed class AliasCollisionShape
    {
        [JsonIgnore]
        public OwnerReference? Owner { get; set; }

        [JsonPropertyName("Owner")]
        public string? OwnerId { get; set; }

        [RequiredIfNot(nameof(Owner), null)]
        public string? Note { get; set; }
    }

    private sealed class OwnerReference
    {
        public string? Id { get; set; }
    }

    /// <summary>
    /// A nested settings DTO whose schema declares only some of its CLR properties, and under different names. It
    /// stands in for the shapes a serializer context surfaces selectively: <c>ServerOnly</c> and <c>Undeclared</c> are
    /// absent from the schema the way a <c>[JsonIgnore]</c>d or extension-data member is.
    /// </summary>
    private sealed class NestedSettingsShape
    {
        public bool Enabled { get; set; }

        public bool ServerOnly { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public string? Prompt { get; set; }

        [RequiredIf(nameof(ServerOnly), true)]
        public string? DependsOnUndeclared { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public string? Undeclared { get; set; }

        [RequiredIf(nameof(Enabled), true)]
        public string? Placeholder { get; set; }
    }

    /// <summary>
    /// Conditional declarations covering the target shapes the reference app does not exercise. The dependent property
    /// names differ per case so each extraction is independent.
    /// </summary>
    private sealed class ConditionalTargetShapes
    {
        public enum Mode
        {
            Basic,
            Advanced,
            Expert,
        }

        public bool Dependent { get; set; }

        public string? Alpha { get; set; }

        public string? Beta { get; set; }

        [RequiredIf(nameof(Dependent), true)]
        public bool RequiredBool { get; set; }

        [RequiredIf(nameof(Dependent), true)]
        public string? RequiredString { get; set; }

        [RequiredIf(nameof(Dependent), true)]
        public int? RequiredNullableInt { get; set; }

        [RequiredIf(nameof(Dependent), true)]
        public Uri? RequiredReference { get; set; }

        [RequiredItemsIf(nameof(Dependent), true)]
        public List<string>? RequiredStringItems { get; set; }

        [RequiredItemsIf(nameof(Dependent), true)]
        public int[]? RequiredIntItems { get; set; }

        [RequiredItemsIf(nameof(Dependent), true)]
        public Dictionary<string, string>? RequiredMapItems { get; set; }

        [RequiredIf(nameof(Alpha), null)]
        public string? RequiredWhenDependentIsNull { get; set; }

        [RequiredIfNot(nameof(Dependent), true)]
        public string? RequiredUnlessDependentMatches { get; set; }

        [RequiredItemsIfNot(nameof(Dependent), true)]
        public List<string>? ItemsRequiredUnlessDependentMatches { get; set; }

        public uint UnsignedDependent { get; set; }

        public sbyte SignedByteDependent { get; set; }

        public char CharDependent { get; set; }

        [RequiredIf(nameof(UnsignedDependent), 5u)]
        public string? RequiredWhenUnsignedDependentMatches { get; set; }

        [RequiredIf(nameof(SignedByteDependent), (sbyte)-3)]
        public string? RequiredWhenSignedByteDependentMatches { get; set; }

        [RequiredIfNot(nameof(UnsignedDependent), 5u)]
        public string? RequiredUnlessUnsignedDependentMatches { get; set; }

        [RequiredIf(nameof(CharDependent), 'y')]
        public string? RequiredWhenCharDependentMatches { get; set; }

        [RequiredIf(nameof(Alpha), typeof(string))]
        public string? RequiredWhenDependentIsAType { get; set; }

        [RequiredIf(nameof(Alpha), new[] { Mode.Advanced, Mode.Expert })]
        public string? RequiredForSeveralModes { get; set; }

        [RequiredIf(nameof(Beta), "b")]
        [RequiredIf(nameof(Alpha), "a")]
        public string? RequiredByTwoDependents { get; set; }

        // Declared items-attribute-first so a pre-fix ordering that relies only on reflection's (unstable) attribute
        // sequence for a same-target/same-dependent tie is exercised in the direction most likely to disagree with
        // the fixed, TargetKind-driven order below.
        [RequiredItemsIf(nameof(Dependent), true)]
        [RequiredIf(nameof(Dependent), true)]
        public List<string>? RequiredByScalarAndItemsOnSameDependent { get; set; }
    }

    private static async Task<(string Json, int StatusCode)> InvokeOpenApiEndpointAsync()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static candidate => candidate.RoutePattern.RawText == "api/.blueprint/openapi.json");

        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/.blueprint/openapi.json";

        await endpoint.RequestDelegate!(context);
        body.Position = 0;

        var json = await new StreamReader(body).ReadToEndAsync();
        return (json, context.Response.StatusCode);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }
}
