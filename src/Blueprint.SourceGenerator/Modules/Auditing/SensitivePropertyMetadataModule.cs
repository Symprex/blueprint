using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules.Auditing;

/// <summary>
/// Descriptor contributor that scans operation properties for <c>[Sensitive]</c> attributes and
/// emits a <c>SetFeatureData&lt;SensitivePropertyMetadata&gt;</c> call in the generated descriptor
/// constructor. This provides compile-time-resolved metadata that audit sinks and logging
/// pipelines can inspect at runtime without reflection.
/// </summary>
public sealed class SensitivePropertyMetadataModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string SensitiveAttributeName = "SensitiveAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(SensitivePropertyMetadataModule);

    /// <inheritdoc />
    public string ContributorId => nameof(SensitivePropertyMetadataModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public int Order => 960;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationSymbol = context.GetOperationTypeSymbol();
        var sensitiveProperties = FindSensitiveProperties(operationSymbol, context);

        if (sensitiveProperties.Count == 0)
        {
            return;
        }

        var propertyArray = string.Join(", ", sensitiveProperties.Select(static p => $"\"{p}\""));

        context.AddRenderStep(writer =>
        {
            writer.AppendLine(
                $"this.SetFeatureData(new global::Blueprint.Auditing.SensitivePropertyMetadata([{propertyArray}]));");
        });
    }

    private static List<string> FindSensitiveProperties(INamedTypeSymbol operationSymbol, DescriptorContributorContext context)
    {
        var result = new List<string>();

        foreach (var member in operationSymbol.GetAllMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            var hasSensitive = property.GetAttributes()
                .Any(static attr => attr.AttributeClass?.Name == SensitiveAttributeName
                    && attr.AttributeClass.ContainingNamespace is { Name: "Auditing", ContainingNamespace.Name: "Blueprint" });

            if (!hasSensitive)
            {
                continue;
            }

            if (property.DeclaredAccessibility != Accessibility.Public)
            {
                context.AddDiagnostic(
                    new DiagnosticInfo(
                        AuditingDiagnosticDescriptors.SensitiveOnNonPublicProperty,
                        property.Locations.FirstOrDefault(),
                        [operationSymbol.Name, property.Name]));

                continue;
            }

            result.Add(property.Name);
        }

        return result;
    }
}
