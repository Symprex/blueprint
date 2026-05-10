using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Blueprint.Runtime.Utilities;

/// <summary>
/// Compiles property getters into <c>Func&lt;object, object?&gt;</c> delegates using expression trees,
/// avoiding the overhead of <see cref="PropertyInfo.GetValue(object?)"/> on every invocation.
/// Results are cached per (Type, PropertyName) pair in a thread-safe dictionary.
/// </summary>
internal static class CompiledPropertyAccessor
{
    private static readonly ConcurrentDictionary<(Type, string), Func<object, object?>?> _cache = new();

    /// <summary>
    /// Returns a compiled getter delegate for the named property on the given type, or <see langword="null"/>
    /// if the property does not exist.
    /// </summary>
    /// <param name="type">The type to look up the property on.</param>
    /// <param name="propertyName">The name of the public instance property.</param>
    /// <returns>A compiled getter delegate, or <see langword="null"/> if not found.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2080",
        Justification = "Blueprint operation and resource types are preserved; property lookups are cached for performance.")]
    internal static Func<object, object?>? GetGetter(Type type, string propertyName)
    {
        return _cache.GetOrAdd((type, propertyName), static key =>
        {
            var property = key.Item1.GetProperty(key.Item2, BindingFlags.Public | BindingFlags.Instance);

            if (property is null)
            {
                return null;
            }

            return CompileGetter(key.Item1, property);
        });
    }

    /// <summary>
    /// Returns a compiled getter delegate for the given <see cref="PropertyInfo"/>, caching by
    /// declaring type and property name.
    /// </summary>
    /// <param name="property">The property to compile a getter for.</param>
    /// <returns>A compiled getter delegate.</returns>
    internal static Func<object, object?> GetGetter(PropertyInfo property)
    {
        var key = (property.DeclaringType ?? property.ReflectedType!, property.Name);

        return _cache.GetOrAdd(key, _ => CompileGetter(key.Item1, property))!;
    }

    private static Func<object, object?> CompileGetter(Type declaringType, PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var cast = Expression.Convert(instance, declaringType);
        var access = Expression.Property(cast, property);
        var box = Expression.Convert(access, typeof(object));

        return Expression.Lambda<Func<object, object?>>(box, instance).Compile();
    }
}
