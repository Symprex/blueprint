using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Reports compile-time diagnostics when a single HTTP-visible union mixes host-neutral no-result semantics with host-owned no-content/status-only result families.
/// </summary>
public sealed class NoResultShapeSeparationModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(NoResultShapeSeparationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(NoResultShapeSeparationModule);

    /// <inheritdoc />
    public int Order => 62;

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

        var noResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Exec.NoResultOperationResult");
        var httpNoContentResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpNoContentResult");
        var httpAcceptedResultType = context.FindTypeByMetadataName("Blueprint.Runtime.Http.HttpAcceptedResult");
        if (noResultType is null || (httpNoContentResultType is null && httpAcceptedResultType is null))
        {
            return;
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredContractType is null
            || OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var unionMembers) == false)
        {
            return;
        }

        var hasNoResult = unionMembers.Any(member => SymbolEqualityComparer.Default.Equals(member, noResultType));
        if (hasNoResult == false)
        {
            return;
        }

        var hasHttpStatusOnly = unionMembers.Any(member =>
            SymbolEqualityComparer.Default.Equals(member, httpNoContentResultType)
            || SymbolEqualityComparer.Default.Equals(member, httpAcceptedResultType));
        if (hasHttpStatusOnly == false)
        {
            return;
        }

        var resultTypes = string.Join(", ", unionMembers.Select(static member => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        context.AddDiagnostic(
            new DiagnosticInfo(
                DiagnosticDescriptors.ConflictingNoResultShapeCategories,
                GetDiagnosticLocation(context),
                [operationType.Name, resultTypes, "http"]));
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
}
