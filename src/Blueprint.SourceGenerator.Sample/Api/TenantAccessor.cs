namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Abstraction for accessing the current tenant identifier within the request scope.
/// Demonstrates the same pattern as Signature365's <c>ICurrentTenantAccessor</c>,
/// which uses <see cref="AsyncLocal{T}"/> for ambient tenant context that survives
/// async/await boundaries.
/// </summary>
/// <remarks>
/// <para>
/// In Signature365, the <c>SetTenantMiddleware</c> extracts the tenant ID from a JWT claim
/// (<c>Sig365TenantId</c>) and sets it on this accessor. Operations can then inject
/// <c>ICurrentTenantAccessor</c> to read the current tenant without needing to parse
/// claims themselves. The middleware also auto-populates operation properties named
/// <c>TenantId</c> from the claim value.
/// </para>
/// <para>
/// In the new Blueprint, this pattern is implemented through:
/// <list type="number">
///   <item>Register <see cref="ITenantAccessor"/> as a singleton (uses AsyncLocal internally)</item>
///   <item>Tenant middleware or pipeline contributor sets the value from JWT claims</item>
///   <item>Handlers inject <see cref="ITenantAccessor"/> to read the current tenant</item>
///   <item>The resource authorization provider can also read it for ownership checks</item>
/// </list>
/// </para>
/// </remarks>
public interface ITenantAccessor
{
    /// <summary>
    /// Gets or sets the current tenant identifier for the executing operation scope.
    /// </summary>
    string? Current { get; set; }
}

/// <summary>
/// AsyncLocal-backed implementation of <see cref="ITenantAccessor"/> that provides ambient
/// tenant context across async boundaries. Registered as a singleton since the state is
/// stored in <see cref="AsyncLocal{T}"/> (thread/context-local, not instance-local).
/// </summary>
public sealed class AsyncLocalTenantAccessor : ITenantAccessor
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
