using System.Collections.Frozen;
using System.Collections.Immutable;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Captures the app-owned serializer declaration plus the HTTP-exposed operation metadata that a later OpenAPI document
/// builder will consume, so schema/document work can stay on stable runtime contracts instead of rediscovering
/// descriptor or serializer facts ad hoc.
/// </summary>
/// <param name="App">The constructed Blueprint app that owns the serializer declaration and registered operations.</param>
/// <param name="DeclaredSerializerContextType">The app-owned serializer context type when the app declares one.</param>
/// <param name="DeclaredSerializerContextBodyTypeNames">The distinct body payload CLR type names declared by the app-owned serializer context.</param>
/// <param name="Operations">
/// The HTTP-exposed operations prepared for OpenAPI document building: those on the HTTP host whose
/// <see cref="ApiOperationDescriptor.IsExposed"/> is <see langword="true"/>.
/// </param>
public sealed record OpenApiDocumentBuilderContext(
    BlueprintApp App,
    Type? DeclaredSerializerContextType,
    ImmutableArray<string> DeclaredSerializerContextBodyTypeNames,
    ImmutableArray<OpenApiDocumentOperationContext> Operations)
{
    private const string HttpHostName = "http";

    /// <summary>
    /// Creates a deterministic OpenAPI document-builder snapshot from the app-owned serializer declaration and the
    /// descriptor metadata already published for HTTP-exposed operations.
    /// </summary>
    /// <param name="app">The constructed Blueprint app to inspect.</param>
    /// <returns>The document-builder context for the app.</returns>
    public static OpenApiDocumentBuilderContext Create(BlueprintApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var declaredBodyTypeNames = app.AppOwnedSerializerContextBodyTypeNames;
        var declaredBodyTypeNameSet = declaredBodyTypeNames.ToFrozenSet(StringComparer.Ordinal);

        // An operation that is not exposed — [UnexposedOperation], or a Rebus message handled through the pipeline — is
        // still routed, but is not part of the published API, so it is left out of the document as legacy Blueprint left
        // it out.
        var operations = app.GetOperationsForHost(HttpHostName)
            .Where(static operation => operation.IsExposed)
            .OrderBy(static operation => operation.Name, StringComparer.Ordinal)
            .Select(operation => CreateOperationContext(operation, declaredBodyTypeNameSet))
            .ToImmutableArray();

        return new OpenApiDocumentBuilderContext(
            app,
            app.AppOwnedSerializerContextType,
            declaredBodyTypeNames,
            operations);
    }

    private static OpenApiDocumentOperationContext CreateOperationContext(
        ApiOperationDescriptor operation,
        FrozenSet<string> declaredBodyTypeNameSet)
    {
        var responses = operation.TryGetFeatureData<OperationHttpResponseMetadataFeatureData>(out var responseMetadata)
            ? responseMetadata.Responses
            : ImmutableArray<OperationHttpResponseMetadata>.Empty;
        var requiredBodyTypeNames = operation.TryGetFeatureData<OperationSerializerContextCoverageFeatureData>(out var serializerCoverage)
            ? serializerCoverage.BodyTypeNames
            : ImmutableArray<string>.Empty;
        var missingBodyTypeNames = requiredBodyTypeNames
            .Where(requiredBodyTypeName => !declaredBodyTypeNameSet.Contains(requiredBodyTypeName))
            .ToImmutableArray();

        return new OpenApiDocumentOperationContext(
            operation,
            responses,
            requiredBodyTypeNames,
            missingBodyTypeNames,
            missingBodyTypeNames.IsEmpty);
    }
}

/// <summary>
/// Captures the per-operation metadata that a later OpenAPI document builder needs to decide whether the app-owned
/// serializer declaration covers the descriptor-published HTTP response contract.
/// </summary>
/// <param name="Operation">The HTTP-exposed operation descriptor.</param>
/// <param name="Responses">The HTTP response metadata already published for the operation.</param>
/// <param name="RequiredSerializerContextBodyTypeNames">The distinct serializer-context body payload CLR type names the operation requires.</param>
/// <param name="MissingSerializerContextBodyTypeNames">The required body payload CLR type names not covered by the app-owned serializer declaration.</param>
/// <param name="IsSerializerContextCoverageSatisfied">Whether the app-owned serializer declaration covers all required body payload CLR type names for the operation.</param>
public sealed record OpenApiDocumentOperationContext(
    ApiOperationDescriptor Operation,
    ImmutableArray<OperationHttpResponseMetadata> Responses,
    ImmutableArray<string> RequiredSerializerContextBodyTypeNames,
    ImmutableArray<string> MissingSerializerContextBodyTypeNames,
    bool IsSerializerContextCoverageSatisfied);
