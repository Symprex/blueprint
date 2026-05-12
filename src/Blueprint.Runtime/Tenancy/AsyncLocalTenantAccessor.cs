namespace Blueprint.Tenancy;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ITenantAccessor"/> that
/// provides ambient tenant context across async boundaries. Register as a singleton since
/// the state is stored per async context, not per instance.
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
