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
    /// <summary>Gets the human-readable description for this parameter, sourced from <c>[Description]</c> attribute or null.</summary>
    public string? Description { get; init; }

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

    /// <summary>Gets the enum string values when this parameter's CLR type is an enum, or empty when not an enum.</summary>
    public ImmutableArray<string> EnumValues { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Gets whether this parameter is nullable (reference type with <c>?</c> or <c>Nullable&lt;T&gt;</c>).</summary>
    public bool IsNullable { get; init; }

    /// <summary>Gets whether this parameter is deprecated (mapped from <see cref="System.ObsoleteAttribute"/>).</summary>
    public bool IsDeprecated { get; init; }

    /// <summary>Gets the deprecation reason extracted from the <see cref="System.ObsoleteAttribute.Message"/>, or null when unset.</summary>
    public string? DeprecationReason { get; init; }

    /// <summary>
    /// Gets whether this parameter uses <c>allowReserved: true</c>, which is required for catch-all route
    /// parameters (<c>{*param}</c> or <c>{**param}</c>) that may contain forward slashes or other reserved characters.
    /// </summary>
    public bool AllowReserved { get; init; }

    /// <summary>Gets a representative example value from <see cref="Blueprint.Model.Attributes.OpenApiExampleAttribute"/>, or null when unset.</summary>
    public object? ExampleValue { get; init; }

    /// <summary>Gets the OpenAPI type of array element items (e.g. <c>string</c>, <c>integer</c>) when the parameter is a collection.</summary>
    public string? ItemsType { get; init; }

    /// <summary>Gets the OpenAPI format of array element items when the items have a standard format mapping.</summary>
    public string? ItemsFormat { get; init; }

    /// <summary>
    /// Gets the enum member names when the parameter is an array whose items are an enum, or empty when its items are
    /// not an enum. Rendered inline on the items sub-schema, as legacy's NJsonSchema still carried the member names for
    /// a collection query parameter's items, however oddly it nested them (a path-local <c>definitions</c> block).
    /// </summary>
    public ImmutableArray<string> ItemsEnumValues { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets the name of the component of the enum the items are, or <see langword="null"/> for none: under
    /// <see cref="OpenApiSchemaNaming.Legacy"/>, an enum array parameter's items reference the enum's component, as legacy's
    /// referenced its definition of it, rather than repeat <see cref="ItemsEnumValues"/> inline.
    /// </summary>
    public string? ItemsEnumSchemaRef { get; init; }

    /// <summary>
    /// Gets the description of the enum the parameter is, which its schema carries, or <see langword="null"/> for none: under
    /// <see cref="OpenApiSchemaNaming.Legacy"/>, as legacy's schema for an enum parameter carried the enum's own description.
    /// </summary>
    public string? EnumDescription { get; init; }

    /// <summary>Gets the description of the enum <see cref="ItemsEnumSchemaRef"/> names, which its component carries, or null.</summary>
    public string? ItemsEnumDescription { get; init; }

    /// <summary>
    /// Gets the title of the parameter's schema before the schema name formatter is applied, or <see langword="null"/> for
    /// none: under <see cref="OpenApiSchemaNaming.Legacy"/>, the legacy name of the parameter's type, as legacy titled it.
    /// </summary>
    public string? SchemaTitle { get; init; }
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
    ImmutableArray<OpenApiRequestBodyPropertyModel> Properties)
{
    /// <summary>
    /// Gets the cross-property conditional requirements declared by the body-owned properties. These belong to the
    /// containing body schema rather than to any one property, and render as <c>allOf</c>/<c>if</c>/<c>then</c>.
    /// Empty when no body property carries a conditional validator.
    /// </summary>
    public ImmutableArray<OpenApiConditionalRequirement> Conditionals { get; init; } = [];

    /// <summary>
    /// Gets the name of the schema component the body is rendered as and referenced by, or <see langword="null"/> to key
    /// it by the operation's name.
    /// </summary>
    public string? SchemaName { get; init; }

    /// <summary>
    /// Gets the description of the body's schema component, or <see langword="null"/> for none.
    /// </summary>
    public string? Description { get; init; }
}

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
    /// <summary>Gets the human-readable description for this body property, sourced from <c>[Description]</c> attribute or null.</summary>
    public string? Description { get; init; }

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

    /// <summary>Gets the enum string values when this property's CLR type is an enum, or empty when not an enum.</summary>
    public ImmutableArray<string> EnumValues { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Gets the component schema name for enum-typed properties, enabling <c>$ref</c> rendering via <c>oneOf</c>.</summary>
    public string? EnumSchemaRef { get; init; }

    /// <summary>Gets whether this property is nullable (reference type with <c>?</c> or <c>Nullable&lt;T&gt;</c>).</summary>
    public bool IsNullable { get; init; }

    /// <summary>Gets the OpenAPI type of array element items (e.g. <c>string</c>, <c>integer</c>) when the property is a collection.</summary>
    public string? ItemsType { get; init; }

    /// <summary>Gets the OpenAPI format of array element items when the items have a standard format mapping.</summary>
    public string? ItemsFormat { get; init; }

    /// <summary>Gets the OpenAPI type of dictionary values (e.g. <c>string</c>) when the property is a dictionary/map.</summary>
    public string? AdditionalPropertiesType { get; init; }

    /// <summary>Gets the OpenAPI format of dictionary values when the values have a standard format mapping.</summary>
    public string? AdditionalPropertiesFormat { get; init; }

    /// <summary>Gets whether this property is deprecated (mapped from <see cref="System.ObsoleteAttribute"/>).</summary>
    public bool IsDeprecated { get; init; }

    /// <summary>Gets the deprecation reason extracted from the <see cref="System.ObsoleteAttribute.Message"/>, or null when unset.</summary>
    public string? DeprecationReason { get; init; }

    /// <summary>Gets the default value for this property, sourced from <c>[DefaultValue]</c> attribute, or null when unset.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Gets a representative example value from <see cref="Blueprint.Model.Attributes.OpenApiExampleAttribute"/>, or null when unset.</summary>
    public object? ExampleValue { get; init; }

    /// <summary>Gets the component schema name for complex array element items, rendered as a <c>$ref</c> instead of an inline type.</summary>
    public string? ItemsSchemaRef { get; init; }

    /// <summary>Gets the component schema name for complex dictionary values, rendered as a <c>$ref</c> instead of an inline type.</summary>
    public string? AdditionalPropertiesSchemaRef { get; init; }

    /// <summary>Gets whether this property is a dictionary of any values, rendered as <c>additionalProperties: {}</c>.</summary>
    public bool AllowsAdditionalProperties { get; init; }

    /// <summary>Gets whether this property is a dictionary whose values may be null, rendered with <c>"nullable": true</c> in its <c>additionalProperties</c>.</summary>
    public bool AdditionalPropertiesAreNullable { get; init; }

    /// <summary>Gets the description of the enum type <see cref="EnumSchemaRef"/> names, which its component carries, or null.</summary>
    public string? EnumDescription { get; init; }

    /// <summary>Gets whether this property is write-only (annotated with <see cref="Blueprint.Model.Attributes.OpenApiWriteOnlyAttribute"/>).</summary>
    public bool IsWriteOnly { get; init; }

    /// <summary>Gets the component schema name of the object this property holds, rendered as a <c>$ref</c> instead of an inline <c>object</c>.</summary>
    public string? SchemaRef { get; init; }
}
