using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves, on the legacy-shaped fixtures, that the rendered OpenAPI document can reproduce the component names and
/// shapes legacy Blueprint gave Signature365's documents, which its generated TypeScript clients import by name.
/// </summary>
/// <remarks>
/// The formatter used is Signature365's legacy rule, <c>Replace("ApiResource", "").Replace("PagedOf", "Paged")</c>
/// (legacy Blueprint's <c>BlueprintSchemaNameGenerator</c>), supplied by the consumer rather than the framework. The
/// expected keys follow from NJsonSchema's names for the same types, as <see cref="OpenApiLegacySchemaNamingTests"/>
/// pins them.
/// </remarks>
public class OpenApiLegacyDocumentTests
{
    [Fact]
    public void Legacy_naming_keys_components_by_their_NJsonSchema_names()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        var components = ComponentNames(document);

        components.Should().Contain([
            "PagedApiResourceOfLegacyShapeTenantApiResource",
            "LegacyShapeListApiResourceOfLegacyShapeCountryApiResource",
            "LegacyShapeTenantApiResource",
            "LegacyShapeRun",
            "LegacyShapeStatus",
        ]);
        components.Should().NotContain(static name => name.StartsWith("PagedApiResourceOfLegacyShapeTenant", StringComparison.Ordinal)
                                                     && name != "PagedApiResourceOfLegacyShapeTenantApiResource");
    }

    [Fact]
    public void Legacy_naming_hands_the_formatter_those_names_so_a_consumer_can_reproduce_its_legacy_keys()
    {
        var received = new List<string>();

        using var document = Render(options =>
        {
            options.SchemaNaming = OpenApiSchemaNaming.Legacy;
            options.SchemaNameFormatter = name =>
            {
                received.Add(name);

                return name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal);
            };
        });

        received.Should().Contain("PagedApiResourceOfLegacyShapeTenantApiResource")
            .And.NotContain(static name => name.Contains('.') || name.Contains('<'), "the formatter sees names, never CLR type names");

        ComponentNames(document).Should().Contain([
            "PagedLegacyShapeTenant",
            "LegacyShapeListOfLegacyShapeCountry",
            "LegacyShapeTenant",
            "LegacyShapeRun",
        ]);

        ResponseSchemaRef(document, "/legacy-shape/tenants", "get", "200").Should().Be("#/components/schemas/PagedLegacyShapeTenant");
        ResponseSchemaRef(document, "/legacy-shape/countries", "get", "200").Should().Be("#/components/schemas/LegacyShapeListOfLegacyShapeCountry");
        ResponseSchemaRef(document, "/legacy-shape/runs/{Id}", "get", "200").Should().Be("#/components/schemas/LegacyShapeRun");
    }

    [Fact]
    public void A_type_named_per_type_is_referenced_by_that_name_from_response_and_request_body_alike()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        var components = ComponentNames(document);

        components.Should().Contain("LegacyShapeTier").And.NotContain("LegacyShapeTierKind");
        AllRefs(document.RootElement).Should().Contain("#/components/schemas/LegacyShapeTier");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void Every_ref_resolves_to_exactly_one_component(OpenApiSchemaNaming naming)
    {
        using var document = Render(options =>
        {
            options.SchemaNaming = naming;
            options.SchemaNameFormatter = naming == OpenApiSchemaNaming.Legacy
                ? static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal)
                : null;
        });

        var components = ComponentNames(document);

        components.Should().OnlyHaveUniqueItems();
        AllRefs(document.RootElement)
            .Select(static reference => reference["#/components/schemas/".Length..])
            .Should().BeSubsetOf(components);
    }

    [Fact]
    public void Two_components_a_formatter_maps_to_one_key_are_told_apart_by_legacy_counter()
    {
        using var document = Render(static options =>
        {
            options.SchemaNaming = OpenApiSchemaNaming.Legacy;
            options.SchemaNameFormatter = static name => name is "LegacyShapeRun" or "LegacyShapeTenantApiResource" ? "LegacyShapeClash" : name;
        });

        // The tenant is reached first, in the event the partner's tenants path creates; the run after it, at its own path.
        PropertyNames(document, "LegacyShapeClash").Should().Contain("TenantId");
        PropertyNames(document, "LegacyShapeClash2").Should().Contain("Id").And.NotContain("TenantId");
        ResponseSchemaRef(document, "/legacy-shape/runs/{Id}", "get", "200").Should().Be("#/components/schemas/LegacyShapeClash2");
        ComponentNames(document).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Keying_request_bodies_by_operation_type_names_them_after_the_command_as_legacy_did()
    {
        using var document = Render(static options =>
        {
            options.SchemaNaming = OpenApiSchemaNaming.Legacy;
            options.RequestBodyNaming = OpenApiRequestBodyNaming.OperationType;
            options.SchemaNameFormatter = static name => name.Replace("ApiResource", string.Empty, StringComparison.Ordinal).Replace("PagedOf", "Paged", StringComparison.Ordinal);
        });

        RequestBodySchemaRef(document, "/legacy-shape/partners/{PartnerId}/tenants", "post")
            .Should().Be("#/components/schemas/CreateLegacyShapeTenantCommand");

        var components = ComponentNames(document);
        components.Should().Contain("CreateLegacyShapeTenantCommand").And.NotContain("CreateLegacyShapeTenant");

        var body = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("CreateLegacyShapeTenantCommand");
        body.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Contain(["companyName", "address", "tier"]);
    }

    [Fact]
    public void Request_bodies_are_keyed_by_operation_name_by_default()
    {
        using var document = Render(static options => options.SchemaNaming = OpenApiSchemaNaming.Legacy);

        RequestBodySchemaRef(document, "/legacy-shape/partners/{PartnerId}/tenants", "post")
            .Should().Be("#/components/schemas/CreateLegacyShapeTenant");
    }

    private static string? RequestBodySchemaRef(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
    }

    private static List<string> AllRefs(JsonElement element)
    {
        var refs = new List<string>();
        Collect(element);

        return refs;

        void Collect(JsonElement current)
        {
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in current.EnumerateObject())
                    {
                        if (property.NameEquals("$ref") && property.Value.ValueKind == JsonValueKind.String)
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
                    foreach (var item in current.EnumerateArray())
                    {
                        Collect(item);
                    }

                    break;
            }
        }
    }

    private static JsonDocument Render(Action<OpenApiInfoOptions> configure)
    {
        var options = new OpenApiInfoOptions();
        configure(options);

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }

    private static IReadOnlyList<string> ComponentNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(static p => p.Name).ToList();
    }

    private static IReadOnlyList<string> PropertyNames(JsonDocument document, string component)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(component)
            .GetProperty("properties").EnumerateObject().Select(static p => p.Name).ToList();
    }

    private static string? ResponseSchemaRef(JsonDocument document, string path, string method, string status)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
    }
}
