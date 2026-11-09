using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// An operation derived from one a typed client declares, with a member of its own, which a call through the declared type's
/// method must still send.
/// </summary>
public partial class AnnotatedArchiveTemplateCommand : ArchiveTemplateCommand
{
    /// <summary>
    /// Gets or sets a note only the derived type declares.
    /// </summary>
    public string? Note { get; set; }
}
