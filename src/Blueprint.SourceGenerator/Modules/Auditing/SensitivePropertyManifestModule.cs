using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules.Auditing;

/// <summary>
/// Contributes <c>sensitiveProperties.enabled</c> and <c>sensitiveProperties.names</c> to the per-operation
/// debug manifest, reflecting which public properties are marked with <c>[Sensitive]</c> for audit redaction.
/// </summary>
/// <remarks>
/// This module independently scans the operation type for <c>[Sensitive]</c> attributes on public properties,
/// mirroring the analysis in <see cref="SensitivePropertyMetadataModule"/> but projecting into the debug
/// manifest rather than into descriptor feature data.
/// </remarks>
internal sealed class SensitivePropertyManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    private const string SensitiveAttributeMetadataName = "Blueprint.Auditing.SensitiveAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(SensitivePropertyManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(SensitivePropertyManifestModule);

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
        var sensitiveNames = FindSensitivePublicProperties(operationType);

        if (sensitiveNames.Count == 0)
        {
            context.AddEntry("sensitiveProperties.enabled", new JValue(false));
            return;
        }

        context.AddEntry("sensitiveProperties.enabled", new JValue(true));
        context.AddEntry("sensitiveProperties.names", new JArray(sensitiveNames.Select(static n => new JValue(n))));
    }

    private static List<string> FindSensitivePublicProperties(INamedTypeSymbol operationType)
    {
        var result = new List<string>();

        foreach (var member in operationType.GetAllMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            if (property.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var hasSensitive = property.GetAttributes()
                .Any(static attr => attr.AttributeClass?.ToDisplayString() == SensitiveAttributeMetadataName);

            if (hasSensitive)
            {
                result.Add(property.Name);
            }
        }

        return result;
    }
}
