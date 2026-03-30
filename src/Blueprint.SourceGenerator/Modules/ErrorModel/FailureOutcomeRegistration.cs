namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Carries descriptor-planning metadata for one ordered failure outcome without forcing the generator assembly to
/// reference runtime-model feature data types.
/// </summary>
/// <param name="TranslatorId">The stable translator identifier.</param>
/// <param name="HandledExceptionTypeName">The handled exception type name.</param>
/// <param name="ResultTypeName">The semantic result type name emitted by the translator.</param>
internal sealed record FailureOutcomeRegistration(
    string TranslatorId,
    string HandledExceptionTypeName,
    string ResultTypeName);
