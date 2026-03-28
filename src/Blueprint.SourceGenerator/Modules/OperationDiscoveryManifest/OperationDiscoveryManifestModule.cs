using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes the approved operation discovery facts (<c>kind</c>, <c>namespace</c>, <c>typeName</c>) to both the
/// per-operation debug manifest and each generated operation descriptor as typed
/// <c>Blueprint.Model.OperationDiscoveryFeatureData</c> feature data, reusing the same kind-classification logic for both.
/// </summary>
public sealed class OperationDiscoveryManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(OperationDiscoveryManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(OperationDiscoveryManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var typeSymbol = context.GetOperationTypeSymbol();
        var featureData = CreatePlanData(typeSymbol);

        // AddMetadata drives DescriptorPlan equality so cache invalidation fires when kind/namespace/typeName change,
        // even though the runtime feature-data type itself lives in a different target framework.
        context.AddMetadata(featureData);
        context.AddRenderStep(w =>
            w.AppendLine($"this.SetFeatureData(new global::Blueprint.Model.OperationDiscoveryFeatureData(\"{featureData.Kind}\", \"{featureData.Namespace}\", \"{featureData.TypeName}\"));"));
    }

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var featureData = CreatePlanData(context.GetOperationTypeSymbol());
        context.AddEntry("kind", featureData.Kind);
        context.AddEntry("namespace", featureData.Namespace);
        context.AddEntry("typeName", featureData.TypeName);
    }

    private static DiscoveryPlanData CreatePlanData(INamedTypeSymbol typeSymbol)
    {
        return new(
            GetOperationKind(typeSymbol),
            typeSymbol.ContainingNamespace.ToDisplayString(),
            typeSymbol.Name);
    }

    private static string GetOperationKind(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.AllInterfaces.Any(static candidate =>
                candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
                && candidate.Name == "ICommand"))
        {
            return "command";
        }

        if (typeSymbol.AllInterfaces.Any(static candidate =>
                candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
                && candidate.Name == "IQuery"))
        {
            return "query";
        }

        return "operation";
    }

    private sealed record DiscoveryPlanData(string Kind, string Namespace, string TypeName);
}
