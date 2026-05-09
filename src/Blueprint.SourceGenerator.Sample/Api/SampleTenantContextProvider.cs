using Blueprint.Tasks;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates <see cref="IBackgroundTaskContextProvider"/> by capturing and restoring
/// a tenant ID from a hypothetical <c>ITenantAccessor</c> ambient service.
/// </summary>
/// <remarks>
/// In Signature365, this pattern ensures background tasks execute in the same tenant scope
/// as the original request. The tenant ID stored in the envelope travels through Hangfire
/// serialization and is restored before the handler runs.
/// </remarks>
public sealed class SampleTenantContextProvider : IBackgroundTaskContextProvider
{
    private const string TenantIdKey = "TenantId";
    private const string UserIdKey = "UserId";

    /// <inheritdoc />
    public IDictionary<string, string>? CaptureContext()
    {
        // In a real app, this would read from ITenantAccessor or HttpContext.
        // For the sample, we demonstrate the shape with a hardcoded example.
        return new Dictionary<string, string>
        {
            [TenantIdKey] = "sample-tenant-001",
            [UserIdKey] = "sample-user-42",
        };
    }

    /// <inheritdoc />
    public void RestoreContext(IDictionary<string, string> context, IServiceProvider serviceProvider)
    {
        // In a real app, this would call ITenantAccessor.SetCurrentTenant(context["TenantId"])
        // or similar to establish the tenant scope for the background task's DI scope.
        if (context.TryGetValue(TenantIdKey, out var tenantId))
        {
            // Restore tenant context — in production this calls a tenant accessor or sets HttpContext items.
            _ = tenantId;
        }

        if (context.TryGetValue(UserIdKey, out var userId))
        {
            _ = userId;
        }
    }
}
