namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Records one compile-time-proven body-bearing resource-event outcome so later descriptor rendering can project stable
/// event-id metadata into feature data.
/// </summary>
/// <param name="ResultTypeName">The fully-qualified semantic result type name.</param>
/// <param name="BodyTypeName">The fully-qualified payload CLR type name.</param>
/// <param name="EventId">The stable serialized event identifier.</param>
public sealed record ResourceEventMetadataRegistration(string ResultTypeName, string BodyTypeName, string EventId);
