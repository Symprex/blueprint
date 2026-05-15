using System.Text.Json;

namespace Blueprint.Codemod;

/// <summary>
/// Reads the subset of SARIF required by the codemod dry-run collector and rejects malformed roots before they silently drop diagnostics.
/// </summary>
internal static class SarifDiagnosticReader
{
    /// <summary>
    /// Reads and validates the diagnostic identifiers emitted in a SARIF stream.
    /// </summary>
    /// <param name="sarifStream">The SARIF payload stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sorted distinct diagnostic identifiers from the SARIF payload.</returns>
    internal static async Task<IReadOnlyList<string>> ReadDiagnosticIdsAsync(Stream sarifStream, CancellationToken cancellationToken)
    {
        if (sarifStream is null)
        {
            throw new ArgumentNullException(nameof(sarifStream));
        }

        using var document = await JsonDocument.ParseAsync(sarifStream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("runs", out var runsElement)
            || runsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The codemod dry run expected the SARIF payload to contain a 'runs' array.");
        }

        var diagnosticIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var runElement in runsElement.EnumerateArray())
        {
            if (!runElement.TryGetProperty("results", out var resultsElement))
            {
                continue;
            }

            if (resultsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The codemod dry run expected each SARIF run to expose a 'results' array.");
            }

            foreach (var resultElement in resultsElement.EnumerateArray())
            {
                if (!resultElement.TryGetProperty("ruleId", out var ruleIdElement))
                {
                    throw new InvalidOperationException("The codemod dry run expected each SARIF result to expose a string 'ruleId'.");
                }

                if (ruleIdElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException("The codemod dry run expected each SARIF 'ruleId' value to be a JSON string.");
                }

                var diagnosticId = ruleIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(diagnosticId))
                {
                    diagnosticIds.Add(diagnosticId);
                }
            }
        }

        return diagnosticIds.ToArray();
    }

    /// <summary>
    /// Reads diagnostic identifiers with instance counts from a SARIF stream. Unlike <see cref="ReadDiagnosticIdsAsync"/>
    /// which returns distinct IDs, this method counts every occurrence so the migration report can include scale information.
    /// </summary>
    /// <param name="sarifStream">The SARIF payload stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary mapping diagnostic IDs to their occurrence count in this SARIF file.</returns>
    internal static async Task<Dictionary<string, int>> ReadDiagnosticCountsAsync(Stream sarifStream, CancellationToken cancellationToken)
    {
        if (sarifStream is null)
        {
            throw new ArgumentNullException(nameof(sarifStream));
        }

        using var document = await JsonDocument.ParseAsync(sarifStream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("runs", out var runsElement)
            || runsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The codemod dry run expected the SARIF payload to contain a 'runs' array.");
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var runElement in runsElement.EnumerateArray())
        {
            if (!runElement.TryGetProperty("results", out var resultsElement)
                || resultsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var resultElement in resultsElement.EnumerateArray())
            {
                if (!resultElement.TryGetProperty("ruleId", out var ruleIdElement)
                    || ruleIdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var diagnosticId = ruleIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(diagnosticId))
                {
                    counts[diagnosticId] = counts.TryGetValue(diagnosticId, out var current) ? current + 1 : 1;
                }
            }
        }

        return counts;
    }
}
