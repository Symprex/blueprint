namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Shared helpers for normalising operation type names into descriptor names.
/// Both the per-operation descriptor generator and the app-wide registration
/// generator reference these helpers so the same naming rules are applied
/// consistently and collision detection is possible.
/// </summary>
internal static class OperationNameHelpers
{
    /// <summary>
    /// Strips trailing <c>Query</c> or <c>Command</c> suffixes from an operation type name
    /// to produce the descriptor name. This matches legacy Blueprint's convention where
    /// <c>GetSentEmailQuery</c> becomes <c>GetSentEmail</c> and flows through to
    /// OpenAPI <c>operationId</c> values and TypeScript client generation.
    /// </summary>
    /// <param name="typeName">The simple (non-qualified) operation class name.</param>
    /// <remarks>
    /// Length guards (<c>&gt; 5</c> / <c>&gt; 7</c>) prevent stripping names that are
    /// <em>only</em> "Query" or "Command".  <c>BackgroundTask</c> suffix is intentionally
    /// <strong>not</strong> stripped — it is a distinct operation category, not a naming convention.
    /// </remarks>
    /// <returns>The descriptor name with the suffix removed, or the original name if no suffix matched.</returns>
    internal static string StripOperationSuffix(string typeName)
    {
        if (typeName.Length > 5 && typeName.EndsWith("Query", StringComparison.Ordinal))
        {
            return typeName.Substring(0, typeName.Length - 5);
        }

        if (typeName.Length > 7 && typeName.EndsWith("Command", StringComparison.Ordinal))
        {
            return typeName.Substring(0, typeName.Length - 7);
        }

        return typeName;
    }
}
