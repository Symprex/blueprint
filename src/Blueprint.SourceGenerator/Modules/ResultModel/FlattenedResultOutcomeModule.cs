using System.Collections.Immutable;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects declared semantic result contracts into additive descriptor feature data without freezing host translation behavior.
/// </summary>
public sealed class FlattenedResultOutcomeModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(FlattenedResultOutcomeModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(FlattenedResultOutcomeModule);

    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult");
        if (operationResultType is null)
        {
            return;
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(context.GetOperationTypeSymbol());

        if (declaredContractType is null)
        {
            return;
        }

        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var declaredUnionMembers) == false
            && OperationResultContractHelper.IsOperationResultType(declaredContractType, operationResultType)
            && OperationResultContractHelper.IsPublicSemanticResultType(declaredContractType, operationResultType) == false)
        {
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MisalignedFlattenedDescriptorOutcomes,
                    GetOperationLocation(context.GetOperationTypeSymbol()),
                    [context.GetOperationTypeSymbol().Name, declaredContractType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return;
        }

        if (declaredUnionMembers.IsDefaultOrEmpty == false
            && TryValidateFlattenedOutcomeMembers(context, declaredUnionMembers, operationResultType) == false)
        {
            return;
        }

        var flattenedOutcomes = GetFlattenedOutcomes(declaredContractType, operationResultType);
        if (flattenedOutcomes.Length == 0)
        {
            return;
        }

        foreach (var outcome in flattenedOutcomes)
        {
            context.AddMetadata(outcome);
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<FlattenedResultOutcomeRegistration>()));
    }

    private static ImmutableArray<FlattenedResultOutcomeRegistration> GetFlattenedOutcomes(
        ITypeSymbol declaredContractType,
        INamedTypeSymbol operationResultType)
    {
        return OperationResultContractHelper.GetFlattenedSemanticResultTypes(declaredContractType, operationResultType)
            .Select(static member => CreateRegistration(member))
            .ToImmutableArray();
    }

    private static FlattenedResultOutcomeRegistration CreateRegistration(ITypeSymbol resultType)
    {
        return new(resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool TryValidateFlattenedOutcomeMembers(
        DescriptorContributorContext context,
        ImmutableArray<ITypeSymbol> declaredUnionMembers,
        INamedTypeSymbol operationResultType)
    {
        var isValid = true;
        var operationName = context.GetOperationTypeSymbol().Name;
        var diagnosticLocation = GetOperationLocation(context.GetOperationTypeSymbol());

        foreach (var declaredUnionMember in declaredUnionMembers)
        {
            if (OperationResultContractHelper.IsSupportedResultsUnionMember(declaredUnionMember, operationResultType))
            {
                continue;
            }

            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.MisalignedFlattenedDescriptorOutcomes,
                    diagnosticLocation,
                    [operationName, declaredUnionMember.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            isValid = false;
        }

        return isValid;
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<FlattenedResultOutcomeRegistration> registrations)
    {
        var renderedRegistrations = registrations
            .Select(static registration => $"new global::Blueprint.Model.OperationResultOutcome(typeof({registration.ResultTypeName}))")
            .ToArray();

        if (renderedRegistrations.Length == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationResultOutcomesFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("
            + string.Join(", ", renderedRegistrations)
            + ")));");
    }

    private static LocationInfo? GetOperationLocation(INamedTypeSymbol operationType)
    {
        var location = operationType.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
        return location is null ? null : LocationInfo.CreateFrom(location);
    }

    private sealed record FlattenedResultOutcomeRegistration(string ResultTypeName);
}
