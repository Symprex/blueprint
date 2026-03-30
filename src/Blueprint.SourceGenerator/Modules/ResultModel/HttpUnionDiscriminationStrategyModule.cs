using System.Globalization;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Reports compile-time diagnostics when HTTP-visible semantic result unions share a status code without a distinct client discrimination strategy.
/// </summary>
public sealed class HttpUnionDiscriminationStrategyModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(HttpUnionDiscriminationStrategyModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HttpUnionDiscriminationStrategyModule);

    /// <inheritdoc />
    public int Order => 65;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        if (HasHttpHostExposure(context) == false)
        {
            return;
        }

        var okResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult");
        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult");
        var keyOnlyResourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.KeyOnlyResourceEventResult");
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
        if (okResultType is null || noResultType is null)
        {
            return;
        }

        var operationType = context.GetOperationTypeSymbol();
        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredContractType is null
            || OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var unionMembers) == false)
        {
            return;
        }

        var translatedMembers = new List<TranslatedResultMember>(unionMembers.Length);
        foreach (var unionMember in unionMembers)
        {
            if (OperationResultContractHelper.TryGetHttpOperationResultResponseShape(unionMember, okResultType, noResultType, keyOnlyResourceEventResultType, httpNoContentResultType, out var statusCode, out var hasResponseBody) == false)
            {
                return;
            }

            translatedMembers.Add(new TranslatedResultMember(unionMember, statusCode, hasResponseBody));
        }

        var sameStatusGroups = translatedMembers
            .GroupBy(static member => member.StatusCode)
            .Where(static group => group.Count() > 1 && group.Select(static member => member.HasResponseBody).Distinct().Count() > 1);

        foreach (var sameStatusGroup in sameStatusGroups)
        {
            var resultTypes = string.Join(", ", sameStatusGroup.Select(static member => member.ResultType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MissingHttpUnionDiscriminationStrategy,
                    GetDiagnosticLocation(context),
                    [operationType.Name, resultTypes, sameStatusGroup.Key.ToString(CultureInfo.InvariantCulture), "http"]));
        }
    }

    private static LocationInfo? GetDiagnosticLocation(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .FirstOrDefault(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal))
            ?.LocationInfo
            ?? GetOperationLocation(context.GetOperationTypeSymbol());
    }

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static LocationInfo? GetOperationLocation(INamedTypeSymbol operationType)
    {
        var location = operationType.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
        return location is null ? null : LocationInfo.CreateFrom(location);
    }

    private sealed record TranslatedResultMember(ITypeSymbol ResultType, int StatusCode, bool HasResponseBody);
}
