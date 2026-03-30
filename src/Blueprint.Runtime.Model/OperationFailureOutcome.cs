namespace Blueprint.Model;

/// <summary>
/// Describes one ordered failure outcome that a generated operation descriptor exposes from the approved exception
/// translator policy.
/// </summary>
/// <param name="TranslatorId">The stable translator identifier that owns this failure outcome.</param>
/// <param name="HandledExceptionTypeName">The fully-qualified exception type name owned by the translator.</param>
/// <param name="ResultTypeName">The fully-qualified operation result type name produced for the handled exception family.</param>
public sealed record OperationFailureOutcome(string TranslatorId, string HandledExceptionTypeName, string ResultTypeName);
