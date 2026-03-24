using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Blueprint.Migration.Analyzers;

/// <summary>
/// Represents a single migration-diagnostic family in a structured report so the migration workflow can classify
/// findings consistently across analyzer output, codemod dry runs, and human/AI review artifacts.
/// </summary>
public sealed class DiagnosticFinding
{
    private const string AutoFixableCategory = "auto-fixable";
    private const string ConfigurationRequiredCategory = "configuration required";
    private const string ConsumerExtensionRewriteRequiredCategory = "consumer-extension rewrite required";
    private const string ManualDesignReviewRequiredCategory = "manual design review required";

    private DiagnosticFinding(
        string diagnosticId,
        string title,
        string severity,
        string message,
        string? description,
        string category,
        string documentationPath)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic ID must be a non-empty value.", nameof(diagnosticId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must be a non-empty value.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(severity))
        {
            throw new ArgumentException("Severity must be a non-empty value.", nameof(severity));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message must be a non-empty value.", nameof(message));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category must be a non-empty value.", nameof(category));
        }

        if (string.IsNullOrWhiteSpace(documentationPath))
        {
            throw new ArgumentException("Documentation path must be a non-empty value.", nameof(documentationPath));
        }

        this.DiagnosticId = diagnosticId;
        this.Title = title;
        this.Severity = severity;
        this.Message = message;
        this.Description = description;
        this.Category = category;
        this.DocumentationPath = documentationPath;
    }

    /// <summary>
    /// Gets the stable diagnostic identifier such as <c>BPMIG001</c>.
    /// </summary>
    public string DiagnosticId { get; }

    /// <summary>
    /// Gets the human-readable descriptor title for review output.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the diagnostic severity as a stable string so JSON projections stay easy to consume.
    /// </summary>
    public string Severity { get; }

    /// <summary>
    /// Gets the primary message contract for the diagnostic family.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the descriptor description when the analyzer exposes one.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the migration-planning bucket used by the repo's Phase 0 workflow.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the most relevant durable migration-plan path for the finding.
    /// </summary>
    public string DocumentationPath { get; }

    /// <summary>
    /// Creates a structured finding from a reserved <c>BPMIG</c> descriptor.
    /// </summary>
    /// <param name="descriptor">The migration descriptor to project.</param>
    /// <returns>A deterministic report finding.</returns>
    public static DiagnosticFinding FromDescriptor(DiagnosticDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        return new DiagnosticFinding(
            descriptor.Id,
            descriptor.Title.ToString(),
            descriptor.DefaultSeverity.ToString(),
            descriptor.MessageFormat.ToString(),
            descriptor.Description?.ToString(),
            GetCategory(descriptor.Id),
            GetDocumentationPath(descriptor.Id));
    }

    private static string GetCategory(string diagnosticId)
    {
        if (!TryGetMigrationIdNumber(diagnosticId, out var numericId))
        {
            throw new InvalidOperationException($"The migration report only supports reserved BPMIG diagnostic IDs. '{diagnosticId}' is not a supported migration diagnostic ID.");
        }

        return numericId switch
        {
            >= 1 and <= 99 => AutoFixableCategory,
            >= 100 and <= 199 => ConfigurationRequiredCategory,
            >= 200 and <= 299 => ConsumerExtensionRewriteRequiredCategory,
            >= 300 and <= 399 => ManualDesignReviewRequiredCategory,
            _ => throw new InvalidOperationException($"The migration report only supports the reserved BPMIG001-BPMIG399 range. '{diagnosticId}' falls outside the supported report range."),
        };
    }

    private static string GetDocumentationPath(string diagnosticId)
    {
        return diagnosticId switch
        {
            "BPMIG001" or "BPMIG100" => @"migration\openapi\plan.md",
            "BPMIG002" => @"migration\resource-model-links-and-post-processing\plan.md",
            "BPMIG003" or "BPMIG101" or "BPMIG200" or "BPMIG201" => @"migration\authentication-and-authorisation\plan.md",
            _ => @"migration\breaking-changes-and-migration-tooling\plan.md",
        };
    }

    private static bool TryGetMigrationIdNumber(string diagnosticId, out int numericId)
    {
        numericId = 0;

        if (diagnosticId is null
            || !diagnosticId.StartsWith("BPMIG", StringComparison.Ordinal)
            || diagnosticId.Length != 8)
        {
            return false;
        }

        return int.TryParse(
            diagnosticId.Substring(5),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out numericId);
    }
}
