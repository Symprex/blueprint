using Microsoft.CodeAnalysis;

namespace Blueprint.Migration.Analyzers;

/// <summary>
/// Represents a deterministic, machine-readable migration report for a specific diagnostic family.
/// </summary>
public sealed class MigrationReport
{
    private const int CurrentSchemaVersion = 1;
    private const string ActivationDiagnosticsReportType = "activation-diagnostics";

    /// <summary>
    /// Initializes a new instance of the <see cref="MigrationReport"/> class.
    /// </summary>
    /// <param name="reportType">The stable report family identifier.</param>
    /// <param name="findings">The findings that belong to the report.</param>
    private MigrationReport(string reportType, IEnumerable<DiagnosticFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(reportType))
        {
            throw new ArgumentException("Report type must be a non-empty value.", nameof(reportType));
        }

        if (findings is null)
        {
            throw new ArgumentNullException(nameof(findings));
        }

        this.ReportType = reportType;
        this.Findings = findings
            .OrderBy(static finding => finding.DiagnosticId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Gets the schema version so later tooling can evolve the JSON contract without guessing.
    /// </summary>
    public int SchemaVersion { get; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets the stable family name for the report payload.
    /// </summary>
    public string ReportType { get; }

    /// <summary>
    /// Gets the ordered findings that belong to the report.
    /// </summary>
    public IReadOnlyList<DiagnosticFinding> Findings { get; }

    /// <summary>
    /// Creates a report for the currently shipped activation diagnostics so later codemod/report tooling can
    /// classify analyzer families without scraping text output from build logs.
    /// </summary>
    /// <param name="descriptors">The activation diagnostic descriptors that should appear in the report.</param>
    /// <returns>A deterministic activation-diagnostics report.</returns>
    public static MigrationReport CreateActivationDiagnostics(params DiagnosticDescriptor[] descriptors)
    {
        if (descriptors is null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        return new MigrationReport(
            ActivationDiagnosticsReportType,
            descriptors.Select(static descriptor => DiagnosticFinding.FromDescriptor(descriptor)));
    }
}
