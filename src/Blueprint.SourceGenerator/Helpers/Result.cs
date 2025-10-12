using Blueprint.SourceGenerator.AppDataModel.Model;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// A simple result class used during source generation as part of an incremental generation pipeline, allowing
/// for creating "safe" and cacheable results that may possibly include diagnostics too.
/// </summary>
/// <param name="Value">The value of the result.</param>
/// <param name="Diagnostics">Any diagnostics that must be reported.</param>
/// <typeparam name="TValue">The result type.</typeparam>
internal sealed record Result<TValue>(TValue? Value, EquatableList<DiagnosticInfo> Diagnostics)
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

    public static Result<TValue> Failure(DiagnosticInfo diagnostics)
    {
        return new(default, [diagnostics]);
    }

    public static Result<TValue> Failure(EquatableList<DiagnosticInfo> diagnostics)
    {
        return new(default, diagnostics);
    }

    public static Result<TValue> Failure(DiagnosticInfo[] diagnostics)
    {
        return new(default, new EquatableList<DiagnosticInfo>(diagnostics));
    }
}

internal static class Result
{
    public static Result<TValue> Success<TValue>(TValue value) where TValue : IEquatable<TValue>?
    {
        return new(value, []);
    }
}
