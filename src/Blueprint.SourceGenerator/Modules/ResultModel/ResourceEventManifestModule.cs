using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes <c>resourceEvents.enabled</c> and <c>resourceEvents.events</c> to the per-operation debug manifest,
/// reflecting declared resource-event result outcomes (body-bearing and key-only) and their stable event identifiers.
/// </summary>
/// <remarks>
/// This module independently inspects the operation's declared result contract to find
/// <c>ResourceEventResult&lt;T&gt;</c> and <c>KeyOnlyResourceEventResult</c> outcomes. It does not depend on
/// <see cref="ResourceEventMetadataModule"/> metadata because the manifest should surface resource-event
/// declarations regardless of host exposure status.
/// </remarks>
internal sealed class ResourceEventManifestModule : IBlueprintSourceGeneratorModule, IDebugManifestContributor
{
    private const string ResourceEventAttributeMetadataName = "Blueprint.Model.Attributes.ResourceEventAttribute";

    /// <inheritdoc />
    public string ModuleId => nameof(ResourceEventManifestModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResourceEventManifestModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn => [];

    /// <inheritdoc />
    public void ContributeToManifest(DebugManifestContributorContext context)
    {
        var operationType = context.Operation.TypeSymbol;
        var declaredContractType = OperationResultContractHelper.GetDeclaredOperationResultType(operationType);

        if (declaredContractType is null)
        {
            context.AddEntry("resourceEvents.enabled", new JValue(false));
            return;
        }

        var events = CollectResourceEventOutcomes(declaredContractType);

        if (events.Count == 0)
        {
            context.AddEntry("resourceEvents.enabled", new JValue(false));
            return;
        }

        context.AddEntry("resourceEvents.enabled", new JValue(true));
        context.AddEntry("resourceEvents.events", new JArray(events));
    }

    private static List<JObject> CollectResourceEventOutcomes(ITypeSymbol declaredContractType)
    {
        var events = new List<JObject>();

        if (OperationResultContractHelper.TryGetResultsUnionMembers(declaredContractType, out var unionMembers))
        {
            foreach (var member in unionMembers)
            {
                TryAddResourceEventEntry(member, events);
            }
        }
        else
        {
            TryAddResourceEventEntry(declaredContractType, events);
        }

        return events;
    }

    private static void TryAddResourceEventEntry(ITypeSymbol resultType, List<JObject> events)
    {
        if (resultType is not INamedTypeSymbol namedResultType)
        {
            return;
        }

        var bodyType = TryGetResourceEventBodyType(namedResultType);
        if (bodyType is not null)
        {
            var eventId = GetResourceEventId(bodyType);
            var entry = new JObject
            {
                ["shape"] = "body-bearing",
                ["resultType"] = GetTypeName(namedResultType),
                ["bodyType"] = GetTypeName(bodyType),
            };

            if (eventId is not null)
            {
                entry["eventId"] = eventId;
            }

            events.Add(entry);
            return;
        }

        if (IsKeyOnlyResourceEventResult(namedResultType))
        {
            var entry = new JObject
            {
                ["shape"] = "key-only",
                ["resultType"] = GetTypeName(namedResultType),
            };

            events.Add(entry);
        }
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

    private static bool IsKeyOnlyResourceEventResult(INamedTypeSymbol resultType)
    {
        for (var current = (INamedTypeSymbol?)resultType; current is not null; current = current.BaseType)
        {
            if (current.IsInNamespace("Blueprint", "Runtime", "Exec")
                && string.Equals(current.Name, "KeyOnlyResourceEventResult", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetResourceEventId(ITypeSymbol bodyType)
    {
        return bodyType.GetAttributes()
            .Where(static attr => attr.AttributeClass?.ToDisplayString() == ResourceEventAttributeMetadataName)
            .Select(static attr => attr.ConstructorArguments.Length == 1 ? attr.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }
}
