using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// A simple result class used during source generation as part of an incremental generation pipeline, allowing
/// for creating "safe" and cacheable results that may possibly include diagnostics too.
/// </summary>
/// <param name="Value">The value of the result.</param>
/// <param name="Diagnostics">Any diagnostics that must be reported.</param>
/// <typeparam name="TValue">The result type.</typeparam>
public sealed record Result<TValue>(TValue? Value, EquatableList<DiagnosticInfo> Diagnostics)
    where TValue : IEquatable<TValue>?
{
    /// <summary>
    /// Reports any diagnostics that have been generated.
    /// </summary>
    /// <param name="spc">The context to report the diagnostics in.</param>
    public void ReportDiagnostics(SourceProductionContext spc)
    {
        foreach (var diagnosticInfo in this.Diagnostics)
        {
            spc.ReportDiagnostic(diagnosticInfo.ToDiagnostic());
        }
    }

    /// <summary>
    /// Creates a failed result from a single diagnostic without forcing callers to allocate a collection first.
    /// </summary>
    /// <param name="diagnostics">The diagnostic that explains the failure.</param>
    /// <returns>A failed result that carries the supplied diagnostic.</returns>
    public static Result<TValue> Failure(DiagnosticInfo diagnostics)
    {
        return new(default, [diagnostics]);
    }

    /// <summary>
    /// Creates a failed result from an existing diagnostic collection.
    /// </summary>
    /// <param name="diagnostics">The diagnostics that explain the failure.</param>
    /// <returns>A failed result that carries the supplied diagnostics.</returns>
    public static Result<TValue> Failure(EquatableList<DiagnosticInfo> diagnostics)
    {
        return new(default, diagnostics);
    }

    /// <summary>
    /// Creates a failed result from an array of diagnostics.
    /// </summary>
    /// <param name="diagnostics">The diagnostics that explain the failure.</param>
    /// <returns>A failed result that carries the supplied diagnostics.</returns>
    public static Result<TValue> Failure(DiagnosticInfo[] diagnostics)
    {
        return new(default, new EquatableList<DiagnosticInfo>(diagnostics));
    }
}

/// <summary>
/// Provides factory helpers for creating successful generator results without repeating empty diagnostic collections.
/// </summary>
internal static class Result
{
    /// <summary>
    /// Creates a successful result with no diagnostics.
    /// </summary>
    /// <typeparam name="TValue">The successful payload type.</typeparam>
    /// <param name="value">The value produced by the generator stage.</param>
    /// <returns>A successful result that wraps the supplied value.</returns>
    public static Result<TValue> Success<TValue>(TValue value) where TValue : IEquatable<TValue>?
    {
        return new(value, []);
    }
}
