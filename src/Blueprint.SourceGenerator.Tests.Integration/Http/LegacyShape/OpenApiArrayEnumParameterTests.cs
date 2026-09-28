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

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }
}
