using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
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

        ValidateCommandQueryConflict(context, typeSymbol);

        var featureData = CreatePlanData(typeSymbol);
        var hostNames = GetOrderedHostNames(context.GetMetadata<HostExposureRegistration>());

        // AddMetadata drives DescriptorPlan equality so cache invalidation fires when kind/namespace/typeName change,
        // even though the runtime feature-data type itself lives in a different target framework.
        context.AddMetadata(featureData);
        context.AddRenderStep(w =>
            w.AppendLine($"this.SetFeatureData(new global::Blueprint.Model.OperationDiscoveryFeatureData({RenderStringLiteral(featureData.Kind)}, {RenderStringLiteral(featureData.Namespace)}, {RenderStringLiteral(featureData.TypeName)}));"));

        // Operations marked with [UnexposedOperation] or exposed only to the tasks host are not publicly visible.
        if (HasUnexposedOperationAttribute(typeSymbol) || IsTasksOnlyExposure(hostNames))
        {
            context.AddRenderStep(static w => w.AppendLine("this.IsExposed = false;"));
        }

        if (hostNames.Length == 0)
        {
            return;
        }

        if (hostNames.Length == 1 && string.Equals(hostNames[0], "http", StringComparison.Ordinal))
        {
            context.AddRenderStep(static w =>
                w.AppendLine("this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.HttpHostExposure);"));
        }
        else
        {
            var renderedHostNames = string.Join(", ", hostNames.Select(RenderStringLiteral));
            context.AddRenderStep(w =>
                w.AppendLine($"this.SetFeatureData(new global::Blueprint.Model.OperationHostExposureFeatureData(global::System.Collections.Immutable.ImmutableArray.Create({renderedHostNames})));"));
        }
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

    private static bool HasUnexposedOperationAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(static attr =>
            attr.AttributeClass?.Name is "UnexposedOperationAttribute" or "UnexposedOperation"
            && attr.AttributeClass.ContainingNamespace.ToDisplayString() == "Blueprint");
    }

    private static bool IsTasksOnlyExposure(string[] hostNames)
    {
        return hostNames.Length > 0
               && hostNames.All(static h => string.Equals(h, "tasks", StringComparison.Ordinal));
    }

    private static string RenderStringLiteral(string value)
    {
        return CSharpSymbolDisplay.FormatLiteral(value, quote: true);
    }

    /// <summary>
    /// Emits BP115 when an operation implements both <c>ICommand</c> and <c>IQuery</c>,
    /// which violates command-query separation.
    /// </summary>
    private static void ValidateCommandQueryConflict(DescriptorContributorContext context, INamedTypeSymbol typeSymbol)
    {
        var isCommand = false;
        var isQuery = false;

        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.ContainingNamespace.ToDisplayString() != "Blueprint.Model")
            {
                continue;
            }

            if (iface.Name == "ICommand")
            {
                isCommand = true;
            }
            else if (iface.Name == "IQuery")
            {
                isQuery = true;
            }
        }

        if (isCommand && isQuery)
        {
            var location = typeSymbol.Locations.FirstOrDefault();
            context.AddDiagnostic(
                new DiagnosticInfo(
                    DiagnosticDescriptors.CommandAndQueryConflict,
                    location,
                    [typeSymbol.Name]));
        }
    }

    /// <summary>
    /// Captures the stable discovery facts that other descriptor contributors can reuse without re-deriving operation
    /// identity from Roslyn symbols independently.
    /// </summary>
    /// <param name="Kind">The approved operation kind.</param>
    /// <param name="Namespace">The CLR namespace of the operation type.</param>
    /// <param name="TypeName">The simple operation type name.</param>
    internal sealed record DiscoveryPlanData(string Kind, string Namespace, string TypeName);
}
