using System.Collections.Immutable;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
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
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        if (HasHttpHostExposure(context) == false)
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
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
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
        if (flattenedResults.Length == 0)
        {
            return [];
        }

        var responses = ImmutableArray.CreateBuilder<HttpResponseMetadataRegistration>(flattenedResults.Length);
        foreach (var flattenedResult in flattenedResults)
        {
            if (OperationResultContractHelper.TryGetHttpOperationResultResponseShape(flattenedResult, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, validationFailedResultType, noResultType, keyOnlyResourceEventResultType, httpNoContentResultType, out var statusCode, out var hasResponseBody) == false)
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
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
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
                    httpNoContentResultType,
                    unhandledExceptionResultType,
                    out var statusCode,
                    out var hasResponseBody,
                    out var bodyTypeNames))
            {
                responses.Add(CreateResponse(statusCode, hasResponseBody, failureOutcome.ResultTypeName, bodyTypeNames));
            }
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
        INamedTypeSymbol? httpNoContentResultType,
        INamedTypeSymbol? unhandledExceptionResultType,
        out int statusCode,
        out bool hasResponseBody,
        out ImmutableArray<string> bodyTypeNames)
    {
        var resultType = context.FindTypeByMetadataName(resultTypeName);
        if (resultType is not null
            && OperationResultContractHelper.TryGetHttpOperationResultResponseShape(resultType, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, validationFailedResultType, noResultType, keyOnlyResourceEventResultType, httpNoContentResultType, out statusCode, out hasResponseBody))
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

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
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

            if (current.Name is "OkResult" or "AcceptedResult" or "ConflictResult" or "NotFoundResult" or "CreatedResult")
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
        var renderedResponses = responses
            .Select(static response =>
                $"new global::Blueprint.Model.OperationHttpResponseMetadata({response.StatusCode}, {(response.HasResponseBody ? "true" : "false")}, {RenderStringArray(response.ResultTypeNames)}, {RenderStringArray(response.BodyTypeNames)})")
            .ToArray();

        if (renderedResponses.Length == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationHttpResponseMetadataFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("
            + string.Join(", ", renderedResponses)
            + ")));");
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
