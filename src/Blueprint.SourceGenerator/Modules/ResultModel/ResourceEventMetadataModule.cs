using System.Collections.Immutable;
using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects compile-time resource-event discriminator metadata from body-bearing semantic result contracts so later
/// same-status union and OpenAPI slices can reason from declared event IDs instead of runtime constructor arguments.
/// </summary>
public sealed class ResourceEventMetadataModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ResourceEventMetadataModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResourceEventMetadataModule);

    /// <inheritdoc />
    public int Order => 56;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } =
    [
        nameof(FlattenedResultOutcomeModule),
    ];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var operationType = context.GetOperationTypeSymbol();
        if (HttpHostExposureHelper.HasHttpHostExposure(operationType) == false)
        {
            return;
        }

        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult");
        if (operationResultType is null)
        {
            return;
        }

        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);
        if (declaredContractType is null)
        {
            return;
        }

        var registrations = OperationResultContractHelper.GetFlattenedSemanticResultTypes(declaredContractType, operationResultType)
            .Select(static resultType => resultType as INamedTypeSymbol)
            .Where(static resultType => resultType is not null)
            .Select(static resultType => TryCreateRegistration(resultType!))
            .Where(static registration => registration is not null)
            .Cast<ResourceEventMetadataRegistration>()
            .ToImmutableArray();
        if (registrations.Length == 0)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            context.AddMetadata(registration);
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<ResourceEventMetadataRegistration>()));
    }

    private static ResourceEventMetadataRegistration? TryCreateRegistration(INamedTypeSymbol resultType)
    {
        var bodyType = TryGetResourceEventBodyType(resultType);
        if (bodyType is null)
        {
            return null;
        }

        var eventId = bodyType.GetAttributes()
            .Where(static attribute => attribute.AttributeClass is { Name: "ResourceEventAttribute" } ac && ac.IsInNamespace("Blueprint", "Model", "Attributes"))
            .Select(static attribute => attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(static value => string.IsNullOrWhiteSpace(value) == false);
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return null;
        }

        return new ResourceEventMetadataRegistration(
            GetTypeName(resultType),
            GetTypeName(bodyType),
            eventId!);
    }

    private static ITypeSymbol? TryGetResourceEventBodyType(INamedTypeSymbol resultType)
    {
        for (var current = resultType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.TypeArguments.Length == 1)
            {
                if ((current.IsInNamespace("Blueprint", "Runtime", "Exec")
                        && string.Equals(current.Name, "ResourceEventResult", StringComparison.Ordinal))
                    || (current.IsInNamespace("Blueprint", "Http")
                        && string.Equals(current.Name, "ResourceEvent", StringComparison.Ordinal)))
                {
                    return current.TypeArguments[0];
                }
            }
        }

        return null;
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<ResourceEventMetadataRegistration> registrations)
    {
        var renderedOutcomes = registrations
            .Select(static registration =>
                "new global::Blueprint.Model.OperationResourceEventMetadata("
                + $"{CSharpSymbolDisplay.FormatLiteral(registration.ResultTypeName, quote: true)}, "
                + $"{CSharpSymbolDisplay.FormatLiteral(registration.BodyTypeName, quote: true)}, "
                + $"{CSharpSymbolDisplay.FormatLiteral(registration.EventId, quote: true)})")
            .ToArray();
        if (renderedOutcomes.Length == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationResourceEventMetadataFeatureData("
            + $"global::System.Collections.Immutable.ImmutableArray.Create({string.Join(", ", renderedOutcomes)})));");
    }
}
