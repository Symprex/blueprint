namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Shared helpers for normalising operation type names into descriptor names.
/// Both the per-operation descriptor generator and the app-wide registration
/// generator reference these helpers so the same naming rules are applied
/// consistently and collision detection is possible.
/// </summary>
/// <remarks>
/// Suffix stripping belongs to the operation and is baked into its generated descriptor. An app's
/// <c>[BlueprintAppOperationNames]</c> removal belongs to the app, because one operation can be hosted by several apps,
/// so it is applied at runtime by the app registration instead — and <see cref="RemoveFromOperationName"/> is its
/// compile-time mirror, which the collision check relies on agreeing with that emitted code.
/// </remarks>
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

    /// <summary>
    /// Gives the descriptor name an app registers once its <c>[BlueprintAppOperationNames(Remove = ...)]</c> has been
    /// applied: every ordinal occurrence of <paramref name="removal"/> is removed from a name that
    /// <see cref="StripOperationSuffix"/> has already produced. That order and those semantics are legacy Blueprint's
    /// <c>descriptor.Name.Replace(prefix, string.Empty)</c> convention, which also ran on the suffix-stripped name.
    /// </summary>
    /// <param name="strippedName">The descriptor name after suffix stripping.</param>
    /// <param name="removal">The text the app removes; an empty value leaves the name unchanged.</param>
    /// <returns>The name the app registers the operation under.</returns>
    internal static string RemoveFromOperationName(string strippedName, string removal)
    {
        return removal.Length == 0
            ? strippedName
            : strippedName.Replace(removal, string.Empty, StringComparison.Ordinal);
    }
}
