using System.Collections.Immutable;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Projects covered app-owned serializer metadata into a stable schema-ready runtime model so later OpenAPI document work
/// can consume JSON shape facts without rediscovering body coverage or widening into document rendering yet.
/// </summary>
/// <param name="Document">The document-builder snapshot that owns the projected schema set.</param>
/// <param name="Schemas">The distinct covered body-type schemas available for later OpenAPI work.</param>
public sealed record OpenApiSchemaProjectionContext(
    OpenApiDocumentBuilderContext Document,
    ImmutableArray<OpenApiSchemaModel> Schemas)
{
    /// <summary>
    /// Creates a schema projection snapshot for the covered body types in the supplied OpenAPI document-builder context.
    /// Missing/uncovered body types are intentionally excluded so this seam only projects serializer-backed shapes.
    /// </summary>
    /// <param name="document">The previously prepared OpenAPI document-builder context.</param>
    /// <returns>The schema projection snapshot.</returns>
    public static OpenApiSchemaProjectionContext Create(OpenApiDocumentBuilderContext document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.App.AppOwnedSerializerContext is not JsonSerializerContext serializerContext)
        {
            return new OpenApiSchemaProjectionContext(document, ImmutableArray<OpenApiSchemaModel>.Empty);
        }

        var coveredTypesByName = document.App.AppOwnedSerializerContextBodyTypes
            .Select(static bodyType => new KeyValuePair<string, Type>(FormatTypeName(bodyType), bodyType))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        var coveredBodyTypeNames = document.Operations
            .Where(static operation => operation.IsSerializerContextCoverageSatisfied)
            .SelectMany(static operation => operation.RequiredSerializerContextBodyTypeNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static bodyTypeName => bodyTypeName, StringComparer.Ordinal);

        var schemas = coveredBodyTypeNames
            .Where(coveredTypesByName.ContainsKey)
            .Select(bodyTypeName => CreateSchema(bodyTypeName, coveredTypesByName[bodyTypeName], serializerContext))
            .ToImmutableArray();

        return new OpenApiSchemaProjectionContext(document, schemas);
    }

    private static OpenApiSchemaModel CreateSchema(
        string bodyTypeName,
        Type bodyType,
        JsonSerializerContext serializerContext)
    {
        var typeInfo = serializerContext.GetTypeInfo(bodyType)
            ?? throw new InvalidOperationException($"The serializer context did not expose metadata for covered body type '{bodyTypeName}'.");
        var propertyNames = typeInfo.Kind == JsonTypeInfoKind.Object
            ? typeInfo.Properties
                .Select(static property => property.Name)
                .OrderBy(static propertyName => propertyName, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

        return new OpenApiSchemaModel(
            bodyTypeName,
            MapKind(bodyType, typeInfo.Kind),
            true,
            propertyNames);
    }

    private static OpenApiSchemaKind MapKind(Type bodyType, JsonTypeInfoKind kind)
    {
        if (bodyType == typeof(string))
        {
            return OpenApiSchemaKind.String;
        }

        return kind switch
        {
            JsonTypeInfoKind.None => OpenApiSchemaKind.None,
            JsonTypeInfoKind.Object => OpenApiSchemaKind.Object,
            JsonTypeInfoKind.Enumerable => OpenApiSchemaKind.Array,
            JsonTypeInfoKind.Dictionary => OpenApiSchemaKind.Object,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported serializer metadata kind."),
        };
    }

    private static string FormatTypeName(Type type)
    {
        return type == typeof(string)
            ? "string"
            : type.FullName ?? type.Name;
    }
}

/// <summary>
/// Captures the currently projected schema facts for one covered serializer body type.
/// </summary>
/// <param name="BodyTypeName">The stable CLR body-type name used by the surrounding OpenAPI seams.</param>
/// <param name="Kind">The broad schema shape derived from serializer metadata.</param>
/// <param name="IsCoveredBySerializerContext">Whether the serializer context explicitly covers this type.</param>
/// <param name="PropertyNames">The ordered JSON-visible property names when the serializer metadata exposes an object shape.</param>
public sealed record OpenApiSchemaModel(
    string BodyTypeName,
    OpenApiSchemaKind Kind,
    bool IsCoveredBySerializerContext,
    ImmutableArray<string> PropertyNames);

/// <summary>
/// Describes the broad schema shape currently projected from serializer metadata.
/// </summary>
public enum OpenApiSchemaKind
{
    /// <summary>
    /// The serializer metadata did not expose a richer schema shape.
    /// </summary>
    None = 0,

    /// <summary>
    /// The serializer metadata describes an object/document shape.
    /// </summary>
    Object = 1,

    /// <summary>
    /// The serializer metadata describes an array or enumerable shape.
    /// </summary>
    Array = 2,

    /// <summary>
    /// The serializer metadata describes a string scalar.
    /// </summary>
    String = 3,
}
