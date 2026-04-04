using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects a stable operation-group name for HTTP-exposed operations so later OpenAPI rendering can publish tags
/// without rediscovering namespace/grouping conventions at runtime.
/// </summary>
public sealed class OperationGroupMetadataModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private const string ApiOperationsAttributeMetadataName = "Blueprint.Model.Attributes.ApiOperationsAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(OperationGroupMetadataModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(OperationGroupMetadataModule);

    /// <inheritdoc />
    public int Order => 67;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(OperationDiscoveryManifestModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        if (HasHttpHostExposure(context) == false)
        {
            return;
        }

        var groupName = TryResolveGroupName(context);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        context.AddMetadata(new OperationGroupMetadataRegistration(groupName!));
        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<OperationGroupMetadataRegistration>()));
    }

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static string? TryResolveGroupName(DescriptorContributorContext context)
    {
        var explicitGroupName = TryResolveExplicitGroupName(context.GetOperationTypeSymbol());
        if (string.IsNullOrWhiteSpace(explicitGroupName) == false)
        {
            return explicitGroupName;
        }

        var discoveryMetadata = context.GetMetadata<OperationDiscoveryManifestModule.DiscoveryPlanData>().FirstOrDefault();
        if (discoveryMetadata is null || string.IsNullOrWhiteSpace(discoveryMetadata.Namespace))
        {
            return null;
        }

        var namespaceSegments = discoveryMetadata.Namespace
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToArray();

        return namespaceSegments.Length == 0 ? null : namespaceSegments[^1];
    }

    private static string? TryResolveExplicitGroupName(INamedTypeSymbol operationType)
    {
        return operationType.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.ToDisplayString() == ApiOperationsAttributeMetadataName)
            .Select(static attribute => attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(static groupName => string.IsNullOrWhiteSpace(groupName) == false);
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<OperationGroupMetadataRegistration> registrations)
    {
        var groupName = registrations
            .Select(static registration => registration.GroupName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name));

        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationGroupMetadataFeatureData("
            + $"{CSharpSymbolDisplay.FormatLiteral(groupName, quote: true)}));");
    }
}
