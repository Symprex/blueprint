using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Copies a template, declaring a <see cref="ResourceCreated{TResource}"/> result, which no sample contract operation
/// declares. Called through <see cref="LocalOperationsApi"/>.
/// </summary>
[RootLink("/templates/{Id}/copies")]
[HttpPost]
[AllowAnonymous]
public partial class CopyTemplateCommand : ICommand<ResourceCreated<TemplateApiResource>>
{
    /// <summary>
    /// Gets or sets the identifier of the template to copy.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the copy's name.
    /// </summary>
    public string? Name { get; set; }
}
