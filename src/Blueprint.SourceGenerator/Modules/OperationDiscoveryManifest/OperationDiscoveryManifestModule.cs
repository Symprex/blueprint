using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes the first real operations/discovery manifest data so inspection tooling can answer which operations exist
/// without rediscovering the compilation shape separately.
/// </summary>
public sealed class OperationDiscoveryManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
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
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        context.AddEntry("kind", GetOperationKind(context.GetOperationTypeSymbol()));
        context.AddEntry("namespace", context.Operation.Namespace);
        context.AddEntry("typeName", context.Operation.TypeName);
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
}
