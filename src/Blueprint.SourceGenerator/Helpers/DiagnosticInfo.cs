using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// A simple diagnostic info class that can be used to create a diagnostic from a <see cref="DiagnosticDescriptor"/>, used
/// in a <See cref="Result{TValue}"/> to report diagnostics in a cacheable way.
/// </summary>
[DebuggerDisplay("{_descriptor.Id}: {_descriptor.Title} @ {_location}")]
public sealed record DiagnosticInfo
{
    private readonly DiagnosticDescriptor _descriptor;
    private readonly EquatableList<string>? _messageArgs;
    private readonly LocationInfo? _location;

    public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location, EquatableList<string>? messageArgs = null)
    {
        this._descriptor = descriptor;
        this._messageArgs = messageArgs;
        this._location = location;
    }

    // Explicit constructor to convert Location into LocationInfo
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, EquatableList<string>? messageArgs = null)
    {
        this._descriptor = descriptor;
        this._messageArgs = messageArgs;
        this._location = location is not null ? LocationInfo.CreateFrom(location) : null;
    }

    /// <summary>
    /// Converts this <see cref="DiagnosticInfo"/> to a <see cref="Diagnostic"/> to report.
    /// </summary>
    /// <returns>This diagnostic as a <see cref="Diagnostic"/>.</returns>
    public Diagnostic ToDiagnostic()
    {
        // ReSharper disable once CoVariantArrayConversion The array is not for writing, the covariant conversion is fine
        return Diagnostic.Create(this._descriptor, this._location?.ToLocation(), this._messageArgs?.AsArray());
    }
}
