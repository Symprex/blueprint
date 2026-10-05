using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that a query parameter can be named by its CLR property, as legacy Blueprint named it (<c>Page</c>), rather
/// than by the camel-case name the framework's typed client sends (<c>page</c>).
/// </summary>
/// <remarks>
/// Signature365's TypeScript generator uses a parameter's name as the generated method's argument name and as the query
/// key it sends, so a changed name changes every generated call. The host binds a query parameter whatever its case.
/// </remarks>
public class OpenApiParameterNamingTests
{
    [Fact]
    public void Query_parameters_are_named_by_their_binding_by_default()
    {
        using var document = Render(static _ => { });

        ParameterNames(document, "/legacy-shape/tenants", "get").Should().BeEquivalentTo(["page:query", "pageSize:query"]);
    }

    [Fact]
    public void Query_parameters_can_be_named_by_their_property_as_legacy_named_them()
    {
        using var document = Render(static options => options.QueryParameterNaming = OpenApiParameterNaming.PropertyName);

        ParameterNames(document, "/legacy-shape/tenants", "get").Should().BeEquivalentTo(["Page:query", "PageSize:query"]);
    }

    [Fact]
    public void Route_parameters_keep_the_name_their_placeholder_has_either_way()
    {
        using var document = Render(static options => options.QueryParameterNaming = OpenApiParameterNaming.PropertyName);

        ParameterNames(document, "/legacy-shape/partners/{PartnerId}/tenants", "post").Should().BeEquivalentTo(["PartnerId:path"]);
    }

    private static IEnumerable<string> ParameterNames(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("parameters")
            .EnumerateArray()
            .Select(static parameter => $"{parameter.GetProperty("name").GetString()}:{parameter.GetProperty("in").GetString()}")
            .ToList();
    }

    private static JsonDocument Render(Action<OpenApiInfoOptions> configure)
    {
        var options = new OpenApiInfoOptions();
        configure(options);

        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), options);

        return JsonDocument.Parse(rendered.Json);
    }
}
