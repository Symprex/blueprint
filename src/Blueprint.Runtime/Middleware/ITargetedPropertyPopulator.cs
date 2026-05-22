using Blueprint.Model;

namespace Blueprint.Runtime.Middleware;

/// <summary>
/// A specialization of <see cref="IPropertyPopulator"/> that declares which operations it applies to.
/// Implementing this interface enables the runtime to pre-filter populators per-pipeline on first use,
/// eliminating unnecessary <see cref="IPropertyPopulator.PopulateAsync"/> calls for operations that
/// don't need this populator's behavior.
/// <para>
/// The <see cref="AppliesTo"/> result is deterministic per descriptor and is cached after the first
/// request to each pipeline. Populators that apply to ALL operations should implement
/// <see cref="IPropertyPopulator"/> directly (no filtering overhead since the array is shared).
/// </para>
/// </summary>
/// <remarks>
/// In S365, <c>TenantPropertyPopulator</c> could implement this interface and return <c>true</c>
/// only for operations that have a <c>TenantId</c> property, skipping all tenant-less operations
/// (e.g., health checks, login) entirely.
/// </remarks>
public interface ITargetedPropertyPopulator : IPropertyPopulator
{
    /// <summary>
    /// Determines whether this populator should run for the given operation.
    /// </summary>
    /// <param name="descriptor">The operation descriptor to check.</param>
    /// <returns><see langword="true"/> if this populator should be invoked during the operation's Population stage.</returns>
    bool AppliesTo(ApiOperationDescriptor descriptor);
}
