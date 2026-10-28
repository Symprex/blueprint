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

    /// <summary>
    /// Gets the underlying <see cref="DiagnosticDescriptor"/> so callers can inspect severity
    /// or ID without materialising a full <see cref="Diagnostic"/>.
    /// </summary>
    public DiagnosticDescriptor Descriptor => this._descriptor;

    /// <summary>
    /// Gets the normalised location this diagnostic is reported at, if it has one.
    /// </summary>
    internal LocationInfo? Location => this._location;

    /// <summary>
    /// Initializes a cacheable diagnostic wrapper from an already-normalized <see cref="LocationInfo"/> instance.
    /// </summary>
    /// <param name="descriptor">The diagnostic descriptor to report.</param>
    /// <param name="location">The normalized diagnostic location, if one is available.</param>
    /// <param name="messageArgs">Optional message-format arguments for the descriptor.</param>
    public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location, EquatableList<string>? messageArgs = null)
    {
        this._descriptor = descriptor;
        this._messageArgs = messageArgs;
        this._location = location;
    }

    /// <summary>
    /// Initializes a cacheable diagnostic wrapper from a Roslyn <see cref="Location"/>, converting it into the
    /// normalized location shape used by incremental result payloads.
    /// </summary>
    /// <param name="descriptor">The diagnostic descriptor to report.</param>
    /// <param name="location">The Roslyn location to normalize, if one is available.</param>
    /// <param name="messageArgs">Optional message-format arguments for the descriptor.</param>
    public DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, EquatableList<string>? messageArgs = null)
    {
        this._descriptor = descriptor;
        this._messageArgs = messageArgs;
        this._location = location is not null ? LocationInfo.CreateFrom(location) : null;
    }

    /// <summary>
    /// Converts this <see cref="DiagnosticInfo"/> to a <see cref="Diagnostic"/> to report.
    /// </summary>
    /// <param name="compilation">
    /// The compilation being generated for, so the location can be bound to its syntax tree and suppressed in source.
    /// Pass <see langword="null"/> only where the diagnostic has no source location, or when inspecting it.
    /// </param>
    /// <returns>This diagnostic as a <see cref="Diagnostic"/>.</returns>
    public Diagnostic ToDiagnostic(Compilation? compilation)
    {
        // ReSharper disable once CoVariantArrayConversion The array is not for writing, the covariant conversion is fine
        return Diagnostic.Create(this._descriptor, this._location?.ToLocation(compilation), this._messageArgs?.AsArray());
    }
}
