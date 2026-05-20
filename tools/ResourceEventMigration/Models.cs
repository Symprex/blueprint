using System.Text.RegularExpressions;

namespace ResourceEventMigration;

/// <summary>
/// Represents a parsed ResourceEventDefinition factory from an *Events.cs file.
/// </summary>
public sealed record FactoryDefinition(
    string FieldName,
    string ResourceType,
    string? DomainType,
    FactoryKind Kind,
    string? QueryType,
    string? LambdaParameter,
    string? LambdaBody);

/// <summary>
/// Represents a parsed event definition (e.g., Created, Updated, Deleted).
/// </summary>
public sealed record EventDefinition(
    string Name,
    string FactoryFieldName,
    ChangeTypeKind ChangeType,
    string? EventSubId);

/// <summary>
/// Represents a complete Events class with its factory and event definitions.
/// </summary>
public sealed record EventsClass(
    string ClassName,
    string Namespace,
    string FilePath,
    List<string> Usings,
    List<FactoryDefinition> Factories,
    List<EventDefinition> Events);

public enum FactoryKind
{
    SelfQuery,
    Mapper,
    NoMapper,
}

public enum ChangeTypeKind
{
    Created,
    Updated,
    Deleted,
}
