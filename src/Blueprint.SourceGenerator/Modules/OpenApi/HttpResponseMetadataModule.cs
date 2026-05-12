using System.Collections.Immutable;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules.Authentication;
using Blueprint.SourceGenerator.Modules.Authorization;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects HTTP-visible response metadata from descriptor outcomes so later OpenAPI/documentation slices can reuse the
/// same compile-time contract instead of rediscovering status behavior from runtime executors.
/// </summary>
public sealed class HttpResponseMetadataModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string ValidationProblemDetailsTypeName = "Microsoft.AspNetCore.Mvc.ValidationProblemDetails";
    private const string ProblemDetailsTypeName = "Microsoft.AspNetCore.Mvc.ProblemDetails";

    /// <inheritdoc />
    public string ModuleId => nameof(HttpResponseMetadataModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HttpResponseMetadataModule);

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(FlattenedResultOutcomeModule),
        nameof(FailureOutcomesModule),
        nameof(AuthenticationModule),
        nameof(PropertyOwnershipModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (HttpHostExposureHelper.HasHttpHostExposure(operationType) == false)
        {
            return;
        }

        var responses = GetResponses(context);
        if (responses.Length == 0)
        {
            return;
        }

        foreach (var response in responses)
        {
            context.AddMetadata(response);
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<HttpResponseMetadataRegistration>()));
    }

    private static ImmutableArray<HttpResponseMetadataRegistration> GetResponses(DescriptorContributorContext context)
    {
        var responseCandidates = GetSuccessResponses(context)
            .AddRange(GetFailureResponses(context));
        if (responseCandidates.Length == 0)
        {
            return [];
        }

        return responseCandidates
            .GroupBy(static response => response.StatusCode)
            .OrderBy(static responseGroup => responseGroup.Key)
            .Select(static responseGroup => CreateGroupedResponse(responseGroup))
            .ToImmutableArray();
    }

    private static ImmutableArray<HttpResponseMetadataRegistration> GetSuccessResponses(DescriptorContributorContext context)
    {
        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult");
        var okResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult");
        var acceptedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.AcceptedResult");
        var conflictResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ConflictResult");
        var notFoundResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NotFoundResult");
        var createdResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.CreatedResult");
        var validationFailedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ValidationFailedResult");
        var unhandledExceptionResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.UnhandledExceptionResult");
        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult");
        var keyOnlyResourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.KeyOnlyResourceEventResult");
        var resourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ResourceEventResult");
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
        var plainTextResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.PlainTextResult");
        var redirectResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.RedirectResult");
        var fileStreamResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.FileStreamResult");
        var statusCodeResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.StatusCodeResult");
        if (operationResultType is null || okResultType is null || noResultType is null)
        {
            return [];
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(context.GetOperationTypeSymbol());
        if (declaredContractType is null)
        {
            return [];
        }

        var flattenedResults = OperationResultContractHelper.GetFlattenedSemanticResultTypes(declaredContractType, operationResultType);

        // When the declared contract type is a plain CLR payload (not a semantic result type and not a Results union),
        // treat it as an implicit OkResult<T> — a 200 response with the payload type as the body.
        if (flattenedResults.Length == 0 && !OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out _))
        {
            var bodyTypeName = GetTypeName(declaredContractType);
            return [CreateResponse(200, true, bodyTypeName, ImmutableArray.Create(bodyTypeName))];
        }

        if (flattenedResults.Length == 0)
        {
            return [];
        }

        var responses = ImmutableArray.CreateBuilder<HttpResponseMetadataRegistration>(flattenedResults.Length);
        foreach (var flattenedResult in flattenedResults)
        {
            if (OperationResultContractHelper.TryGetHttpOperationResultResponseShape(flattenedResult, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, validationFailedResultType, noResultType, keyOnlyResourceEventResultType, resourceEventResultType, httpNoContentResultType, out var statusCode, out var hasResponseBody, plainTextResultType, redirectResultType, fileStreamResultType, statusCodeResultType) == false)
            {
                continue;
            }

            responses.Add(CreateResponse(
                statusCode,
                hasResponseBody,
                GetTypeName(flattenedResult),
                GetResponseBodyTypeNames(flattenedResult, hasResponseBody, validationFailedResultType, unhandledExceptionResultType)));
        }

        return responses.ToImmutable();
    }

    private static ImmutableArray<HttpResponseMetadataRegistration> GetFailureResponses(DescriptorContributorContext context)
    {
        var okResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult");
        var acceptedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.AcceptedResult");
        var conflictResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ConflictResult");
        var notFoundResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NotFoundResult");
        var createdResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.CreatedResult");
        var validationFailedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ValidationFailedResult");
        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult");
        var keyOnlyResourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.KeyOnlyResourceEventResult");
        var resourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ResourceEventResult");
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
        var plainTextResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.PlainTextResult");
        var redirectResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.RedirectResult");
        var fileStreamResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.FileStreamResult");
        var unhandledExceptionResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.UnhandledExceptionResult");
        if (okResultType is null || noResultType is null)
        {
            return [];
        }

        var responses = ImmutableArray.CreateBuilder<HttpResponseMetadataRegistration>();

        foreach (var failureOutcome in context.GetMetadata<FailureOutcomeRegistration>())
        {
            if (TryGetFailureResponseShape(
                    context,
                    failureOutcome.ResultTypeName,
                    okResultType,
                    acceptedResultType,
                    conflictResultType,
                    notFoundResultType,
                    createdResultType,
                    validationFailedResultType,
                    noResultType,
                    keyOnlyResourceEventResultType,
                    resourceEventResultType,
                    httpNoContentResultType,
                    unhandledExceptionResultType,
                    out var statusCode,
                    out var hasResponseBody,
                    out var bodyTypeNames,
                    plainTextResultType,
                    redirectResultType,
                    fileStreamResultType))
            {
                responses.Add(CreateResponse(statusCode, hasResponseBody, failureOutcome.ResultTypeName, bodyTypeNames));
            }
        }

        // Binding-driven pipeline short-circuits: 400 (binding failure) and 415 (unsupported media type).
        // 400 applies when the operation has body or header/cookie bindings that can fail during population.
        // 415 applies only when body binding is present (content-type rejection before parsing).
        // All four short-circuit responses return RFC 7807 application/problem+json bodies.
        var problemDetailsBody = ImmutableArray.Create(ProblemDetailsTypeName);
        var binding = context.GetMetadata<BindingDescriptorRegistration>().FirstOrDefault();
        if (binding is not null)
        {
            if (binding.HasBodyBinding || binding.HasHeaderOrCookieBinding)
            {
                responses.Add(CreateResponse(400, true, "Blueprint.Runtime.Exec.BindingFailureResult", problemDetailsBody));
            }

            if (binding.HasBodyBinding)
            {
                responses.Add(CreateResponse(415, true, "Blueprint.Runtime.Exec.UnsupportedMediaTypeResult", problemDetailsBody));
            }
        }

        // Authentication-driven short-circuit: 401 when [MustBeAuthenticated] is declared.
        if (context.GetMetadata<AuthenticationDescriptorRegistration>().Any(static r => r.RequiresIdentityCheck))
        {
            responses.Add(CreateResponse(401, true, "Blueprint.Runtime.Exec.UnauthenticatedResult", problemDetailsBody));
        }

        // Authorization-driven short-circuit: 403 when [Resource] scopes are declared.
        if (context.GetMetadata<AuthorizationDescriptorRegistration>().Any(static r => r.RequiresPermissionCheck))
        {
            responses.Add(CreateResponse(403, true, "Blueprint.Runtime.Exec.ForbiddenResult", problemDetailsBody));
        }

        return responses.ToImmutable();
    }

    private static bool TryGetFailureResponseShape(
        DescriptorContributorContext context,
        string resultTypeName,
        INamedTypeSymbol okResultType,
        INamedTypeSymbol? acceptedResultType,
        INamedTypeSymbol? conflictResultType,
        INamedTypeSymbol? notFoundResultType,
        INamedTypeSymbol? createdResultType,
        INamedTypeSymbol? validationFailedResultType,
        INamedTypeSymbol noResultType,
        INamedTypeSymbol? keyOnlyResourceEventResultType,
        INamedTypeSymbol? resourceEventResultType,
        INamedTypeSymbol? httpNoContentResultType,
        INamedTypeSymbol? unhandledExceptionResultType,
        out int statusCode,
        out bool hasResponseBody,
        out ImmutableArray<string> bodyTypeNames,
        INamedTypeSymbol? plainTextResultType = null,
        INamedTypeSymbol? redirectResultType = null,
        INamedTypeSymbol? fileStreamResultType = null)
    {
        var resultType = context.FindTypeByMetadataName(resultTypeName);
        if (resultType is not null
            && OperationResultContractHelper.TryGetHttpOperationResultResponseShape(resultType, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, validationFailedResultType, noResultType, keyOnlyResourceEventResultType, resourceEventResultType, httpNoContentResultType, out statusCode, out hasResponseBody, plainTextResultType, redirectResultType, fileStreamResultType))
        {
            bodyTypeNames = GetResponseBodyTypeNames(resultType, hasResponseBody, validationFailedResultType, unhandledExceptionResultType);
            return true;
        }

        if (unhandledExceptionResultType is not null
            && SymbolEqualityComparer.Default.Equals(resultType, unhandledExceptionResultType))
        {
            statusCode = 500;
            hasResponseBody = true;
            bodyTypeNames = ImmutableArray.Create(ProblemDetailsTypeName);
            return true;
        }

        if (string.Equals(resultTypeName, "Blueprint.Runtime.Exec.UnhandledExceptionResult", StringComparison.Ordinal))
        {
            statusCode = 500;
            hasResponseBody = true;
            bodyTypeNames = ImmutableArray.Create(ProblemDetailsTypeName);
            return true;
        }

        statusCode = default;
        hasResponseBody = default;
        bodyTypeNames = default;
        return false;
    }

    private static HttpResponseMetadataRegistration CreateResponse(
        int statusCode,
        bool hasResponseBody,
        string resultTypeName,
        ImmutableArray<string> bodyTypeNames)
    {
        return new HttpResponseMetadataRegistration(
            statusCode,
            hasResponseBody,
            ImmutableArray.Create(resultTypeName),
            bodyTypeNames);
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static ImmutableArray<string> GetResponseBodyTypeNames(
        ITypeSymbol resultType,
        bool hasResponseBody,
        INamedTypeSymbol? validationFailedResultType,
        INamedTypeSymbol? unhandledExceptionResultType)
    {
        if (hasResponseBody == false)
        {
            return [];
        }

        if (IsTypeOrDerivedFromOptional(resultType, validationFailedResultType)
            || string.Equals(GetTypeName(resultType), "Blueprint.Runtime.Exec.ValidationFailedResult", StringComparison.Ordinal))
        {
            return ImmutableArray.Create(ValidationProblemDetailsTypeName);
        }

        if (IsTypeOrDerivedFromOptional(resultType, unhandledExceptionResultType)
            || string.Equals(GetTypeName(resultType), "Blueprint.Runtime.Exec.UnhandledExceptionResult", StringComparison.Ordinal))
        {
            return ImmutableArray.Create(ProblemDetailsTypeName);
        }

        var bodyType = GetTypedResultBodyType(resultType);
        return bodyType is null
            ? []
            : ImmutableArray.Create(GetTypeName(bodyType));
    }

    private static ITypeSymbol? GetTypedResultBodyType(ITypeSymbol resultType)
    {
        for (var current = resultType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType == false
                || current.TypeArguments.Length != 1
                || string.Equals(current.ContainingNamespace.ToDisplayString(), "Blueprint.Runtime.Exec", StringComparison.Ordinal) == false)
            {
                continue;
            }

            if (current.Name is "OkResult" or "AcceptedResult" or "ConflictResult" or "NotFoundResult" or "CreatedResult" or "ResourceEventResult")
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsTypeOrDerivedFromOptional(ITypeSymbol candidateType, INamedTypeSymbol? baseType)
    {
        return baseType is not null && OperationResultContractHelper.IsTypeOrDerivedFrom(candidateType, baseType);
    }

    private static HttpResponseMetadataRegistration CreateGroupedResponse(IGrouping<int, HttpResponseMetadataRegistration> responseGroup)
    {
        // Same-status mixed body shapes are already diagnosed for declared HTTP unions, and current failure families are
        // body-bearing. Conservatively keep a body when any contributing response has one until a later OpenAPI
        // discriminator slice needs a richer same-status representation.
        return new HttpResponseMetadataRegistration(
            responseGroup.Key,
            responseGroup.Any(static response => response.HasResponseBody),
            responseGroup
                .SelectMany(static response => response.ResultTypeNames)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray(),
            responseGroup
                .SelectMany(static response => response.BodyTypeNames)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<HttpResponseMetadataRegistration> responses)
    {
        var responseList = responses as IReadOnlyList<HttpResponseMetadataRegistration>
            ?? responses.ToArray();

        if (responseList.Count == 0)
        {
            return;
        }

        // Failures-only: emit the matching shared well-known instance to avoid per-descriptor allocation
        if (TryGetFailuresOnlyConstantName(responseList, out var featureDataConstantName))
        {
            codeWriter.AppendLine(
                $"this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.{featureDataConstantName});");
            return;
        }

        // Trailing pipeline failures: compose success/declared entries inline + AddRange the shared failure array
        if (TryGetTrailingFailureTailInfo(responseList, out var tailLength, out var tailConstantName))
        {
            var leadEntries = responseList
                .Take(responseList.Count - tailLength)
                .Select(static response =>
                    $"new global::Blueprint.Model.OperationHttpResponseMetadata({response.StatusCode}, {(response.HasResponseBody ? "true" : "false")}, {RenderStringArray(response.ResultTypeNames)}, {RenderStringArray(response.BodyTypeNames)})")
                .ToArray();

            codeWriter.AppendLine(
                "this.SetFeatureData(new global::Blueprint.Model.OperationHttpResponseMetadataFeatureData("
                + $"global::System.Collections.Immutable.ImmutableArray.Create({string.Join(", ", leadEntries)})"
                + $".AddRange(global::Blueprint.Model.WellKnownFeatureData.{tailConstantName})));");
            return;
        }

        // Fallback: emit all responses inline
        var renderedResponses = responseList
            .Select(static response =>
                $"new global::Blueprint.Model.OperationHttpResponseMetadata({response.StatusCode}, {(response.HasResponseBody ? "true" : "false")}, {RenderStringArray(response.ResultTypeNames)}, {RenderStringArray(response.BodyTypeNames)})")
            .ToArray();

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHttpResponseMetadataFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("
            + string.Join(", ", renderedResponses)
            + ")));");
    }

    /// <summary>
    /// Checks if the response set exactly matches one of the known failures-only patterns and returns
    /// the corresponding <c>WellKnownFeatureData</c> field name when it does.
    /// </summary>
    private static bool TryGetFailuresOnlyConstantName(IReadOnlyList<HttpResponseMetadataRegistration> responses, out string constantName)
    {
        // [422, 500] → DefaultFailureResponsesFeatureData
        if (responses.Count == 2
            && IsDefaultValidationFailureEntry(responses[0])
            && IsDefaultUnhandledExceptionEntry(responses[1]))
        {
            constantName = "DefaultFailureResponsesFeatureData";
            return true;
        }

        // [400, 415, 422, 500] → BodyBindingFailureResponsesFeatureData
        if (responses.Count == 4
            && IsBindingFailureEntry(responses[0])
            && IsUnsupportedMediaTypeEntry(responses[1])
            && IsDefaultValidationFailureEntry(responses[2])
            && IsDefaultUnhandledExceptionEntry(responses[3]))
        {
            constantName = "BodyBindingFailureResponsesFeatureData";
            return true;
        }

        // [401, 422, 500] → AuthenticatedFailureResponsesFeatureData
        if (responses.Count == 3
            && IsUnauthenticatedEntry(responses[0])
            && IsDefaultValidationFailureEntry(responses[1])
            && IsDefaultUnhandledExceptionEntry(responses[2]))
        {
            constantName = "AuthenticatedFailureResponsesFeatureData";
            return true;
        }

        // [400, 401, 415, 422, 500] → AuthenticatedBodyBindingFailureResponsesFeatureData
        if (responses.Count == 5
            && IsBindingFailureEntry(responses[0])
            && IsUnauthenticatedEntry(responses[1])
            && IsUnsupportedMediaTypeEntry(responses[2])
            && IsDefaultValidationFailureEntry(responses[3])
            && IsDefaultUnhandledExceptionEntry(responses[4]))
        {
            constantName = "AuthenticatedBodyBindingFailureResponsesFeatureData";
            return true;
        }

        // [401, 403, 422, 500] → AuthorizedFailureResponsesFeatureData
        if (responses.Count == 4
            && IsUnauthenticatedEntry(responses[0])
            && IsForbiddenEntry(responses[1])
            && IsDefaultValidationFailureEntry(responses[2])
            && IsDefaultUnhandledExceptionEntry(responses[3]))
        {
            constantName = "AuthorizedFailureResponsesFeatureData";
            return true;
        }

        // [400, 401, 403, 415, 422, 500] → AuthorizedBodyBindingFailureResponsesFeatureData
        if (responses.Count == 6
            && IsBindingFailureEntry(responses[0])
            && IsUnauthenticatedEntry(responses[1])
            && IsForbiddenEntry(responses[2])
            && IsUnsupportedMediaTypeEntry(responses[3])
            && IsDefaultValidationFailureEntry(responses[4])
            && IsDefaultUnhandledExceptionEntry(responses[5]))
        {
            constantName = "AuthorizedBodyBindingFailureResponsesFeatureData";
            return true;
        }

        constantName = string.Empty;
        return false;
    }

    /// <summary>
    /// Checks if the response set ends with one of the known pipeline-failure tail patterns and returns the
    /// tail length and the corresponding <c>WellKnownFeatureData</c> array field name when it does.
    /// </summary>
    private static bool TryGetTrailingFailureTailInfo(
        IReadOnlyList<HttpResponseMetadataRegistration> responses,
        out int tailLength,
        out string tailConstantName)
    {
        // Try longest tail first: [400, 401, 403, 415, 422, 500]
        if (responses.Count >= 7
            && IsBindingFailureEntry(responses[responses.Count - 6])
            && IsUnauthenticatedEntry(responses[responses.Count - 5])
            && IsForbiddenEntry(responses[responses.Count - 4])
            && IsUnsupportedMediaTypeEntry(responses[responses.Count - 3])
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 6;
            tailConstantName = "AuthorizedBodyBindingFailureResponses";
            return true;
        }

        // Try [400, 401, 415, 422, 500]
        if (responses.Count >= 6
            && IsBindingFailureEntry(responses[responses.Count - 5])
            && IsUnauthenticatedEntry(responses[responses.Count - 4])
            && IsUnsupportedMediaTypeEntry(responses[responses.Count - 3])
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 5;
            tailConstantName = "AuthenticatedBodyBindingFailureResponses";
            return true;
        }

        // Try [401, 403, 422, 500]
        if (responses.Count >= 5
            && IsUnauthenticatedEntry(responses[responses.Count - 4])
            && IsForbiddenEntry(responses[responses.Count - 3])
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 4;
            tailConstantName = "AuthorizedFailureResponses";
            return true;
        }

        // Try [400, 415, 422, 500]
        if (responses.Count >= 5
            && IsBindingFailureEntry(responses[responses.Count - 4])
            && IsUnsupportedMediaTypeEntry(responses[responses.Count - 3])
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 4;
            tailConstantName = "BodyBindingFailureResponses";
            return true;
        }

        // Try [401, 422, 500]
        if (responses.Count >= 4
            && IsUnauthenticatedEntry(responses[responses.Count - 3])
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 3;
            tailConstantName = "AuthenticatedFailureResponses";
            return true;
        }

        // Try shortest tail: [422, 500]
        if (responses.Count >= 3
            && IsDefaultValidationFailureEntry(responses[responses.Count - 2])
            && IsDefaultUnhandledExceptionEntry(responses[responses.Count - 1]))
        {
            tailLength = 2;
            tailConstantName = "CommonFailureResponses";
            return true;
        }

        tailLength = 0;
        tailConstantName = string.Empty;
        return false;
    }

    private static bool IsDefaultValidationFailureEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 422
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.ValidationFailedResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ValidationProblemDetailsTypeName;
    }

    private static bool IsDefaultUnhandledExceptionEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 500
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.UnhandledExceptionResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ProblemDetailsTypeName;
    }

    private static bool IsBindingFailureEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 400
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.BindingFailureResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ProblemDetailsTypeName;
    }

    private static bool IsUnauthenticatedEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 401
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.UnauthenticatedResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ProblemDetailsTypeName;
    }

    private static bool IsUnsupportedMediaTypeEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 415
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.UnsupportedMediaTypeResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ProblemDetailsTypeName;
    }

    private static bool IsForbiddenEntry(HttpResponseMetadataRegistration response)
    {
        return response.StatusCode == 403
            && response.HasResponseBody
            && response.ResultTypeNames.Length == 1
            && response.ResultTypeNames[0] == "Blueprint.Runtime.Exec.ForbiddenResult"
            && response.BodyTypeNames.Length == 1
            && response.BodyTypeNames[0] == ProblemDetailsTypeName;
    }

    private static string RenderStringLiteral(string value)
    {
        return CSharpSymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string RenderStringArray(IEnumerable<string> values)
    {
        var renderedValues = values
            .Select(RenderStringLiteral)
            .ToArray();

        return renderedValues.Length == 0
            ? "global::System.Collections.Immutable.ImmutableArray<string>.Empty"
            : $"global::System.Collections.Immutable.ImmutableArray.Create<string>({string.Join(", ", renderedValues)})";
    }
}
