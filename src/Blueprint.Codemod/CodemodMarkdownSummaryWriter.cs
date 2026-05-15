using System.Text;
using Blueprint.Migration.Analyzers;

namespace Blueprint.Codemod;

/// <summary>
/// Projects the canonical codemod report into a deterministic markdown summary for quick human review.
/// </summary>
public sealed class CodemodMarkdownSummaryWriter
{
    private static readonly string[] CategoryOrder =
    [
        "auto-fixable",
        "configuration required",
        "consumer-extension rewrite required",
        "manual design review required",
    ];

    /// <summary>
    /// Renders a markdown summary from the canonical migration report with optional instance counts.
    /// </summary>
    /// <param name="report">The canonical migration report.</param>
    /// <param name="instanceCounts">Optional per-diagnostic instance counts from SARIF analysis.</param>
    /// <returns>The deterministic markdown summary.</returns>
    public string Write(MigrationReport report, IReadOnlyDictionary<string, int>? instanceCounts = null)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"# {report.ReportType}");
        builder.AppendLine();
        builder.AppendLine($"- Schema version: {report.SchemaVersion}");
        builder.AppendLine($"- Findings: {report.Findings.Count}");
        builder.AppendLine();
        builder.AppendLine("## Buckets");
        builder.AppendLine();

        foreach (var category in CategoryOrder)
        {
            builder.AppendLine($"- {category}: {report.Findings.Count(finding => finding.Category == category)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Findings");
        builder.AppendLine();

        foreach (var finding in report.Findings)
        {
            builder.AppendLine($"### {finding.DiagnosticId}");
            builder.AppendLine($"- Title: {finding.Title}");
            builder.AppendLine($"- Category: {finding.Category}");
            builder.AppendLine($"- Severity: {finding.Severity}");

            if (instanceCounts is not null && instanceCounts.TryGetValue(finding.DiagnosticId, out var count))
            {
                builder.AppendLine($"- Instances: {count}");
            }

            builder.AppendLine($"- Documentation: {finding.DocumentationPath}");
            builder.AppendLine($"- Message: {finding.Message}");
            builder.AppendLine();
        }

        return builder.ToString()
            .TrimEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}
