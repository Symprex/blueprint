using System.Collections.Immutable;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders the covered schema projection into deterministic OpenAPI-style component contracts so later document assembly
/// can reuse schema facts without coupling schema rendering to response grouping or endpoint emission.
/// </summary>
/// <param name="SchemaProjection">The previously prepared schema projection snapshot for the app.</param>
/// <param name="Components">The rendered reusable schema components in deterministic covered body-type order.</param>
public sealed record OpenApiSchemaComponentRenderingContext(
    OpenApiSchemaProjectionContext SchemaProjection,
    ImmutableArray<OpenApiSchemaComponentModel> Components)
{
    /// <summary>
    /// Creates reusable schema components from the supplied schema projection snapshot.
    /// This seam intentionally stays schema-only: it renders covered schemas in isolation and leaves document assembly for
    /// a later slice.
    /// </summary>
    /// <param name="schemaProjection">The schema projection snapshot to render.</param>
    /// <returns>The rendered schema-component context.</returns>
    public static OpenApiSchemaComponentRenderingContext Create(OpenApiSchemaProjectionContext schemaProjection)
    {
        ArgumentNullException.ThrowIfNull(schemaProjection);

        var components = schemaProjection.Schemas
            .Select(static schema => CreateComponent(schema))
            .ToImmutableArray();

        return new OpenApiSchemaComponentRenderingContext(schemaProjection, components);
    }

    private static OpenApiSchemaComponentModel CreateComponent(OpenApiSchemaModel schema)
    {
        var requiredPropertyNames = schema.Properties
            .Where(static property => property.IsRequired)
            .Select(static property => property.Name)
            .ToImmutableArray();
        var properties = schema.Properties
            .Select(static property => CreateProperty(property))
            .ToImmutableArray();

        return new OpenApiSchemaComponentModel(
            schema.BodyTypeName,
            MapType(schema.Kind),
            requiredPropertyNames,
            properties,
            schema.Description,
            schema.Title);
    }

    private static OpenApiSchemaComponentPropertyModel CreateProperty(OpenApiSchemaPropertyModel property)
    {
        return new OpenApiSchemaComponentPropertyModel(
            property.Name,
            MapType(property.Kind),
            property.IsExtensionData,
            property.IsNullable,
            property.Format,
            property.EnumValues,
            property.Description,
            property.MinLength,
            property.MaxLength,
            property.Pattern,
            property.Minimum,
            property.Maximum,
            property.IsMinimumExclusive,
            property.IsMaximumExclusive,
            property.ItemsKind.HasValue ? MapType(property.ItemsKind.Value) : null,
            property.ItemsFormat,
            property.AdditionalPropertiesKind.HasValue ? MapType(property.AdditionalPropertiesKind.Value) : null,
            property.AdditionalPropertiesFormat,
            property.ItemsSchemaName,
            property.AdditionalPropertiesSchemaName,
            property.IsDeprecated,
            property.DeprecationReason,
            property.DefaultValue,
            property.ExampleValue,
            property.IsReadOnly,
            property.IsWriteOnly);
    }

    private static string? MapType(OpenApiSchemaKind kind)
    {
        return kind switch
        {
            OpenApiSchemaKind.None => null,
            OpenApiSchemaKind.Object => "object",
            OpenApiSchemaKind.Array => "array",
            OpenApiSchemaKind.String => "string",
            OpenApiSchemaKind.Integer => "integer",
            OpenApiSchemaKind.Number => "number",
            OpenApiSchemaKind.Boolean => "boolean",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported OpenAPI schema kind."),
        };
    }
}

/// <summary>
/// Captures one rendered reusable OpenAPI schema component for a covered body type.
/// </summary>
/// <param name="BodyTypeName">The stable covered body type name that identifies the component.</param>
/// <param name="Type">The OpenAPI-style schema type when the current projection can determine one.</param>
/// <param name="RequiredPropertyNames">The ordered required property names for object schemas.</param>
/// <param name="Properties">The ordered rendered property contracts for object schemas.</param>
/// <param name="Description">The schema-level description from <see cref="System.ComponentModel.DescriptionAttribute"/> when specified.</param>
/// <param name="Title">The schema-level title from <see cref="System.ComponentModel.DisplayNameAttribute"/> when specified.</param>
/// <param name="Links">
/// The vendor <c>x-links</c> extension entries for this schema when the body type is an <c>ILinkableResource</c>
/// with registered non-self HATEOAS links. Empty when the resource has no non-self links or is not linkable.
/// </param>
public sealed record OpenApiSchemaComponentModel(
    string BodyTypeName,
    string? Type,
    ImmutableArray<string> RequiredPropertyNames,
    ImmutableArray<OpenApiSchemaComponentPropertyModel> Properties,
    string? Description = null,
    string? Title = null,
    ImmutableArray<OpenApiSchemaLinkExtension> Links = default);

/// <summary>
/// Represents one entry in the <c>x-links</c> vendor extension array for an OpenAPI schema component.
/// Each entry captures a registered HATEOAS link relationship (excluding "self") so downstream tooling
/// (e.g. TypeScript client generators) can create typed resource methods from the OpenAPI spec.
/// </summary>
/// <param name="Rel">The relationship name (e.g. "details", "delete", "activate").</param>
/// <param name="OperationId">The <c>operationId</c> of the linked operation (its class name).</param>
/// <param name="Method">The HTTP method of the linked operation (e.g. "GET", "POST", "DELETE").</param>
public sealed record OpenApiSchemaLinkExtension(string Rel, string OperationId, string Method);

/// <summary>
/// Captures one rendered property within a reusable OpenAPI schema component.
/// </summary>
/// <param name="Name">The JSON-visible property name.</param>
/// <param name="Type">The OpenAPI-style schema type when the current projection can determine one.</param>
/// <param name="IsExtensionData">Whether the property acts as serializer extension data.</param>
/// <param name="IsNullable">Whether this property is nullable (NRT-annotated reference type or <c>Nullable&lt;T&gt;</c>).</param>
/// <param name="Format">The OpenAPI format hint (e.g. <c>int32</c>, <c>date-time</c>, <c>uuid</c>) when the CLR type has a standard mapping.</param>
/// <param name="EnumValues">The enum member names when the property is an enum type, or empty when not an enum.</param>
/// <param name="Description">The property description from <c>[Description]</c> attribute when specified.</param>
/// <param name="MinLength">The inclusive minimum string/collection length when validation attributes expose one.</param>
/// <param name="MaxLength">The inclusive maximum string/collection length when validation attributes expose one.</param>
/// <param name="Pattern">The regular-expression pattern when validation attributes expose one.</param>
/// <param name="Minimum">The numeric lower bound when validation attributes expose one.</param>
/// <param name="Maximum">The numeric upper bound when validation attributes expose one.</param>
/// <param name="ExclusiveMinimum">Whether the numeric lower bound is exclusive.</param>
/// <param name="ExclusiveMaximum">Whether the numeric upper bound is exclusive.</param>
/// <param name="ItemsType">The OpenAPI type of array element items (e.g. <c>string</c>, <c>integer</c>) when the property is an array.</param>
/// <param name="ItemsFormat">The OpenAPI format of array element items (e.g. <c>int32</c>, <c>uuid</c>) when the items have a standard format.</param>
/// <param name="AdditionalPropertiesType">The OpenAPI type of dictionary values (e.g. <c>string</c>) when the property is a dictionary/map.</param>
/// <param name="AdditionalPropertiesFormat">The OpenAPI format of dictionary values when the values have a standard format.</param>
/// <param name="ItemsSchemaRef">The schema component name for complex array element types, enabling <c>$ref</c> rendering instead of inline <c>type</c>.</param>
/// <param name="AdditionalPropertiesSchemaRef">The schema component name for complex dictionary value types, enabling <c>$ref</c> rendering instead of inline <c>type</c>.</param>
/// <param name="IsDeprecated">Whether the property is deprecated (mapped from <see cref="ObsoleteAttribute"/>).</param>
/// <param name="DeprecationReason">The deprecation reason from <see cref="ObsoleteAttribute.Message"/>, or null.</param>
/// <param name="DefaultValue">The default value from <see cref="System.ComponentModel.DefaultValueAttribute"/>, or null.</param>
/// <param name="ExampleValue">A representative example value from <see cref="Blueprint.Model.Attributes.OpenApiExampleAttribute"/>, or null.</param>
/// <param name="IsReadOnly">Whether this property is read-only (no public setter or <c>[ReadOnly(true)]</c>).</param>
/// <param name="IsWriteOnly">Whether this property is write-only (<c>[OpenApiWriteOnly]</c>, e.g. a password field).</param>
public sealed record OpenApiSchemaComponentPropertyModel(
    string Name,
    string? Type,
    bool IsExtensionData,
    bool IsNullable,
    string? Format = null,
    ImmutableArray<string> EnumValues = default,
    string? Description = null,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    bool ExclusiveMinimum = false,
    bool ExclusiveMaximum = false,
    string? ItemsType = null,
    string? ItemsFormat = null,
    string? AdditionalPropertiesType = null,
    string? AdditionalPropertiesFormat = null,
    string? ItemsSchemaRef = null,
    string? AdditionalPropertiesSchemaRef = null,
    bool IsDeprecated = false,
    string? DeprecationReason = null,
    object? DefaultValue = null,
    object? ExampleValue = null,
    bool IsReadOnly = false,
    bool IsWriteOnly = false);
