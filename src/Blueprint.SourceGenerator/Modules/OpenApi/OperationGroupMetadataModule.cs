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

        var (groupName, groupDescription) = TryResolveGroup(context);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        context.AddMetadata(new OperationGroupMetadataRegistration(groupName!, groupDescription));
        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<OperationGroupMetadataRegistration>()));
    }

    private static bool HasHttpHostExposure(DescriptorContributorContext context)
    {
        return context.GetMetadata<HostExposureRegistration>()
            .Any(static registration => string.Equals(registration.HostName, HostExposureRegistration.Http.HostName, StringComparison.Ordinal));
    }

    private static (string? GroupName, string? GroupDescription) TryResolveGroup(DescriptorContributorContext context)
    {
        var (explicitGroupName, explicitDescription) = TryResolveExplicitGroup(context.GetOperationTypeSymbol());
        if (string.IsNullOrWhiteSpace(explicitGroupName) == false)
        {
            return (explicitGroupName, explicitDescription);
        }

        var discoveryMetadata = context.GetMetadata<OperationDiscoveryManifestModule.DiscoveryPlanData>().FirstOrDefault();
        if (discoveryMetadata is null || string.IsNullOrWhiteSpace(discoveryMetadata.Namespace))
        {
            return (null, null);
        }

        var namespaceSegments = discoveryMetadata.Namespace
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToArray();

        return namespaceSegments.Length == 0 ? (null, null) : (namespaceSegments[^1], null);
    }

    private static (string? GroupName, string? Description) TryResolveExplicitGroup(INamedTypeSymbol operationType)
    {
        var attribute = operationType.GetAttributes()
            .FirstOrDefault(static attribute => attribute.AttributeClass?.ToDisplayString() == ApiOperationsAttributeMetadataName);

        if (attribute is null || attribute.ConstructorArguments.Length != 1)
        {
            return (null, null);
        }

        var groupName = attribute.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return (null, null);
        }

        var description = attribute.NamedArguments
            .FirstOrDefault(static kvp => kvp.Key == "Description")
            .Value.Value as string;

        return (groupName, string.IsNullOrWhiteSpace(description) ? null : description);
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<OperationGroupMetadataRegistration> registrations)
    {
        var registration = registrations
            .FirstOrDefault(static r => !string.IsNullOrWhiteSpace(r.GroupName));

        if (registration is null || string.IsNullOrWhiteSpace(registration.GroupName))
        {
            return;
        }

        var descriptionArg = registration.GroupDescription is not null
            ? $", {CSharpSymbolDisplay.FormatLiteral(registration.GroupDescription, quote: true)}"
            : string.Empty;

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationGroupMetadataFeatureData("
            + $"{CSharpSymbolDisplay.FormatLiteral(registration.GroupName, quote: true)}{descriptionArg}));");
    }
}
