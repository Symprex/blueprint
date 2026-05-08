using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Pre-built feature data instances for values that are identical across most or all operation descriptors.
/// Generator modules emit references to these shared instances instead of allocating fresh <see cref="ImmutableArray"/>
/// and string instances per descriptor, reducing code size and runtime memory for large API surfaces.
/// </summary>
public static class WellKnownFeatureData
{
    /// <summary>
    /// The default failure outcomes produced by the standard exception translator policy. Every operation
    /// using the default policy shares this exact triple: legacy validation, data-annotations validation,
    /// and unhandled exception.
    /// </summary>
    public static readonly OperationFailureOutcomesFeatureData DefaultFailureOutcomes = new(
        ImmutableArray.Create(
            new OperationFailureOutcome(
                "legacy-validation-exception",
                "Blueprint.Validation.ValidationException",
                "Blueprint.Runtime.Exec.ValidationFailedResult"),
            new OperationFailureOutcome(
                "data-annotations-validation-exception",
                "System.ComponentModel.DataAnnotations.ValidationException",
                "Blueprint.Runtime.Exec.ValidationFailedResult"),
            new OperationFailureOutcome(
                "default-unhandled-exception",
                "System.Exception",
                "Blueprint.Runtime.Exec.UnhandledExceptionResult")));

    /// <summary>
    /// Host exposure metadata for the single-host HTTP case, shared by the vast majority of operations.
    /// </summary>
    public static readonly OperationHostExposureFeatureData HttpHostExposure = new(
        ImmutableArray.Create("http"));

    /// <summary>HTTP GET method metadata.</summary>
    public static readonly OperationHttpMethodMetadataFeatureData HttpGet = new(
        ImmutableArray.Create("GET"));

    /// <summary>HTTP POST method metadata.</summary>
    public static readonly OperationHttpMethodMetadataFeatureData HttpPost = new(
        ImmutableArray.Create("POST"));

    /// <summary>HTTP PUT method metadata.</summary>
    public static readonly OperationHttpMethodMetadataFeatureData HttpPut = new(
        ImmutableArray.Create("PUT"));

    /// <summary>HTTP DELETE method metadata.</summary>
    public static readonly OperationHttpMethodMetadataFeatureData HttpDelete = new(
        ImmutableArray.Create("DELETE"));

    /// <summary>HTTP PATCH method metadata.</summary>
    public static readonly OperationHttpMethodMetadataFeatureData HttpPatch = new(
        ImmutableArray.Create("PATCH"));

    /// <summary>
    /// Authentication feature data for operations that require an authenticated caller.
    /// </summary>
    public static readonly OperationAuthenticationFeatureData RequiresAuthentication = new(
        AuthenticationRequirement.Authenticated);

    /// <summary>
    /// The common serializer context coverage base for failure body types that most HTTP-exposed
    /// operations share: <c>ValidationProblemDetails</c> and <c>ProblemDetails</c>.
    /// </summary>
    public static readonly ImmutableArray<string> CommonFailureBodyTypeNames = ImmutableArray.Create(
        "Microsoft.AspNetCore.Mvc.ProblemDetails",
        "Microsoft.AspNetCore.Mvc.ValidationProblemDetails");

    /// <summary>
    /// The common HTTP response metadata base shared by most HTTP-exposed operations: 422 (validation)
    /// and 500 (unhandled exception) with their standard body type names.
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> CommonFailureResponses = ImmutableArray.Create(
        new OperationHttpResponseMetadata(
            422,
            true,
            ImmutableArray.Create<string>("Blueprint.Runtime.Exec.ValidationFailedResult"),
            ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ValidationProblemDetails")),
        new OperationHttpResponseMetadata(
            500,
            true,
            ImmutableArray.Create<string>("Blueprint.Runtime.Exec.UnhandledExceptionResult"),
            ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ProblemDetails")));

    /// <summary>
    /// The pre-built response metadata for operations whose only HTTP responses are the two standard
    /// failure shapes (422 validation, 500 unhandled exception). Shared by all void commands and
    /// operations that do not declare an explicit <c>OperationResult</c> contract.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData DefaultFailureResponsesFeatureData =
        new(CommonFailureResponses);

    /// <summary>
    /// The pre-built serializer context coverage for operations whose only response body types are the
    /// two standard problem details shapes. Shared by all operations that have no additional response
    /// body beyond the standard failure envelopes.
    /// </summary>
    public static readonly OperationSerializerContextCoverageFeatureData DefaultSerializerContextCoverageFeatureData =
        new(CommonFailureBodyTypeNames);

    /// <summary>
    /// The HTTP response metadata entry for a 400 binding-failure response with RFC 7807 ProblemDetails body.
    /// Shared by all operations that project this status via <c>BindingFailureResult</c>.
    /// </summary>
    public static readonly OperationHttpResponseMetadata BindingFailureResponse = new(
        400,
        true,
        ImmutableArray.Create<string>("Blueprint.Runtime.Exec.BindingFailureResult"),
        ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ProblemDetails"));

    /// <summary>
    /// The HTTP response metadata entry for a 401 unauthenticated response with RFC 7807 ProblemDetails body.
    /// Shared by all operations that project this status via <c>UnauthenticatedResult</c>.
    /// </summary>
    public static readonly OperationHttpResponseMetadata UnauthenticatedResponse = new(
        401,
        true,
        ImmutableArray.Create<string>("Blueprint.Runtime.Exec.UnauthenticatedResult"),
        ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ProblemDetails"));

    /// <summary>
    /// The HTTP response metadata entry for a 415 unsupported-media-type response with RFC 7807 ProblemDetails body.
    /// Shared by all operations that project this status via <c>UnsupportedMediaTypeResult</c>.
    /// </summary>
    public static readonly OperationHttpResponseMetadata UnsupportedMediaTypeResponse = new(
        415,
        true,
        ImmutableArray.Create<string>("Blueprint.Runtime.Exec.UnsupportedMediaTypeResult"),
        ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ProblemDetails"));

    /// <summary>
    /// The HTTP response metadata entry for a 403 forbidden response with RFC 7807 ProblemDetails body.
    /// Shared by all operations that project this status via <c>ForbiddenResult</c>.
    /// </summary>
    public static readonly OperationHttpResponseMetadata ForbiddenResponse = new(
        403,
        true,
        ImmutableArray.Create<string>("Blueprint.Runtime.Exec.ForbiddenResult"),
        ImmutableArray.Create<string>("Microsoft.AspNetCore.Mvc.ProblemDetails"));

    /// <summary>
    /// The common HTTP failure responses for operations with body binding but no authentication:
    /// 400 (binding), 415 (unsupported media type), 422 (validation), 500 (unhandled exception).
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> BodyBindingFailureResponses =
        ImmutableArray.Create(BindingFailureResponse, UnsupportedMediaTypeResponse)
            .AddRange(CommonFailureResponses);

    /// <summary>
    /// The common HTTP failure responses for authenticated operations with no body binding:
    /// 401 (unauthenticated), 422 (validation), 500 (unhandled exception).
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> AuthenticatedFailureResponses =
        ImmutableArray.Create(UnauthenticatedResponse)
            .AddRange(CommonFailureResponses);

    /// <summary>
    /// The common HTTP failure responses for authenticated operations with body binding:
    /// 400 (binding), 401 (unauthenticated), 415 (unsupported media type), 422 (validation), 500 (unhandled exception).
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> AuthenticatedBodyBindingFailureResponses =
        ImmutableArray.Create(BindingFailureResponse, UnauthenticatedResponse, UnsupportedMediaTypeResponse)
            .AddRange(CommonFailureResponses);

    /// <summary>
    /// The common HTTP failure responses for authorized operations (with resource permissions) and no body binding:
    /// 401 (unauthenticated), 403 (forbidden), 422 (validation), 500 (unhandled exception).
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> AuthorizedFailureResponses =
        ImmutableArray.Create(UnauthenticatedResponse, ForbiddenResponse)
            .AddRange(CommonFailureResponses);

    /// <summary>
    /// The common HTTP failure responses for authorized operations with body binding:
    /// 400 (binding), 401 (unauthenticated), 403 (forbidden), 415 (unsupported media type), 422 (validation), 500 (unhandled exception).
    /// </summary>
    public static readonly ImmutableArray<OperationHttpResponseMetadata> AuthorizedBodyBindingFailureResponses =
        ImmutableArray.Create(BindingFailureResponse, UnauthenticatedResponse, ForbiddenResponse, UnsupportedMediaTypeResponse)
            .AddRange(CommonFailureResponses);

    /// <summary>
    /// Pre-built response metadata for operations with body binding but no authentication and no declared
    /// result contract (failures-only): 400, 415, 422, 500.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData BodyBindingFailureResponsesFeatureData =
        new(BodyBindingFailureResponses);

    /// <summary>
    /// Pre-built response metadata for authenticated operations with no body binding and no declared
    /// result contract (failures-only): 401, 422, 500.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData AuthenticatedFailureResponsesFeatureData =
        new(AuthenticatedFailureResponses);

    /// <summary>
    /// Pre-built response metadata for authenticated operations with body binding and no declared
    /// result contract (failures-only): 400, 401, 415, 422, 500.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData AuthenticatedBodyBindingFailureResponsesFeatureData =
        new(AuthenticatedBodyBindingFailureResponses);

    /// <summary>
    /// Pre-built response metadata for authorized operations (with resource permissions) with no body
    /// binding and no declared result contract (failures-only): 401, 403, 422, 500.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData AuthorizedFailureResponsesFeatureData =
        new(AuthorizedFailureResponses);

    /// <summary>
    /// Pre-built response metadata for authorized operations with body binding and no declared
    /// result contract (failures-only): 400, 401, 403, 415, 422, 500.
    /// </summary>
    public static readonly OperationHttpResponseMetadataFeatureData AuthorizedBodyBindingFailureResponsesFeatureData =
        new(AuthorizedBodyBindingFailureResponses);
}
