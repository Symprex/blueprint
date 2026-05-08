using System.ComponentModel.DataAnnotations;

using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// A sample command that exercises handler-level exception translation by throwing
/// <see cref="Blueprint.Validation.ValidationException"/> or <see cref="Blueprint.NotFoundException"/>
/// depending on the input. Used by E2E tests to prove the generated pipeline correctly translates
/// handler-thrown exceptions into the appropriate HTTP responses.
/// </summary>
[RootLink("/templates/{Id}/validate")]
[HttpPost]
[MustBeAuthenticated]
public partial class ValidateTemplateCommand : ICommand
{
    /// <summary>
    /// Gets or sets the template identifier.
    /// </summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the template name to validate. "INVALID" triggers a validation exception,
    /// "BAD" triggers a bad-request exception.
    /// </summary>
    public string? Name { get; set; }
}
