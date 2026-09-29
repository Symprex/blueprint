using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that an array-typed query parameter whose items are an enum still carries the enum's member names, as
/// legacy Blueprint did (Signature365 8b40ecc961 committed Subscriptions document,
/// <c>/tenants/{TenantId}/invoices</c>.get's <c>Statuses</c> parameter: an inline <c>InvoiceStatus</c> definition
/// with <c>enum</c> and <c>x-enumNames</c>, not a bare <c>{"type":"string"}</c>).
/// </summary>
public class OpenApiArrayEnumParameterTests
{
    [Fact]
    public void An_array_query_parameters_enum_items_keep_their_member_names()
    {
        using var document = Render();

        var items = document.RootElement.GetProperty("paths").GetProperty("/array-enum-parameter/invoices")
            .GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(static parameter => parameter.GetProperty("name").GetString() == "statuses")
            .GetProperty("schema").GetProperty("items");

        items.GetProperty("type").GetString().Should().Be("string");
        items.GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Paid", "Posted", "PaymentDue");
        items.GetProperty("x-enumNames").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Paid", "Posted", "PaymentDue");
    }

    [Fact]
    public void Under_legacy_naming_an_array_parameters_enum_items_reference_the_enums_component()
    {
        // The generated client types the items by the last segment of the reference, so a reference to the component it
        // has for the enum gives "Array<InvoiceStatus>" where inline items gave "Array<undefined>" (R148).
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var items = ItemsOf(document, "/array-enum-parameter/invoices", "statuses");

        items.EnumerateObject().Select(static property => property.Name).Should().Equal("$ref");
        items.GetProperty("$ref").GetString().Should().Be("#/components/schemas/ArrayEnumParameterStatus");

        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ArrayEnumParameterStatus");
        component.GetProperty("enum").EnumerateArray().Select(static value => value.GetString()).Should().Equal("Paid", "Posted", "PaymentDue");
        component.GetProperty("description").GetString().Should().Be("An invoice's status, as Signature365's Subscriptions app declares it.");
    }

    [Fact]
    public void Under_legacy_naming_an_enum_a_response_also_uses_is_referenced_by_the_one_component()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        ItemsOf(document, "/array-enum-parameter/audits", "severities").GetProperty("$ref").GetString()
            .Should().Be("#/components/schemas/ArrayEnumParameterSeverity");

        var severity = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ArrayEnumParameterAuditApiResource")
            .GetProperty("properties").EnumerateObject().Single(static property => property.Name.Equals("severity", StringComparison.OrdinalIgnoreCase)).Value;

        // Nothing else beside the reference, so legacy left it bare (R163).
        severity.GetProperty("$ref").GetString().Should().Be("#/components/schemas/ArrayEnumParameterSeverity");
    }

    private static JsonElement ItemsOf(JsonDocument document, string path, string parameterName)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path)
            .GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(parameter => string.Equals(parameter.GetProperty("name").GetString(), parameterName, StringComparison.OrdinalIgnoreCase))
            .GetProperty("schema").GetProperty("items");
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming = OpenApiSchemaNaming.Default)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}
