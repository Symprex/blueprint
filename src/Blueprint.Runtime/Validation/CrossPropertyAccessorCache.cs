using System.Diagnostics.CodeAnalysis;
using Blueprint.Runtime.Utilities;

namespace Blueprint.Validation;

/// <summary>
/// Thread-safe cache for compiled property getter delegates used by cross-property validation
/// attributes. Returns pre-compiled expression-tree delegates instead of raw <c>PropertyInfo</c>,
/// eliminating per-invocation <see cref="System.Reflection.PropertyInfo.GetValue(object?)"/> overhead.
/// </summary>
internal static class CrossPropertyAccessorCache
{
    /// <summary>
    /// Returns a compiled getter delegate for <paramref name="propertyName"/> on <paramref name="ownerType"/>,
    /// or <see langword="null"/> if the property does not exist. The delegate is compiled once and cached.
    /// </summary>
    /// <param name="ownerType">The type to inspect.</param>
    /// <param name="propertyName">The property name to look up.</param>
    /// <returns>A compiled getter delegate, or <see langword="null"/> if the property does not exist.</returns>
    [RequiresDynamicCode("Delegates to CompiledPropertyAccessor which uses Expression.Compile().")]
    internal static Func<object, object?>? GetGetter(Type ownerType, string propertyName)
    {
        return CompiledPropertyAccessor.GetGetter(ownerType, propertyName);
    }
}
