using System.Collections.Frozen;
using System.Collections.Immutable;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Projects per-response schema references by merging HTTP response metadata with the covered schema inventory already
/// prepared for the surrounding app. This stays intentionally narrower than document rendering so later OpenAPI slices
/// can consume stable response-to-schema relationships without recomputing coverage or serializer metadata.
/// </summary>
/// <param name="SchemaProjection">The previously prepared schema projection snapshot for the app.</param>
/// <param name="Operations">The HTTP-exposed operations with response metadata augmented by covered schema references.</param>
public sealed record OpenApiResponseSchemaReferenceProjectionContext(
    OpenApiSchemaProjectionContext SchemaProjection,
    ImmutableArray<OpenApiResponseSchemaReferenceOperationContext> Operations)
{
    /// <summary>
    /// Creates a per-response schema reference projection from the supplied schema projection context.
    /// Uncovered response body types remain present on the underlying response metadata but do not receive schema
    /// references in this seam.
    /// </summary>
    /// <param name="schemaProjection">The schema projection snapshot to compose with response metadata.</param>
    /// <returns>The response-schema reference projection.</returns>
    public static OpenApiResponseSchemaReferenceProjectionContext Create(OpenApiSchemaProjectionContext schemaProjection)
    {
        ArgumentNullException.ThrowIfNull(schemaProjection);

        var schemasByBodyTypeName = schemaProjection.Schemas
            .ToFrozenDictionary(static schema => schema.BodyTypeName, StringComparer.Ordinal);
        var operations = schemaProjection.Document.Operations
            .Select(operation => CreateOperationContext(operation, schemasByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseSchemaReferenceProjectionContext(schemaProjection, operations);
    }

    private static OpenApiResponseSchemaReferenceOperationContext CreateOperationContext(
        OpenApiDocumentOperationContext operation,
        FrozenDictionary<string, OpenApiSchemaModel> schemasByBodyTypeName)
    {
        var responses = operation.Responses
            .Select(response => CreateResponseContext(response, schemasByBodyTypeName))
            .ToImmutableArray();

        return new OpenApiResponseSchemaReferenceOperationContext(operation.Operation, responses);
    }

    private static OpenApiResponseSchemaReferenceResponseContext CreateResponseContext(
        OperationHttpResponseMetadata response,
        FrozenDictionary<string, OpenApiSchemaModel> schemasByBodyTypeName)
    {
        var schemaReferences = response.BodyTypeNames
            .Distinct(StringComparer.Ordinal)
            .Select(bodyTypeName => TryCreateSchemaReference(bodyTypeName, schemasByBodyTypeName))
            .OfType<OpenApiResponseSchemaReference>()
            .ToImmutableArray();

        return new OpenApiResponseSchemaReferenceResponseContext(response, schemaReferences);
    }

    private static OpenApiResponseSchemaReference? TryCreateSchemaReference(
        string bodyTypeName,
        FrozenDictionary<string, OpenApiSchemaModel> schemasByBodyTypeName)
    {
        // Array body types (e.g. "Namespace.Type[]") resolve to the element type's schema with array wrapping.
        // Check this first so array types render as inline "type: array, items: { $ref: ... }" rather than
        // referencing an empty array schema component.
        if (bodyTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementTypeName = bodyTypeName[..^2];
            if (schemasByBodyTypeName.TryGetValue(elementTypeName, out var elementSchema))
            {
                return new OpenApiResponseSchemaReference(bodyTypeName, elementSchema, IsArrayWrapper: true);
            }
        }

        if (schemasByBodyTypeName.TryGetValue(bodyTypeName, out var schema))
        {
            return new OpenApiResponseSchemaReference(bodyTypeName, schema);
        }

        return null;
    }
}

/// <summary>
/// Captures the per-operation response metadata plus the covered schema references derived for each response family.
/// </summary>
/// <param name="Operation">The HTTP-exposed operation descriptor.</param>
/// <param name="Responses">The response metadata entries augmented by covered schema references.</param>
public sealed record OpenApiResponseSchemaReferenceOperationContext(
    ApiOperationDescriptor Operation,
    ImmutableArray<OpenApiResponseSchemaReferenceResponseContext> Responses);

/// <summary>
/// Captures one HTTP response family together with the covered schema references for its body payloads.
/// </summary>
/// <param name="Response">The original HTTP response metadata entry.</param>
/// <param name="SchemaReferences">The covered schema references resolved for the response body payloads.</param>
public sealed record OpenApiResponseSchemaReferenceResponseContext(
    OperationHttpResponseMetadata Response,
    ImmutableArray<OpenApiResponseSchemaReference> SchemaReferences);

/// <summary>
/// Links one response body payload name to the covered schema model that was projected for it.
/// When the body type is an array (e.g. <c>Namespace.Type[]</c>), the reference wraps the element type's schema
/// and signals array-wrapping so the rendering layer emits <c>type: array, items: { $ref: ... }</c>.
/// </summary>
/// <param name="BodyTypeName">The stable CLR body-type name from the response metadata.</param>
/// <param name="Schema">The covered schema model resolved for the body payload (element type for arrays).</param>
/// <param name="IsArrayWrapper">Whether this reference represents an array wrapping the element schema.</param>
public sealed record OpenApiResponseSchemaReference(
    string BodyTypeName,
    OpenApiSchemaModel Schema,
    bool IsArrayWrapper = false);
