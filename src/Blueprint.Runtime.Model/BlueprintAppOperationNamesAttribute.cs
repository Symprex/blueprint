using System.Diagnostics;

namespace Blueprint.Model;

/// <summary>
/// Declares how a Blueprint app renames the operations it hosts, so that every reader of
/// <see cref="ApiOperationDescriptor.Name"/> in that app — OpenAPI <c>operationId</c> values, request-body schema keys,
/// <c>x-links</c> extensions, and code that reads <c>link.OperationDescriptor.Name</c> such as an authorisation-manifest
/// query — sees the app's name rather than the contract's.
/// </summary>
/// <remarks>
/// <para>
/// The rename belongs to the app root rather than the operation because one contract operation can be hosted by
/// several apps, and each may name it differently. The generated app registration applies it to the app's own
/// descriptor instances before they are registered, so descriptors created by <c>Configure()</c> elsewhere are
/// unaffected.
/// </para>
/// <para>
/// It replaces legacy Blueprint's per-app <c>IOperationScannerConvention</c> that ran
/// <c>descriptor.Name = descriptor.Name.Replace(prefix, string.Empty)</c>, and keeps those semantics exactly: an ordinal
/// removal of every occurrence of <see cref="Remove"/>, applied after the <c>Query</c>/<c>Command</c> suffix has been
/// stripped. Resource and response schema names are not affected, nor are the compile-time manifest artifacts, which are
/// keyed by operation type. Two operations that the rename gives the same name, or an operation it leaves with no name
/// at all, fail the build with <c>BP011</c>.
/// </para>
/// </remarks>
[Conditional("CODE_ANALYSIS")]
[AttributeUsage(AttributeTargets.Class)]
public sealed class BlueprintAppOperationNamesAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the text removed, wherever it occurs, from the name of every operation this app hosts — for example
    /// <c>"Portal"</c>, so that <c>PortalResetActiveUsersCommand</c> is named <c>ResetActiveUsers</c>. A
    /// <see langword="null"/> or empty value leaves names unchanged.
    /// </summary>
    public string? Remove { get; set; }
}
