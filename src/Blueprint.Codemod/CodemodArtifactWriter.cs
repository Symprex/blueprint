using System.Text.Json;
using Blueprint.Migration.Analyzers;

namespace Blueprint.Codemod;

/// <summary>
/// Writes the durable codemod review artifacts that should travel with dry-run or apply-mode output.
/// </summary>
public sealed class CodemodArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly CodemodMarkdownSummaryWriter _markdownSummaryWriter = new();

    /// <summary>
    /// Writes the canonical codemod JSON report and its derived markdown summary to an output directory.
    /// </summary>
    /// <param name="report">The canonical migration report to persist.</param>
    /// <param name="outputDirectory">The directory where the artifacts should be written.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when both artifacts have been written.</returns>
    public async Task WriteAsync(
        MigrationReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be a non-empty value.", nameof(outputDirectory));
        }

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var jsonPath = Path.Combine(fullOutputDirectory, "codemod-report.json");
        var markdownPath = Path.Combine(fullOutputDirectory, "codemod-report.md");

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken);
        await File.WriteAllTextAsync(
            markdownPath,
            this._markdownSummaryWriter.Write(report),
            cancellationToken);
    }
}
