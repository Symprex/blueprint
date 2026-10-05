using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Reports compile-time diagnostics when multiple declared semantic result members collapse to the same current host translation shape.
/// </summary>
public sealed class AmbiguousHostTranslationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(AmbiguousHostTranslationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(AmbiguousHostTranslationModule);

    /// <inheritdoc />
    public int Order => 60;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (HttpHostExposureHelper.HasHttpHostExposure(operationType) == false)
        {
            return;
        }

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
        var httpAcceptedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpAcceptedResult");
        var plainTextResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.PlainTextResult");
        var redirectResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.RedirectResult");
        var fileStreamResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.FileStreamResult");
        var statusCodeResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.StatusCodeResult");
        var resourceEventType = context.FindTypeByMetadataName("Blueprint.Http.ResourceEvent");
        var httpResultType = context.FindTypeByMetadataName("Blueprint.Http.HttpResult");
        if (okResultType is null || noResultType is null)
        {
            return;
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredContractType is null
            || OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var unionMembers) == false)
        {
            return;
        }

        var translatedMembers = new List<TranslatedResultMember>(unionMembers.Length);
        foreach (var unionMember in unionMembers)
        {
            if (OperationResultContractHelper.TryGetHttpOperationResultResponseShape(unionMember, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, validationFailedResultType, noResultType, keyOnlyResourceEventResultType, resourceEventResultType, httpNoContentResultType, out var statusCode, out var hasResponseBody, plainTextResultType, redirectResultType, fileStreamResultType, statusCodeResultType, resourceEventType, httpResultType, httpAcceptedResultType) == false)
            {
                return;
            }

            if (SymbolEqualityComparer.Default.Equals(unionMember, keyOnlyResourceEventResultType))
            {
                // The host writes a key-only event as the empty response of its change type, known only at run time.
                foreach (var keyOnlyStatusCode in OperationResultContractHelper.KeyOnlyResourceEventStatusCodes)
                {
                    translatedMembers.Add(new TranslatedResultMember(unionMember, keyOnlyStatusCode, HasResponseBody: false));
                }

                continue;
            }

            translatedMembers.Add(new TranslatedResultMember(unionMember, statusCode, hasResponseBody));
        }

        var ambiguousGroups = translatedMembers
            .GroupBy(static member => (member.StatusCode, member.HasResponseBody))
            .Where(static group => group.Count() > 1);

        foreach (var ambiguousGroup in ambiguousGroups)
        {
            if (ambiguousGroup.Key.HasResponseBody
                && ResourceEventDiscriminationStrategyHelper.HasDistinctCompileTimeEventIds(
                    context,
                    ambiguousGroup.Select(static member => member.ResultType)))
            {
                continue;
            }

            var responseShape = DescribeResponseShape(ambiguousGroup.Key.StatusCode, ambiguousGroup.Key.HasResponseBody);
            var resultTypes = string.Join(", ", ambiguousGroup.Select(static member => member.ResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.AmbiguousHostTranslationForDeclaredResultContract,
                    GetDiagnosticLocation(context),
                    [operationType.Name, resultTypes, responseShape, "http"]));
        }
    }

    private static string DescribeResponseShape(int statusCode, bool hasResponseBody)
    {
        return hasResponseBody
            ? $"{statusCode} JSON body"
            : $"{statusCode} empty body";
    }

    private static LocationInfo? GetDiagnosticLocation(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .FirstOrDefault(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal))
            ?.LocationInfo
            ?? GetOperationLocation(context.GetOperationTypeSymbol());
    }

    private static LocationInfo? GetOperationLocation(INamedTypeSymbol operationType)
    {
        var location = operationType.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
        return location is null ? null : LocationInfo.CreateFrom(location);
    }

    private sealed record TranslatedResultMember(ITypeSymbol ResultType, int StatusCode, bool HasResponseBody);
}
