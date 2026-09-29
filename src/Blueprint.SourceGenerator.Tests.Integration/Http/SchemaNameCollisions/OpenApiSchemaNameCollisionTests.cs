using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.SchemaNameCollisions;

/// <summary>
/// Proves that types whose schema names collide each get a component of their own, named as legacy Blueprint named them,
/// and that every reference lands on the component of its own type.
/// </summary>
/// <remarks>
/// <para>
/// Legacy registered each schema under NJsonSchema's <c>DefaultTypeNameGenerator</c>: the first type to reach a name
/// keeps it, and each later one takes the name with the first free counter from 2 appended. It reached them in the
/// order it walked the document — operation by operation, a request body before the responses, a type before the types
/// its properties reach. Signature365's committed Generation document has four such pairs (<c>RuleMatch</c> and
/// <c>RuleMatch2</c>, and so on): each a Generation <c>...ApiResource</c> and the Portal type of the same name that
/// <c>Replace("ApiResource", "")</c> maps it to.
/// </para>
/// <para>
/// Every expected name here is legacy's, from its OpenAPI document builder run on the same shapes with NJsonSchema 11.1,
/// with the operations in the order this document writes them — legacy registered them in assembly order, which this
/// document does not know. The one departure is a response whose type collides with a name already taken: legacy
/// reused the schema already under that name, documenting the response as a different type, where this gives it the
/// component of its own type.
/// </para>
/// </remarks>
public class OpenApiSchemaNameCollisionTests
{
    private const string Schemas = "#/components/schemas/";

    [Fact]
    public void Types_a_formatter_maps_together_take_legacy_counters_in_the_order_the_document_reaches_them()
    {
        using var document = RenderAsSignature365();

        // The log's match is reached first, though the preview sorts first by name and Portal's type sorts first by its own.
        Properties(document, "CollisionMatch").Should().Equal("OnlyOnMatchResource");
        Properties(document, "CollisionMatch2").Should().Equal("OnlyOnPortal");
        RefsIn(Component(document, "CollisionTrail").GetProperty("properties").GetProperty("Match")).Should().Equal(Schemas + "CollisionMatch");
        RefsIn(Component(document, "CollisionPreview").GetProperty("properties").GetProperty("Match")).Should().Equal(Schemas + "CollisionMatch2");
    }

    [Fact]
    public void A_name_three_types_share_counts_on_from_2_whichever_way_they_came_to_share_it()
    {
        using var document = RenderAsSignature365();

        // One by the formatter, two by a shared simple name: legacy counted all three as one.
        Properties(document, "CollisionItem").Should().Equal("OnlyOnResource");
        Properties(document, "CollisionItem2").Should().Equal("OnlyOnOne");
        Properties(document, "CollisionItem3").Should().Equal("OnlyOnTwo");
        ComponentNames(document).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_reference_to_a_colliding_type_lands_on_its_own_component()
    {
        using var document = RenderAsSignature365();
        var holder = Component(document, "CollisionHolder").GetProperty("properties");

        RefsIn(holder.GetProperty("Alpha")).Should().Equal(Schemas + "CollisionItem");
        RefsIn(holder.GetProperty("Bravo")).Should().Equal(Schemas + "CollisionItem2");
        RefsIn(holder.GetProperty("Charlie")).Should().Equal(Schemas + "CollisionItem3");
        RefsIn(holder.GetProperty("Delta").GetProperty("items")).Should().Equal(Schemas + "CollisionItem3");
        RefsIn(holder.GetProperty("Echo").GetProperty("additionalProperties")).Should().Equal(Schemas + "CollisionItem2");
        RefsIn(Component(document, "SubmitCollisionCommand").GetProperty("properties").GetProperty("item")).Should().Equal(Schemas + "CollisionItem3");

        // Legacy documented these responses as CollisionItem, the schema already under the name: a different type.
        ResponseRefs(document, "/collisions/one").Should().Equal(Schemas + "CollisionItem2");
        ResponseRefs(document, "/collisions/two").Should().Equal(Schemas + "CollisionItem3");
    }

    [Fact]
    public void An_enum_shares_the_names_objects_are_given()
    {
        using var document = RenderAsSignature365();
        var holder = Component(document, "CollisionHolder").GetProperty("properties");

        Component(document, "CollisionKind").GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Plain", "Fancy");
        Properties(document, "CollisionKind2").Should().Equal("OnlyOnKindResource");
        RefsIn(holder.GetProperty("Foxtrot")).Should().Equal(Schemas + "CollisionKind");
        RefsIn(holder.GetProperty("Golf")).Should().Equal(Schemas + "CollisionKind2");
    }

    [Fact]
    public void Two_distinct_enums_of_one_simple_name_each_get_a_component_with_its_own_values()
    {
        using var document = RenderAsSignature365();
        var holder = Component(document, "CollisionHolder").GetProperty("properties");

        Component(document, "CollisionStatus").GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Draft", "Live");
        Component(document, "CollisionStatus2").GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Archived", "Active", "Pending");
        RefsIn(holder.GetProperty("Hotel")).Should().Equal(Schemas + "CollisionStatus");
        RefsIn(holder.GetProperty("India")).Should().Equal(Schemas + "CollisionStatus2");
    }

    [Fact]
    public void A_ResourceEvent_envelope_is_named_from_the_inner_types_final_key_not_its_projection_time_name()
    {
        using var document = Render(new OpenApiInfoOptions());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var envelopeNames = schemas.EnumerateObject()
            .Select(static p => p.Name)
            .Where(static name => name.StartsWith("ResourceEventOfEnvelopeMatch", StringComparison.Ordinal))
            .ToList();

        foreach (var envelopeName in envelopeNames)
        {
            var innerRef = RefsIn(schemas.GetProperty(envelopeName).GetProperty("properties").GetProperty("data")).Single();
            var innerKey = innerRef[Schemas.Length..];

            // The envelope's own key must always be "ResourceEventOf" + the exact key its "data" $ref resolves to —
            // whatever the reach order gave that key, never the projection-time name baked in ad hoc.
            envelopeName.Should().Be("ResourceEventOf" + innerKey);
        }

        envelopeNames.Should().HaveCount(2);

        // A third type of the name, returned plain at the URL that sorts first, is reached before either envelope's inner type
        // and takes the base key, so both envelopes wrap a type whose final key is not its base name; only reaching the inner
        // type before building the envelope's key (R28) names the envelope from it (R135).
        ResponseRefs(document, "/collisions/envelopes/000-three").Should().Equal(Schemas + "EnvelopeMatch");
    }

    [Fact]
    public void Under_the_default_naming_two_types_of_one_simple_name_each_get_a_component()
    {
        using var document = Render(new OpenApiInfoOptions());
        var holder = Component(document, "CollisionHolderApiResource").GetProperty("properties");

        Properties(document, "CollisionItemApiResource").Should().Equal("OnlyOnResource");
        Properties(document, "CollisionItem").Should().Equal("OnlyOnOne");
        Properties(document, "CollisionItem2").Should().Equal("OnlyOnTwo");

        RefsIn(holder.GetProperty("Bravo")).Should().Equal(Schemas + "CollisionItem");
        RefsIn(holder.GetProperty("Charlie")).Should().Equal(Schemas + "CollisionItem2");
        RefsIn(holder.GetProperty("Delta").GetProperty("items")).Should().Equal(Schemas + "CollisionItem2");
        RefsIn(holder.GetProperty("Echo").GetProperty("additionalProperties")).Should().Equal(Schemas + "CollisionItem");
        RefsIn(Component(document, "SubmitCollision").GetProperty("properties").GetProperty("item")).Should().Equal(Schemas + "CollisionItem2");
        ResponseRefs(document, "/collisions/one").Should().Equal(Schemas + "CollisionItem");
        ResponseRefs(document, "/collisions/two").Should().Equal(Schemas + "CollisionItem2");
        ComponentNames(document).Should().OnlyHaveUniqueItems();
    }

    private static JsonDocument RenderAsSignature365()
    {
        return Render(new OpenApiInfoOptions
        {
            SchemaNaming = OpenApiSchemaNaming.Legacy,
            RequestBodyNaming = OpenApiRequestBodyNaming.OperationType,
            QueryParameterNaming = OpenApiParameterNaming.PropertyName,
            SchemaNameFormatter = static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal),
        });
    }

    private static JsonDocument Render(OpenApiInfoOptions options)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static IReadOnlyList<string> Properties(JsonDocument document, string component)
    {
        return Component(document, component).GetProperty("properties").EnumerateObject().Select(static p => p.Name).ToList();
    }

    private static IReadOnlyList<string> ComponentNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(static p => p.Name).ToList();
    }

    private static IReadOnlyList<string> ResponseRefs(JsonDocument document, string path)
    {
        return RefsIn(document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200"));
    }

    /// <summary>Every <c>$ref</c> in <paramref name="schema"/>, however it is wrapped (<c>oneOf</c> beside <c>nullable</c>, and so on).</summary>
    private static IReadOnlyList<string> RefsIn(JsonElement schema)
    {
        var refs = new List<string>();
        Collect(schema);

        return refs;

        void Collect(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "$ref")
                        {
                            refs.Add(property.Value.GetString()!);
                        }
                        else
                        {
                            Collect(property.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Collect(item);
                    }

                    break;
            }
        }
    }
}
