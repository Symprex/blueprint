namespace Blueprint.Codemod;

/// <summary>
/// Describes a codemod solution-analysis request so dry-run reporting and apply-mode planning can reuse the same
/// validated solution/filter contract.
/// </summary>
public sealed class SolutionAnalysisOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SolutionAnalysisOptions"/> class.
    /// </summary>
    /// <param name="solutionPath">The solution to analyze.</param>
    /// <param name="diagnosticFilter">The diagnostic filter such as <c>BPMIG*</c>.</param>
    public SolutionAnalysisOptions(string solutionPath, string diagnosticFilter)
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
    /// Gets the solution path that should be analyzed.
    /// </summary>
    public string SolutionPath { get; }

    /// <summary>
    /// Gets the diagnostic filter such as <c>BPMIG*</c>.
    /// </summary>
    public string DiagnosticFilter { get; }
}
