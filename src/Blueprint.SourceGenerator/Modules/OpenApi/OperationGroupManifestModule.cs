using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.OpenApi;

/// <summary>
/// Contributes <c>openApi.group</c> and <c>openApi.groupSource</c> to the per-operation debug manifest,
/// reflecting the OpenAPI tag/group name used to organize operations in generated documentation.
/// </summary>
/// <remarks>
/// This module independently resolves the operation group name: first from an explicit <c>[ApiOperations("...")]</c>
/// attribute, then by falling back to the last segment of the operation's containing namespace. It does not depend
/// on <see cref="OperationGroupMetadataModule"/> metadata (which gates on HTTP host exposure).
/// </remarks>
internal sealed class OperationGroupManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    private const string ApiOperationsAttributeMetadataName = "Blueprint.Model.Attributes.ApiOperationsAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(OperationGroupManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(OperationGroupManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        var explicitGroup = TryGetExplicitGroupName(operationType);
        if (!string.IsNullOrWhiteSpace(explicitGroup))
        {
            context.AddEntry("openApi.group", new JValue(explicitGroup));
            context.AddEntry("openApi.groupSource", new JValue("explicit"));
            return;
        }

        var namespaceGroup = GetNamespaceDerivedGroupName(operationType);
        if (!string.IsNullOrWhiteSpace(namespaceGroup))
        {
            context.AddEntry("openApi.group", new JValue(namespaceGroup));
            context.AddEntry("openApi.groupSource", new JValue("namespace"));
        }
    }

    private static string? TryGetExplicitGroupName(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Where(static attr => attr.AttributeClass?.ToDisplayString() == ApiOperationsAttributeMetadataName)
            .Select(static attr => attr.ConstructorArguments.Length == 1 ? attr.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));
    }

    private static string? GetNamespaceDerivedGroupName(INamedTypeSymbol operationType)
    {
        var ns = operationType.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrWhiteSpace(ns))
        {
            return null;
        }

        var segments = ns!.Split('.')
            .Select(static s => s.Trim())
            .Where(static s => s.Length > 0)
            .ToArray();

        return segments.Length > 0 ? segments[^1] : null;
    }
}
