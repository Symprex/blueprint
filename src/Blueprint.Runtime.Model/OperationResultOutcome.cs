namespace Blueprint.Model;

/// <summary>
/// Describes one flattened semantic outcome that a generated operation descriptor exposes from its declared result contract.
/// </summary>
/// <param name="ResultType">The concrete semantic result CLR type for this outcome.</param>
public sealed record OperationResultOutcome(Type ResultType);
