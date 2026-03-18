namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable debug-manifest planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being described.</param>
public sealed record GeneratedDebugManifest(OperationModel Operation)
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the current manifest entries.
    /// </summary>
    public IReadOnlyDictionary<string, string> Entries => this._entries;

    /// <summary>
    /// Adds or replaces a manifest entry.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The entry value.</param>
    public void AddEntry(string key, string value)
    {
        this._entries[key] = value;
    }
}
