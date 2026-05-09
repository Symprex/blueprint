using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Declares that an operation requires the user to hold a permission scoped to a specific resource.
/// The resource key is determined by a template that can reference properties on the operation
/// instance via <c>{PropertyName}</c> tokens.
/// </summary>
/// <remarks>
/// <para>
/// This extends <see cref="ClaimRequiredAttribute"/> to require a permission for a specific
/// resource instance, identified by its resource key. At runtime, the key template is resolved
/// against the operation's properties to produce a concrete resource path.
/// </para>
/// <para>
/// Token syntax: <c>{PropertyName}</c> or <c>{PropertyName:FallbackProperty}</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [PermissionFor("Edit", "Tenant/{TenantId}/Document/{DocumentId}")]
/// [SelfLink(typeof(DocumentApiResource), "/tenants/{TenantId}/documents/{DocumentId}", HttpMethod.Put)]
/// public partial class UpdateDocumentCommand : ICommand&lt;ResourceEvent&lt;DocumentApiResource&gt;&gt;
/// {
///     public string TenantId { get; set; }
///     public string DocumentId { get; set; }
/// }
/// </code>
/// </example>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class PermissionForAttribute : ClaimRequiredAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionForAttribute"/> class.
    /// </summary>
    /// <param name="permission">The permission name (e.g. "Edit", "View", "Delete").</param>
    /// <param name="resourceKeyTemplate">
    /// A template for the resource key, e.g. <c>"Tenant/{TenantId}/Document/{DocumentId}"</c>.
    /// Tokens are replaced with property values from the operation at runtime.
    /// </param>
    public PermissionForAttribute(string permission, string resourceKeyTemplate)
        : base(Runtime.Authorization.ClaimTypes.Permission, resourceKeyTemplate, permission)
    {
        this.Permission = permission;
    }

    /// <summary>
    /// Gets the permission name.
    /// </summary>
    public string Permission { get; }
}
