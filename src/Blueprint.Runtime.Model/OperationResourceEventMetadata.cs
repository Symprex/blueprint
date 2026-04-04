namespace Blueprint.Model;

/// <summary>
/// Describes one body-bearing resource-event outcome that descriptor generation could prove from a declared semantic
/// result contract.
/// </summary>
/// <param name="ResultTypeName">The fully-qualified semantic result type name.</param>
/// <param name="BodyTypeName">The fully-qualified payload CLR type name.</param>
/// <param name="EventId">The stable serialized event identifier for the payload type.</param>
public sealed record OperationResourceEventMetadata(string ResultTypeName, string BodyTypeName, string EventId);
