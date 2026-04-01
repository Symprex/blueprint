using System.Collections.Immutable;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Groups rendered OpenAPI-style operations into deterministic path-item contracts keyed by route template and HTTP
/// method so later document-emission slices can consume one stable path seam without rediscovering routing facts.
/// </summary>
/// <param name="OperationRendering">The previously rendered operation context that owns operation ordering and metadata.</param>
/// <param name="PathItems">The rendered OpenAPI-style path items grouped from the operation models.</param>
public sealed record OpenApiPathItemRenderingContext(
    OpenApiOperationRenderingContext OperationRendering,
    ImmutableArray<OpenApiPathItemModel> PathItems)
{
    /// <summary>
    /// Creates deterministic OpenAPI-style path items from the supplied operation-rendering context.
    /// Operations without route templates or HTTP methods are intentionally excluded because they cannot be represented
    /// as path-item entries yet.
    /// </summary>
    /// <param name="operationRendering">The operation-rendering context to group into path items.</param>
    /// <returns>The rendered path-item context.</returns>
    public static OpenApiPathItemRenderingContext Create(OpenApiOperationRenderingContext operationRendering)
    {
        ArgumentNullException.ThrowIfNull(operationRendering);

        var pathItems = operationRendering.Operations
            .SelectMany(static operation => CreateRegistrations(operation))
            .GroupBy(static registration => registration.UrlFormat, StringComparer.Ordinal)
            .OrderBy(static grouping => grouping.Key, StringComparer.Ordinal)
            .Select(static grouping => CreatePathItem(grouping.Key, grouping))
            .ToImmutableArray();

        return new OpenApiPathItemRenderingContext(operationRendering, pathItems);
    }

    private static ImmutableArray<PathItemRegistration> CreateRegistrations(OpenApiOperationModel operation)
    {
        if (operation.UrlFormats.IsEmpty || operation.HttpMethods.IsEmpty)
        {
            return [];
        }

        var urlFormats = operation.UrlFormats
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static urlFormat => urlFormat, StringComparer.Ordinal)
            .ToImmutableArray();
        var httpMethods = operation.HttpMethods
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static httpMethod => httpMethod, StringComparer.Ordinal)
            .ToImmutableArray();
        var registrations = ImmutableArray.CreateBuilder<PathItemRegistration>(urlFormats.Length * httpMethods.Length);

        foreach (var urlFormat in urlFormats)
        {
            foreach (var httpMethod in httpMethods)
            {
                registrations.Add(new PathItemRegistration(urlFormat, httpMethod, operation));
            }
        }

        return registrations.MoveToImmutable();
    }

    private static OpenApiPathItemModel CreatePathItem(
        string urlFormat,
        IGrouping<string, PathItemRegistration> registrations)
    {
        var methods = registrations
            .GroupBy(static registration => registration.HttpMethod, StringComparer.Ordinal)
            .OrderBy(static grouping => grouping.Key, StringComparer.Ordinal)
            .Select(static grouping => new OpenApiPathItemMethodModel(
                grouping.Key,
                grouping
                    .Select(static registration => registration.Operation)
                    .OrderBy(static operation => operation.OperationName, StringComparer.Ordinal)
                    .ThenBy(static operation => operation.OperationTypeName, StringComparer.Ordinal)
                    .ToImmutableArray()))
            .ToImmutableArray();

        return new OpenApiPathItemModel(urlFormat, methods);
    }

    private sealed record PathItemRegistration(string UrlFormat, string HttpMethod, OpenApiOperationModel Operation);
}

/// <summary>
/// Captures one rendered OpenAPI-style path item keyed by a deterministic route template.
/// </summary>
/// <param name="UrlFormat">The route template for this path item.</param>
/// <param name="Methods">The grouped HTTP method slots rendered for this path item.</param>
public sealed record OpenApiPathItemModel(
    string UrlFormat,
    ImmutableArray<OpenApiPathItemMethodModel> Methods);

/// <summary>
/// Captures one grouped HTTP method slot within a rendered path item.
/// </summary>
/// <param name="HttpMethod">The HTTP method key for this slot.</param>
/// <param name="Operations">The operations contributing to this path/method slot in deterministic order.</param>
public sealed record OpenApiPathItemMethodModel(
    string HttpMethod,
    ImmutableArray<OpenApiOperationModel> Operations);
