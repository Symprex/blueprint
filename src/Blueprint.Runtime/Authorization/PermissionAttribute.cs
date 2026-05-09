using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Declares that an operation requires the user to hold a system-wide permission.
/// This is a shorthand for <see cref="ClaimRequiredAttribute"/> with a wildcard resource key,
/// meaning the permission is not scoped to any particular resource instance.
/// </summary>
/// <remarks>
/// Usage: <c>[Permission("ManageUsers")]</c> requires the user to hold a claim of type
/// <see cref="Blueprint.Runtime.Authorization.ClaimTypes.Permission"/> with value type "ManageUsers"
/// and a wildcard value ("*").
/// </remarks>
/// <example>
/// <code>
/// [Permission("ManageUsers")]
/// [RootLink("/users", HttpMethod.Get)]
/// public partial class ListUsersQuery : IQuery&lt;UserListResource&gt;
/// {
/// }
/// </code>
/// </example>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class PermissionAttribute : ClaimRequiredAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionAttribute"/> class.
    /// </summary>
    /// <param name="permission">The permission name (e.g. "ManageUsers", "ViewReports").</param>
    public PermissionAttribute(string permission)
        : base(Runtime.Authorization.ClaimTypes.Permission, "*", permission)
    {
        this.Permission = permission;
    }

    /// <summary>
    /// Gets the permission name.
    /// </summary>
    public string Permission { get; }
}
