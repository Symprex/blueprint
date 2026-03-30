using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Holds the ordered exception-translator policy for the generated executor shell.
/// The policy is validated before generation so specific translators stay deterministic and only the catch-all fallback
/// may overlap a more specific translator's exception family.
/// </summary>
internal sealed record PipelineExceptionTranslatorPolicy(IReadOnlyList<IPipelineExceptionTranslator> Translators)
{
    /// <summary>
    /// Creates and validates a policy from the supplied translators.
    /// </summary>
    /// <param name="translators">The translators to order and validate.</param>
    /// <returns>The ordered policy, or diagnostics if translator ownership overlaps unsafely.</returns>
    public static Result<PipelineExceptionTranslatorPolicy> Create(IReadOnlyList<IPipelineExceptionTranslator> translators)
    {
        var orderedTranslators = translators
            .OrderBy(static translator => translator.IsFallback)
            .ThenBy(static translator => translator.Order)
            .ThenBy(static translator => GetHandledExceptionTypeName(translator), StringComparer.Ordinal)
            .ThenBy(static translator => translator.TranslatorId, StringComparer.Ordinal)
            .ToArray();

        var diagnostics = CollectOverlapDiagnostics(orderedTranslators);

        return diagnostics.Count > 0
            ? Result<PipelineExceptionTranslatorPolicy>.Failure(diagnostics.ToArray())
            : Result.Success(new PipelineExceptionTranslatorPolicy(orderedTranslators));
    }

    /// <summary>
    /// Creates the default ordered policy that preserves the current validation and unhandled fallback behavior.
    /// </summary>
    /// <returns>The ordered default policy, or diagnostics if a future edit makes the configured translators overlap.</returns>
    public static Result<PipelineExceptionTranslatorPolicy> CreateDefault()
    {
        return Create(
        [
            LegacyValidationExceptionTranslator.Instance,
            DataAnnotationsValidationExceptionTranslator.Instance,
            DefaultPipelineExceptionTranslator.Instance,
        ]);
    }

    /// <summary>
    /// Writes the catch shell owned by this policy to the generated pipeline method.
    /// </summary>
    /// <param name="writer">The writer receiving the ordered catch blocks.</param>
    /// <param name="isHandlerAsync">Whether the generated execute method already returns asynchronously.</param>
    public void WriteExceptionHandling(ICodeWriter writer, bool isHandlerAsync)
    {
        foreach (var translator in this.Translators)
        {
            translator.WriteCatchBlock(writer, isHandlerAsync);
        }
    }

    private static List<DiagnosticInfo> CollectOverlapDiagnostics(IReadOnlyList<IPipelineExceptionTranslator> translators)
    {
        var diagnostics = new List<DiagnosticInfo>();

        for (var index = 0; index < translators.Count; index++)
        {
            var current = translators[index];

            for (var candidateIndex = index + 1; candidateIndex < translators.Count; candidateIndex++)
            {
                var candidate = translators[candidateIndex];

                if (current.IsFallback != candidate.IsFallback || !DoHandledExceptionTypesOverlap(current, candidate))
                {
                    continue;
                }

                diagnostics.Add(
                    new DiagnosticInfo(
                        DiagnosticDescriptors.ConflictingExceptionTranslatorOwnership,
                        (Location?)null,
                        [
                            current.TranslatorId,
                            GetHandledExceptionTypeName(current),
                            candidate.TranslatorId,
                            GetHandledExceptionTypeName(candidate),
                        ]));
            }
        }

        return diagnostics;
    }

    private static bool DoHandledExceptionTypesOverlap(IPipelineExceptionTranslator left, IPipelineExceptionTranslator right)
    {
        foreach (var leftTypeName in left.OwnedExceptionTypeNames)
        {
            foreach (var rightTypeName in right.OwnedExceptionTypeNames)
            {
                if (string.Equals(leftTypeName, rightTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetHandledExceptionTypeName(IPipelineExceptionTranslator translator)
    {
        return translator.HandledExceptionTypeName;
    }
}
