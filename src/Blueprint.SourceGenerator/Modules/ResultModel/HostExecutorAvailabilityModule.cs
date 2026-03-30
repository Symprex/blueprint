using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Reports compile-time diagnostics when declared semantic result contracts have no enabled-host executor coverage.
/// </summary>
public sealed class HostExecutorAvailabilityModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(HostExecutorAvailabilityModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(HostExecutorAvailabilityModule);

    /// <inheritdoc />
    public int Order => 55;

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

        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult");
        var okResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.OkResult");
        var acceptedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.AcceptedResult");
        var conflictResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.ConflictResult");
        var notFoundResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NotFoundResult");
        var createdResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.CreatedResult");
        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult");
        var keyOnlyResourceEventResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.KeyOnlyResourceEventResult");
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
        if (operationResultType is null || okResultType is null || noResultType is null)
        {
            return;
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(context.GetOperationTypeSymbol());
        if (declaredContractType is null)
        {
            return;
        }

        var flattenedResults = OperationResultContractHelper.GetFlattenedSemanticResultTypes(declaredContractType, operationResultType);
        if (flattenedResults.Length == 0)
        {
            return;
        }

        var diagnosticLocation = GetDiagnosticLocation(context);
        var operationName = context.GetOperationTypeSymbol().Name;
        foreach (var flattenedResult in flattenedResults)
        {
            if (OperationResultContractHelper.IsSupportedByHttpOperationResultExecutor(flattenedResult, okResultType, acceptedResultType, conflictResultType, notFoundResultType, createdResultType, noResultType, keyOnlyResourceEventResultType, httpNoContentResultType))
            {
                continue;
            }

            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MissingHostExecutorForDeclaredResultContract,
                    diagnosticLocation,
                    [operationName, flattenedResult.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), "http"]));
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
}
