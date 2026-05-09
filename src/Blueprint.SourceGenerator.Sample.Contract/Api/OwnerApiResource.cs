using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents the owner (user) of a template. Used as an <see cref="Expandable{T}"/>
/// reference on <see cref="TemplateApiResource"/> to demonstrate compile-time resource
/// expansion: the generated pipeline discovers the <c>Expandable&lt;OwnerApiResource&gt;</c>
/// property at source-generation time and emits typed expansion calls that resolve the
/// <c>IResourceExpander</c> from DI at runtime.
/// </summary>
public partial class OwnerApiResource : ApiResource
{
    /// <summary>
    /// Gets or sets the owner's unique identifier.
    /// </summary>
    public string Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the owner's display name.
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Gets or sets the owner's email address.
    /// </summary>
    public string Email { get; set; } = null!;
}
