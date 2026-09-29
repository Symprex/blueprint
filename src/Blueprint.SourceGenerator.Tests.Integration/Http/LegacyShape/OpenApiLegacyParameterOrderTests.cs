using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming an operation's parameters are listed in declaration order, the derived type's
/// properties before its bases' with route, query and header parameters interleaved as declared, and that Default naming
/// keeps its stable order by location and then name.
/// </summary>
/// <remarks>
/// Legacy added parameters in the order of the operation's properties (<c>GetProperties</c>: the derived type's, then each
/// base's), and Signature365's generated client builds each method's positional arguments in document order, so a
/// base-class paged query's <c>Page</c> and <c>PageSize</c> come after the derived type's own filters
/// (<c>GetLicenseKeysQuery</c>: ShowActive, ShowRedeemed, ShowRevoked, FilterBy, Page, PageSize, ...). Default keeps the
/// order it has always had, which a consumer's positional generated client may already depend on.
/// </remarks>
public sealed class OpenApiLegacyParameterOrderTests
{
    [Fact]
    public void Under_legacy_naming_parameters_are_in_declaration_order_derived_type_first()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        ParameterNames(document).Should().Equal(
            ["zeta", "id", "alpha", "page", "pagesize"],
            "the derived type's properties come first in the order declared, then its base's");
    }

    [Fact]
    public void Under_default_naming_parameters_stay_ordered_by_location_then_name()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        ParameterNames(document).Should().Equal(
            ["id", "alpha", "page", "pagesize", "zeta"],
            "Default has always listed path parameters first, then the rest by name");
    }

    private static string[] ParameterNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/legacy-shape/parameter-order/{Id}").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Select(static parameter => parameter.GetProperty("name").GetString()!.ToLowerInvariant())
            .ToArray();
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>Declares the paging properties a derived query inherits.</summary>
public abstract class ParameterOrderPagedQuery
{
    public int? Page { get; set; }

    public int? PageSize { get; set; }
}

/// <summary>Declares a query, a route and another query property, before the inherited paging ones.</summary>
[AllowAnonymous]
[RootLink("/legacy-shape/parameter-order/{Id}")]
public partial class GetParameterOrderQuery : ParameterOrderPagedQuery, IQuery<string>
{
    public string? Zeta { get; set; }

    public string Id { get; set; } = string.Empty;

    public string? Alpha { get; set; }

    public static string Handle(GetParameterOrderQuery query)
    {
        return query.Id;
    }
}
