namespace Blueprint.Tenancy;

/// <summary>
/// Provides ambient access to the current tenant identifier within the request scope.
/// Register as a singleton — state is stored in <see cref="AsyncLocal{T}"/> (context-local,
/// not instance-local) so it survives async/await boundaries.
/// </summary>
/// <remarks>
/// This is the Blueprint-native equivalent of Signature365's <c>ICurrentTenantAccessor</c>.
/// The generated pipeline's <c>TenantModule</c> sets <see cref="Current"/> during the
/// Population stage from a value resolved by <see cref="ITenantResolver"/>. Handlers and
/// services inject <see cref="ITenantAccessor"/> to read the current tenant without parsing
/// claims themselves.
/// </remarks>
public interface ITenantAccessor
{
    /// <summary>
    /// Gets or sets the current tenant identifier for the executing operation scope.
    /// Set by the generated pipeline; read by handlers, services, and authorization providers.
    /// </summary>
    string? Current { get; set; }
}
