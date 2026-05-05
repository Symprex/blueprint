using System.Collections.Immutable;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Captures one projected OpenAPI-style parameter from the compile-time property ownership plan.
/// Parameters represent route, query, header, and cookie bindings that are documented in the OpenAPI
/// <c>parameters</c> array.
/// </summary>
/// <param name="Name">The external binding name for this parameter (e.g. camelCase query name, header name).</param>
/// <param name="In">The OpenAPI parameter location: <c>path</c>, <c>query</c>, <c>header</c>, or <c>cookie</c>.</param>
/// <param name="Required">Whether this parameter is required. Path parameters are always required.</param>
/// <param name="SchemaType">The OpenAPI schema type (e.g. <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c>).</param>
/// <param name="SchemaFormat">
/// The optional OpenAPI schema format (e.g. <c>uuid</c>, <c>date-time</c>, <c>int32</c>, <c>int64</c>).
/// Null when no format qualification is needed.
/// </param>
public sealed record OpenApiParameterModel(
    string Name,
    string In,
    bool Required,
    string SchemaType,
    string? SchemaFormat)
{
    /// <summary>Gets the minimum length constraint for string-typed parameters, or null when unconstrained.</summary>
    public int? MinLength { get; init; }

    /// <summary>Gets the maximum length constraint for string-typed parameters, or null when unconstrained.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets the regex pattern constraint for string-typed parameters, or null when unconstrained.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets the minimum (or exclusive minimum) value for numeric parameters, or null when unconstrained.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Gets the maximum (or exclusive maximum) value for numeric parameters, or null when unconstrained.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>Gets whether <see cref="Minimum"/> is exclusive (OpenAPI 3.0 <c>exclusiveMinimum: true</c>).</summary>
    public bool ExclusiveMinimum { get; init; }

    /// <summary>Gets whether <see cref="Maximum"/> is exclusive (OpenAPI 3.0 <c>exclusiveMaximum: true</c>).</summary>
    public bool ExclusiveMaximum { get; init; }
}

/// <summary>
/// Captures the projected OpenAPI-style request body for an HTTP operation. The body schema is rendered inline
/// from the body-owned properties rather than referencing a reusable component, because body-owned properties are
/// a compile-time subset of the operation type that excludes route/query/header/cookie/system/auth properties.
/// </summary>
/// <param name="Required">Whether the request body is required.</param>
/// <param name="Properties">The ordered body-owned properties projected as inline schema entries.</param>
public sealed record OpenApiRequestBodyModel(
    bool Required,
    ImmutableArray<OpenApiRequestBodyPropertyModel> Properties);

/// <summary>
/// Captures one property within an inline request body schema, using the external JSON binding name
/// and the OpenAPI schema type derived from the CLR property type at compile time.
/// </summary>
/// <param name="Name">The JSON property name (camelCase binding name from the ownership plan).</param>
/// <param name="SchemaType">The OpenAPI schema type for this property.</param>
/// <param name="SchemaFormat">The optional OpenAPI schema format for this property.</param>
public sealed record OpenApiRequestBodyPropertyModel(
    string Name,
    string SchemaType,
    string? SchemaFormat)
{
    /// <summary>Gets whether this property is required (has <c>[Required]</c>).</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets the minimum length constraint for string-typed properties, or null when unconstrained.</summary>
    public int? MinLength { get; init; }

    /// <summary>Gets the maximum length constraint for string-typed properties, or null when unconstrained.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Gets the regex pattern constraint for string-typed properties, or null when unconstrained.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets the minimum (or exclusive minimum) value for numeric properties, or null when unconstrained.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Gets the maximum (or exclusive maximum) value for numeric properties, or null when unconstrained.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>Gets whether <see cref="Minimum"/> is exclusive.</summary>
    public bool ExclusiveMinimum { get; init; }

    /// <summary>Gets whether <see cref="Maximum"/> is exclusive.</summary>
    public bool ExclusiveMaximum { get; init; }
}
