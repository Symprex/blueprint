using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes the approved operation discovery facts (<c>kind</c>, <c>namespace</c>, <c>typeName</c>) plus ordered
/// host-exposure metadata when available to both the per-operation debug manifest and each generated operation
/// descriptor, reusing the same discovery logic across inspection and runtime-facing surfaces.
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
    // This module projects already-collected discovery metadata into descriptors and manifests, so it should run after
    // descriptor contributors that add host exposure or other discovery facts.
    public int Order => 100;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var typeSymbol = context.GetOperationTypeSymbol();
        var featureData = CreatePlanData(typeSymbol);
        var hostNames = GetOrderedHostNames(context.GetMetadata<HostExposureRegistration>());

        // AddMetadata drives DescriptorPlan equality so cache invalidation fires when kind/namespace/typeName change,
        // even though the runtime feature-data type itself lives in a different target framework.
        context.AddMetadata(featureData);
        context.AddRenderStep(w =>
            w.AppendLine($"this.SetFeatureData(new global::Blueprint.Model.OperationDiscoveryFeatureData({RenderStringLiteral(featureData.Kind)}, {RenderStringLiteral(featureData.Namespace)}, {RenderStringLiteral(featureData.TypeName)}));"));

        if (hostNames.Length == 0)
        {
            return;
        }

        var renderedHostNames = string.Join(", ", hostNames.Select(RenderStringLiteral));
        context.AddRenderStep(w =>
            w.AppendLine($"this.SetFeatureData(new global::Blueprint.Model.OperationHostExposureFeatureData(global::System.Collections.Immutable.ImmutableArray.Create({renderedHostNames})));"));
    }

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var featureData = CreatePlanData(context.GetOperationTypeSymbol());
        var hostNames = GetOrderedHostNames(context.GetMetadata<HostExposureRegistration>());
        context.AddEntry("kind", featureData.Kind);
        context.AddEntry("namespace", featureData.Namespace);
        context.AddEntry("typeName", featureData.TypeName);

        if (hostNames.Length > 0)
        {
            context.AddEntry("hostExposure", new JArray(hostNames));
        }
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

    private static string[] GetOrderedHostNames(IEnumerable<HostExposureRegistration> hostExposureRegistrations)
    {
        return hostExposureRegistrations
            .Select(static hostExposure => hostExposure.HostName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static hostName => hostName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RenderStringLiteral(string value)
    {
        return CSharpSymbolDisplay.FormatLiteral(value, quote: true);
    }

    private sealed record DiscoveryPlanData(string Kind, string Namespace, string TypeName);
}
