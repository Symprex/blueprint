using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Blueprint.Validation;

/// <summary>
/// Thread-safe cache for <see cref="PropertyInfo"/> lookups used by cross-property validation
/// attributes. Without caching, each validation call performs a <c>Type.GetProperty(string)</c>
/// reflection lookup — measurable overhead on hot paths with many operations.
/// </summary>
internal static class CrossPropertyAccessorCache
{
    private static readonly ConcurrentDictionary<(Type OwnerType, string PropertyName), PropertyInfo?> _cache = new();

    /// <summary>
    /// Returns the <see cref="PropertyInfo"/> for <paramref name="propertyName"/> on <paramref name="ownerType"/>,
    /// retrieving from cache or populating on first access.
    /// </summary>
    /// <param name="ownerType">The type to inspect.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>The cached <see cref="PropertyInfo"/>, or <see langword="null"/> if the property does not exist.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2080", Justification = "Blueprint validation attributes inspect preserved public operation properties.")]
    internal static PropertyInfo? GetProperty(Type ownerType, string propertyName)
    {
        return _cache.GetOrAdd((ownerType, propertyName), static key => key.OwnerType.GetProperty(key.PropertyName));
    }
}
