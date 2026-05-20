using System.Text;
using System.Text.RegularExpressions;

namespace ResourceEventMigration;

/// <summary>
/// Generates replacement [ResourceEventFamily] record files from parsed factory definitions.
/// </summary>
public static class EventsFileGenerator
{
    /// <summary>
    /// Generates the new [ResourceEventFamily] record file content for a given EventsClass.
    /// </summary>
    public static string Generate(EventsClass eventsClass)
    {
        var sb = new StringBuilder();

        // Collect all usings (deduped, excluding Blueprint.Http which is being replaced)
        var allUsings = new HashSet<string>(StringComparer.Ordinal)
        {
            "Blueprint.Model",
            "Blueprint.Model.Attributes",
            "Blueprint.Runtime.Exec",
        };

        foreach (var u in eventsClass.Usings.Where(u => !u.StartsWith("Blueprint.Http")))
        {
            allUsings.Add(u);
        }

        foreach (var u in allUsings.OrderBy(static x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"using {u};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {eventsClass.Namespace};");
        sb.AppendLine();

        // Determine the new class name (XxxEvents → XxxResourceEvents)
        var newClassName = DeriveResourceEventsClassName(eventsClass.ClassName);
        var resourceObject = DeriveResourceObject(eventsClass);

        // For each factory, generate a [ResourceEventFamily] class
        var factoryGroups = eventsClass.Events.GroupBy(e => e.FactoryFieldName).ToList();

        foreach (var group in factoryGroups)
        {
            var factory = eventsClass.Factories.FirstOrDefault(f => f.FieldName == group.Key);
            if (factory == null) continue;

            var familyClassName = factoryGroups.Count > 1
                ? DeriveResourceEventsClassName(eventsClass.ClassName, factory)
                : newClassName;

            var resObj = DeriveResourceObjectFromType(factory.ResourceType);

            sb.AppendLine($"[ResourceEventFamily(typeof({factory.ResourceType}), ResourceObject = \"{resObj}\")]");
            sb.AppendLine($"public static partial class {familyClassName}");
            sb.AppendLine("{");

            foreach (var evt in group)
            {
                var recordName = evt.Name;
                var changeTypeStr = evt.ChangeType.ToString();
                var eventSubId = evt.EventSubId;

                // Determine record parameter based on factory kind
                string recordParam;
                string returnType;

                switch (factory.Kind)
                {
                    case FactoryKind.SelfQuery:
                        var queryType = factory.QueryType ?? "UNKNOWN_QUERY";
                        recordParam = $"[ResourceEventSelfQuery] {queryType} SelfQuery";
                        returnType = $"DeferredResourceEventResult<{factory.ResourceType}>";
                        break;

                    case FactoryKind.Mapper:
                        recordParam = $"{factory.ResourceType} Resource";
                        returnType = $"ResourceEventResult<{factory.ResourceType}>";
                        break;

                    case FactoryKind.NoMapper:
                        recordParam = $"{factory.ResourceType} Resource";
                        returnType = $"ResourceEventResult<{factory.ResourceType}>";
                        break;

                    default:
                        recordParam = "UNKNOWN";
                        returnType = "UNKNOWN";
                        break;
                }

                // Build event ID string for the attribute
                var reasonStr = eventSubId != null ? $"\"{eventSubId}\"" : $"\"{evt.ChangeType.ToString().ToLowerInvariant()}\"";

                // For default events without subId, use the default name
                if (eventSubId == null)
                {
                    reasonStr = evt.ChangeType switch
                    {
                        ChangeTypeKind.Created => "\"created\"",
                        ChangeTypeKind.Updated => "\"updated\"",
                        ChangeTypeKind.Deleted => "\"deleted\"",
                        _ => "\"unknown\"",
                    };
                }

                sb.AppendLine($"    [ResourceEventReason(ChangeType.{changeTypeStr}, {reasonStr})]");
                sb.AppendLine($"    public sealed partial record {recordName}({recordParam});");

                if (evt != group.Last())
                {
                    sb.AppendLine();
                }
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Gets the mapping of old event references to new ones for handler transformation.
    /// Returns entries like: ("UserEvents.Created", "UserResourceEvents.Created", "new GetUserQuery { Id = user.ApiId }")
    /// </summary>
    public static List<EventMapping> GetEventMappings(EventsClass eventsClass)
    {
        var mappings = new List<EventMapping>();
        var factoryGroups = eventsClass.Events.GroupBy(e => e.FactoryFieldName).ToList();
        var newClassName = DeriveResourceEventsClassName(eventsClass.ClassName);

        foreach (var group in factoryGroups)
        {
            var factory = eventsClass.Factories.FirstOrDefault(f => f.FieldName == group.Key);
            if (factory == null) continue;

            var familyClassName = factoryGroups.Count > 1
                ? DeriveResourceEventsClassName(eventsClass.ClassName, factory)
                : newClassName;

            foreach (var evt in group)
            {
                mappings.Add(new EventMapping(
                    OldReference: $"{eventsClass.ClassName}.{evt.Name}",
                    NewClassName: familyClassName,
                    RecordName: evt.Name,
                    Factory: factory,
                    SourceUsings: eventsClass.Usings));
            }
        }

        return mappings;
    }

    private static string DeriveResourceEventsClassName(string oldClassName)
    {
        // UserEvents → UserResourceEvents
        if (oldClassName.EndsWith("Events"))
        {
            return oldClassName[..^"Events".Length] + "ResourceEvents";
        }

        return oldClassName + "ResourceEvents";
    }

    private static string DeriveResourceEventsClassName(string oldClassName, FactoryDefinition factory)
    {
        // When multiple factories exist, derive from resource type
        var resourceName = factory.ResourceType
            .Replace("ApiResource", "")
            .Replace("Resource", "");

        return resourceName + "ResourceEvents";
    }

    private static string DeriveResourceObject(EventsClass eventsClass)
    {
        var factory = eventsClass.Factories.FirstOrDefault();
        if (factory == null) return "unknown";
        return DeriveResourceObjectFromType(factory.ResourceType);
    }

    /// <summary>
    /// Derives the ResourceObject string from the API resource type name, matching the logic
    /// in ApiResource.GetTypeName: removes "ApiResource"/"Resource"/"DTO" suffixes and camelizes.
    /// </summary>
    public static string DeriveResourceObjectFromType(string typeName)
    {
        var name = typeName
            .Replace("ApiResource", "")
            .Replace("Resource", "")
            .Replace("DTO", "");

        // Camelize: first char lowercase
        if (name.Length > 0)
        {
            name = char.ToLowerInvariant(name[0]) + name[1..];
        }

        return name;
    }

    /// <summary>
    /// Determines the return type for commands based on factory kind.
    /// </summary>
    public static string GetCommandReturnType(FactoryDefinition factory)
    {
        return factory.Kind switch
        {
            FactoryKind.SelfQuery => $"DeferredResourceEventResult<{factory.ResourceType}>",
            FactoryKind.Mapper => $"ResourceEventResult<{factory.ResourceType}>",
            FactoryKind.NoMapper => $"ResourceEventResult<{factory.ResourceType}>",
            _ => $"ResourceEventResult<{factory.ResourceType}>",
        };
    }
}

/// <summary>
/// Maps an old event reference (e.g. "UserEvents.Created") to the new record constructor.
/// </summary>
public sealed record EventMapping(
    string OldReference,
    string NewClassName,
    string RecordName,
    FactoryDefinition Factory,
    List<string> SourceUsings);
