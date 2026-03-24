namespace Blueprint.Codemod;

/// <summary>
/// Describes an apply-mode codemod request so the tool can reuse the dry-run planning seam while deciding which
/// supported migrations should be written back to disk.
/// </summary>
public sealed class ApplyModeOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyModeOptions"/> class.
    /// </summary>
    /// <param name="solutionPath">The solution whose supported migrations should be applied.</param>
    /// <param name="diagnosticFilter">The diagnostic filter such as <c>BPMIG*</c>.</param>
    public ApplyModeOptions(string solutionPath, string diagnosticFilter)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            throw new ArgumentException("Solution path must be a non-empty value.", nameof(solutionPath));
        }

        if (string.IsNullOrWhiteSpace(diagnosticFilter))
        {
            throw new ArgumentException("Diagnostic filter must be a non-empty value.", nameof(diagnosticFilter));
        }

        this.SolutionPath = solutionPath;
        this.DiagnosticFilter = diagnosticFilter;
    }

    /// <summary>
    /// Gets the solution path whose supported migrations should be applied.
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    /// Gets the diagnostic filter such as <c>BPMIG*</c>.
    /// </summary>
    public string DiagnosticFilter { get; }
}
