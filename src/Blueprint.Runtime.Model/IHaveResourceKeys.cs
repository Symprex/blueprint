namespace Blueprint.Model;

/// <summary>
/// Marks a resource as having multiple resource keys, enabling hierarchical permission matching.
/// For example, a User resource might have keys like <c>"/Tenants/{TenantId}/Users/{UserId}"</c>
/// that map to different levels of the resource hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// When a resource implements this interface, the claims authorization system can use
/// its keys for pre-expansion during permission checks, avoiding redundant calls to
/// resource key expanders.
/// </para>
/// <para>
/// This extends the single-key <see cref="IHaveResourceKey"/> pattern for resources that
/// participate in multiple hierarchies or have composite identity.
/// </para>
/// </remarks>
public interface IHaveResourceKeys
{
    /// <summary>
    /// Gets the set of resource keys that identify this resource within permission hierarchies.
    /// </summary>
    IEnumerable<string> ResourceKeys { get; }
}
