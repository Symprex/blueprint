using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable debug-manifest planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being described.</param>
public sealed record GeneratedDebugManifest(OperationModel Operation)
{
    private readonly Dictionary<string, JToken> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _entryOwners = new(StringComparer.Ordinal);
    private readonly List<DebugManifestEntryCollision> _entryCollisions = [];

    /// <summary>
    /// Gets the current manifest entries.
    /// </summary>
    public IReadOnlyDictionary<string, JToken> Entries => this._entries;

    /// <summary>
    /// Gets the contributor collisions detected while modules build the manifest.
    /// </summary>
    public IReadOnlyList<DebugManifestEntryCollision> EntryCollisions => this._entryCollisions;

    /// <summary>
    /// Adds or replaces a manifest entry.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The entry value.</param>
    /// <param name="contributorId">The contributor claiming ownership of the entry.</param>
    public void AddEntry(string key, string value, string contributorId)
    {
        this.AddEntry(key, new JValue(value), contributorId);
    }

    /// <summary>
    /// Adds or replaces a manifest entry using a structured JSON token value.
    /// </summary>
    /// <param name="key">The entry key.</param>
    /// <param name="value">The entry value.</param>
    /// <param name="contributorId">The contributor claiming ownership of the entry.</param>
    public void AddEntry(string key, JToken value, string contributorId)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (this._entryOwners.TryGetValue(key, out var ownerContributorId) &&
            !string.Equals(ownerContributorId, contributorId, StringComparison.Ordinal))
        {
            // Keep the first owner so the resulting diagnostics explain which contributor already claimed the key.
            this._entryCollisions.Add(new DebugManifestEntryCollision(key, ownerContributorId, contributorId));
            return;
        }

        this._entries[key] = value.DeepClone();
        this._entryOwners[key] = contributorId;
    }

    /// <summary>
    /// Describes a conflicting attempt to claim an already-owned manifest entry.
    /// </summary>
    /// <param name="Key">The contested manifest entry key.</param>
    /// <param name="OwnerContributorId">The contributor that claimed the key first.</param>
    /// <param name="ConflictingContributorId">The later contributor that attempted to reuse the key.</param>
    public sealed record DebugManifestEntryCollision(
        string Key,
        string OwnerContributorId,
        string ConflictingContributorId);
}
