namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Exposes the stable stage names that tests use when asserting incremental-generator cacheability.
/// </summary>
public interface ITrackedSourceGenerator
{
    /// <summary>
    /// Gets the tracked stage names that should appear in incremental run-step diagnostics.
    /// </summary>
    string[] StageNames { get; }
}
