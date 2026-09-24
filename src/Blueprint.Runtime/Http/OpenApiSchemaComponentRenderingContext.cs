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
        // Extension data properties ([JsonExtensionData]) represent the catch-all bucket for
        // arbitrary extra JSON members. They should not appear as named properties in the schema.
        // When present, the schema should NOT emit additionalProperties: false.
        var hasExtensionData = schema.Properties.Any(static property => property.IsExtensionData);

        var requiredPropertyNames = schema.Properties
            .Where(static property => property.IsRequired && !property.IsExtensionData)
            .Select(static property => property.Name)
            .ToImmutableArray();
        var properties = schema.Properties
            .Where(static property => !property.IsExtensionData)
            .Select(static property => CreateProperty(property))
            .ToImmutableArray();

        return new OpenApiSchemaComponentModel(
            schema.BodyTypeName,
            MapType(schema.Kind),
            requiredPropertyNames,
            properties,
            schema.Description,
            schema.Title,
            HasExtensionData: hasExtensionData,
            Conditionals: schema.Conditionals)
        {
            AdditionalPropertiesAreNullable = schema.AdditionalPropertiesAreNullable,
        };
    }

    private static OpenApiSchemaComponentPropertyModel CreateProperty(OpenApiSchemaPropertyModel property)
    {
        // The well-known $links property (IDictionary<string, Link> from ILinkableResource) should
        // reference the Link schema component rather than rendering as bare "type: object".
        var additionalPropertiesSchemaRef = property.AdditionalPropertiesSchemaName;
        if (additionalPropertiesSchemaRef is null
            && string.Equals(property.Name, "$links", StringComparison.Ordinal)
            && property.Kind == OpenApiSchemaKind.Object)
        {
            additionalPropertiesSchemaRef = "Link";
        }

        return new OpenApiSchemaComponentPropertyModel(
            property.Name,
            MapType(property.Kind),
            property.IsExtensionData,
            property.IsNullable,
            property.Format,
            property.EnumValues,
            property.EnumSchemaName,
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
            additionalPropertiesSchemaRef,
            property.AdditionalPropertiesItemsKind.HasValue ? MapType(property.AdditionalPropertiesItemsKind.Value) : null,
            property.AdditionalPropertiesItemsFormat,
            property.AdditionalPropertiesItemsSchemaName,
            property.IsDeprecated,
            property.DeprecationReason,
            property.DefaultValue,
            property.ExampleValue,
            property.IsReadOnly,
            property.IsWriteOnly,
            SchemaRef: property.SchemaName,
            AllowsAdditionalProperties: property.AllowsAdditionalProperties)
        {
            AdditionalPropertiesAreNullable = property.AdditionalPropertiesAreNullable,
        };
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
/// <param name="HasExtensionData">
/// Whether the original CLR type has a <c>[JsonExtensionData]</c> property, indicating it accepts arbitrary
/// additional JSON members. When true, the schema should NOT emit <c>additionalProperties: false</c>.
/// </param>
/// <param name="ComponentEnumValues">
/// When non-empty, this component represents an enum type rather than an object and the values are the
/// string enum members. Enum components are rendered as <c>type: string</c> with <c>enum</c> and
/// <c>x-enumNames</c> arrays, and properties that reference them use <c>$ref</c> via <c>oneOf</c>.
/// </param>
/// <param name="Conditionals">
/// The cross-property conditional requirements declared by this schema's properties, rendered as an
/// <c>allOf</c> array of <c>if</c>/<c>then</c> pairs. Empty when no property carries a conditional validator.
/// </param>
public sealed record OpenApiSchemaComponentModel(
    string BodyTypeName,
    string? Type,
    ImmutableArray<string> RequiredPropertyNames,
    ImmutableArray<OpenApiSchemaComponentPropertyModel> Properties,
    string? Description = null,
    string? Title = null,
    ImmutableArray<OpenApiSchemaLinkExtension> Links = default,
    bool HasExtensionData = false,
    ImmutableArray<string> ComponentEnumValues = default,
    ImmutableArray<OpenApiConditionalRequirement> Conditionals = default)
{
    /// <summary>
    /// Gets a value indicating whether the component's extension data members may be null, which it documents with
    /// <c>additionalProperties: {"nullable": true}</c>.
    /// </summary>
    public bool AdditionalPropertiesAreNullable { get; init; }
}

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
/// Describes the shape a conditionally-required target property must take once its condition is active.
/// The kinds mirror what <c>RequiredIfAttribute</c>/<c>RequiredItemsIfAttribute</c> and the generated
/// validation fast path enforce at runtime, so the projected schema cannot disagree with either.
/// </summary>
public enum OpenApiConditionalTargetKind
{
    /// <summary>The target is a string that must be present and not blank (matching <c>string.IsNullOrWhiteSpace</c>).</summary>
    NonBlankString,

    /// <summary>
    /// The target must be present. Presence is the whole of it: the stronger <c>{ "not": { "type": "null" } }</c> is
    /// not legal in the OpenAPI 3.0 dialect this document declares, so the enclosing <c>required</c> entry carries the
    /// constraint alone and an explicit <c>null</c> satisfies it.
    /// </summary>
    NonNull,

    /// <summary>The target must be a non-empty array whose every element is a non-blank string.</summary>
    NonBlankStringItems,

    /// <summary>
    /// The target must be present and a non-empty array. Its elements are left unconstrained, for the reason
    /// <see cref="NonNull"/> gives: element non-nullness has no legal expression in this dialect either.
    /// </summary>
    NonNullItems,
}

/// <summary>
/// Captures one cross-property conditional requirement projected onto an object schema as a JSON Schema
/// <c>if</c>/<c>then</c> pair within <c>allOf</c>, so generated client validators enforce the same
/// cross-property rules the server does instead of silently dropping them.
/// </summary>
/// <remarks>
/// The <c>if</c> always requires the dependent property to be present. A payload that omits it therefore
/// never activates the <c>then</c>, even where the server — which sees the CLR default rather than an
/// absence — would consider the requirement active. That under-enforces on the client and never
/// over-enforces, which is the safe direction: a generated validator blocking a payload the server would
/// have accepted is a worse failure than one the server still rejects.
/// </remarks>
/// <param name="DependentPropertyName">The JSON-visible name of the sibling property that controls the requirement.</param>
/// <param name="DependentValues">The dependent values that activate (or, when negated, deactivate) the requirement.
/// Enum values are normalised to their member name to match how enums render in the document.</param>
/// <param name="DependentValueNegated">Whether the requirement activates when the dependent property does
/// <em>not</em> match one of <paramref name="DependentValues"/> (<c>RequiredIfNot</c>/<c>RequiredItemsIfNot</c>).</param>
/// <param name="TargetPropertyName">The JSON-visible name of the property that becomes required.</param>
/// <param name="TargetKind">The shape the target must take once the requirement is active.</param>
public sealed record OpenApiConditionalRequirement(
    string DependentPropertyName,
    ImmutableArray<object?> DependentValues,
    bool DependentValueNegated,
    string TargetPropertyName,
    OpenApiConditionalTargetKind TargetKind);

/// <summary>
/// Captures one rendered property within a reusable OpenAPI schema component.
/// </summary>
/// <param name="Name">The JSON-visible property name.</param>
/// <param name="Type">The OpenAPI-style schema type when the current projection can determine one.</param>
/// <param name="IsExtensionData">Whether the property acts as serializer extension data.</param>
/// <param name="IsNullable">Whether this property is nullable (NRT-annotated reference type or <c>Nullable&lt;T&gt;</c>).</param>
/// <param name="Format">The OpenAPI format hint (e.g. <c>int32</c>, <c>date-time</c>, <c>uuid</c>) when the CLR type has a standard mapping.</param>
/// <param name="EnumValues">The enum member names when the property is an enum type, or empty when not an enum.</param>
/// <param name="EnumSchemaRef">The component schema name for enum-typed properties, enabling <c>$ref</c> rendering via <c>oneOf</c> instead of inline <c>enum</c> arrays.</param>
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
/// <param name="AdditionalPropertiesItemsType">The OpenAPI type of items within dictionary values when values are arrays (e.g. <c>string</c> for <c>Dictionary&lt;string, string[]&gt;</c>).</param>
/// <param name="AdditionalPropertiesItemsFormat">The OpenAPI format of items within dictionary values when the nested items have a standard format.</param>
/// <param name="AdditionalPropertiesItemsSchemaRef">The schema component name for complex items within dictionary array values, enabling nested <c>$ref</c> rendering.</param>
/// <param name="IsDeprecated">Whether the property is deprecated (mapped from <see cref="ObsoleteAttribute"/>).</param>
/// <param name="DeprecationReason">The deprecation reason from <see cref="ObsoleteAttribute.Message"/>, or null.</param>
/// <param name="DefaultValue">The default value from <see cref="System.ComponentModel.DefaultValueAttribute"/>, or null.</param>
/// <param name="ExampleValue">A representative example value from <see cref="Blueprint.Model.Attributes.OpenApiExampleAttribute"/>, or null.</param>
/// <param name="IsReadOnly">Whether this property is read-only (no public setter or <c>[ReadOnly(true)]</c>).</param>
/// <param name="IsWriteOnly">Whether this property is write-only (<c>[OpenApiWriteOnly]</c>, e.g. a password field).</param>
/// <param name="SchemaRef">The body type name of another schema component this property references directly.
/// When set, the property renders as <c>$ref: #/components/schemas/{Name}</c> instead of an inline type definition.
/// For nullable references, renders as <c>nullable: true, oneOf: [{ $ref }]</c>.</param>
/// <param name="AllowsAdditionalProperties">When true, the property emits <c>additionalProperties: {}</c> to indicate
/// the object accepts arbitrary additional members (e.g. metadata, changedValues bags on ResourceEvent envelopes).</param>
public sealed record OpenApiSchemaComponentPropertyModel(
    string Name,
    string? Type,
    bool IsExtensionData,
    bool IsNullable,
    string? Format = null,
    ImmutableArray<string> EnumValues = default,
    string? EnumSchemaRef = null,
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
    string? AdditionalPropertiesItemsType = null,
    string? AdditionalPropertiesItemsFormat = null,
    string? AdditionalPropertiesItemsSchemaRef = null,
    bool IsDeprecated = false,
    string? DeprecationReason = null,
    object? DefaultValue = null,
    object? ExampleValue = null,
    bool IsReadOnly = false,
    bool IsWriteOnly = false,
    string? SchemaRef = null,
    bool AllowsAdditionalProperties = false)
{
    /// <summary>
    /// Gets a value indicating whether the property is a dictionary whose values may be null, written with
    /// <c>"nullable": true</c> in its <c>additionalProperties</c> — beside the value's type, or beside a <c>oneOf</c> wrapping
    /// the value's <c>$ref</c>, which OpenAPI 3.0 would otherwise not let it sit beside.
    /// </summary>
    public bool AdditionalPropertiesAreNullable { get; init; }
}
