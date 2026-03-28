using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Records compile-time host exposure for operations that opt into Blueprint's task host surface through the
/// host-specific <c>Blueprint.Tasks.IBackgroundTask</c> marker, without pulling in broader background-task runtime
/// behavior before that workstream is ready.
/// </summary>
public sealed class TasksHostModule : IBlueprintSourceGeneratorModule, IDescriptorContributor, IEnabledHostContributor
{
    private const string BackgroundTaskNamespace = "Blueprint.Tasks";
    private const string BackgroundTaskInterfaceName = "IBackgroundTask";

    /// <inheritdoc />
    public string ModuleId => nameof(TasksHostModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(TasksHostModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> HostNames { get; } = ["tasks"];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();

        if (!IsBackgroundTask(operationType))
        {
            return;
        }

        var locationInfo = FindBackgroundTaskLocation(operationType);
        context.AddMetadata(locationInfo is null ? HostExposureRegistration.Tasks : HostExposureRegistration.TasksAt(locationInfo));
    }

    private static bool IsBackgroundTask(INamedTypeSymbol operationType)
    {
        return operationType.AllInterfaces.Any(static candidate =>
            candidate.Name == BackgroundTaskInterfaceName
            && candidate.ContainingNamespace.ToDisplayString() == BackgroundTaskNamespace);
    }

    private static LocationInfo? FindBackgroundTaskLocation(INamedTypeSymbol operationType)
    {
        foreach (var syntaxReference in operationType.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration
                || typeDeclaration.BaseList is null)
            {
                continue;
            }

            foreach (var baseType in typeDeclaration.BaseList.Types)
            {
                var baseTypeText = baseType.Type.ToString();

                if (baseTypeText == BackgroundTaskInterfaceName
                    || baseTypeText.EndsWith("." + BackgroundTaskInterfaceName, StringComparison.Ordinal))
                {
                    return LocationInfo.CreateFrom(baseType.Type);
                }
            }
        }

        return operationType.Locations.Length > 0 ? LocationInfo.CreateFrom(operationType.Locations[0]) : null;
    }
}
