using Blueprint.Model;
using Blueprint.Tenancy;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Demonstrates <see cref="IContextMetadataProvider"/> by enriching resource events
/// with tenant context from the ambient <see cref="ITenantAccessor"/>.
/// In a real application this would populate correlation IDs, tenant info, etc.
/// </summary>
public sealed class SampleContextMetadataProvider(ITenantAccessor tenantAccessor) : IContextMetadataProvider
{
    /// <inheritdoc />
    public void PopulateMetadata(Action<string, string> add)
    {
        var tenantId = tenantAccessor.Current;

        if (!string.IsNullOrEmpty(tenantId))
        {
            add("tenant.id", tenantId);
        }
    }
}
