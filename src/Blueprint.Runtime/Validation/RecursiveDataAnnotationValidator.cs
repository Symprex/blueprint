using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Blueprint.Validation;

/// <summary>
/// Provides utility methods to validate nested complex types using DataAnnotations recursively.
/// The generated validation pipeline calls these methods for properties whose types carry their
/// own validation attributes (e.g. child objects or lists of child objects).
/// </summary>
public static class RecursiveDataAnnotationValidator
{
    /// <summary>
    /// Caches the public instance properties for each validated type to avoid repeated reflection allocations.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    /// <summary>
    /// Validates each item in a child list using DataAnnotations, prepending indexed path prefixes
    /// to each error's member names for precise client-side error binding.
    /// </summary>
    /// <typeparam name="T">The type of the child list items.</typeparam>
    /// <param name="propertyName">The name of the property on the parent operation.</param>
    /// <param name="values">The values to validate; <c>null</c> produces no errors.</param>
    /// <param name="validationContext">The validation context from the parent object.</param>
    /// <param name="maxDepth">Maximum recursion depth for nested complex types.</param>
    /// <returns>Validation results with fully-qualified member path names.</returns>
    [RequiresUnreferencedCode("DataAnnotations validation uses reflection to read attributes.")]
    public static IEnumerable<ValidationResult> ValidateChildList<T>(
        string propertyName,
        IReadOnlyList<T>? values,
        ValidationContext validationContext,
        int maxDepth = 5)
    {
        if (values == null)
        {
            yield break;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var propertyPath = ToCamelCase(propertyName);

        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];

            if (value == null)
            {
                continue;
            }

            foreach (var result in ValidateObject(value, $"{propertyPath}[{i}]", validationContext, maxDepth, visited))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Validates a child object using DataAnnotations, prepending the property path to each
    /// error's member names for precise client-side error binding.
    /// </summary>
    /// <typeparam name="T">The type of the child object.</typeparam>
    /// <param name="propertyName">The name of the property on the parent operation.</param>
    /// <param name="value">The value to validate; <c>null</c> produces no errors.</param>
    /// <param name="validationContext">The validation context from the parent object.</param>
    /// <param name="maxDepth">Maximum recursion depth for nested complex types.</param>
    /// <returns>Validation results with fully-qualified member path names.</returns>
    [RequiresUnreferencedCode("DataAnnotations validation uses reflection to read attributes.")]
    public static IEnumerable<ValidationResult> ValidateChildObject<T>(
        string propertyName,
        T? value,
        ValidationContext validationContext,
        int maxDepth = 5)
    {
        if (value == null)
        {
            yield break;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var propertyPath = ToCamelCase(propertyName);

        foreach (var result in ValidateObject(value, propertyPath, validationContext, maxDepth, visited))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Validates a child object and appends any errors to the provided results list, creating it if needed.
    /// Designed for use by generated pipeline code where the list is lazily allocated.
    /// </summary>
    /// <typeparam name="T">The type of the child object.</typeparam>
    /// <param name="propertyName">The name of the property on the parent operation.</param>
    /// <param name="value">The value to validate; <c>null</c> produces no errors.</param>
    /// <param name="validationContext">The validation context from the parent object.</param>
    /// <param name="results">The existing results list (may be <c>null</c>); created on first error.</param>
    /// <returns>The results list, possibly newly allocated, or <c>null</c> if no errors were found.</returns>
    [RequiresUnreferencedCode("DataAnnotations validation uses reflection to read attributes.")]
    public static List<ValidationResult>? AppendChildObjectErrors<T>(
        string propertyName,
        T? value,
        ValidationContext validationContext,
        List<ValidationResult>? results)
    {
        if (value == null)
        {
            return results;
        }

        foreach (var error in ValidateChildObject(propertyName, value, validationContext))
        {
            results ??= new List<ValidationResult>();
            results.Add(error);
        }

        return results;
    }

    /// <summary>
    /// Validates a child list and appends any errors to the provided results list, creating it if needed.
    /// Designed for use by generated pipeline code where the list is lazily allocated.
    /// </summary>
    /// <typeparam name="T">The type of the list element.</typeparam>
    /// <param name="propertyName">The name of the property on the parent operation.</param>
    /// <param name="values">The values to validate; <c>null</c> produces no errors.</param>
    /// <param name="validationContext">The validation context from the parent object.</param>
    /// <param name="results">The existing results list (may be <c>null</c>); created on first error.</param>
    /// <returns>The results list, possibly newly allocated, or <c>null</c> if no errors were found.</returns>
    [RequiresUnreferencedCode("DataAnnotations validation uses reflection to read attributes.")]
    public static List<ValidationResult>? AppendChildListErrors<T>(
        string propertyName,
        IReadOnlyList<T>? values,
        ValidationContext validationContext,
        List<ValidationResult>? results)
    {
        if (values == null)
        {
            return results;
        }

        foreach (var error in ValidateChildList(propertyName, values, validationContext))
        {
            results ??= new List<ValidationResult>();
            results.Add(error);
        }

        return results;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Callers propagate RequiresUnreferencedCode.")]
    private static IEnumerable<ValidationResult> ValidateObject(
        object value,
        string path,
        ValidationContext parentContext,
        int remainingDepth,
        HashSet<object> visited)
    {
        if (remainingDepth <= 0 || !visited.Add(value))
        {
            yield break;
        }

        var context = new ValidationContext(value, parentContext, parentContext.Items);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, context, results, validateAllProperties: true);

        foreach (var result in results)
        {
            if (result == ValidationResult.Success)
            {
                continue;
            }

            var memberNames = result.MemberNames.ToArray();

            if (memberNames.Length == 0)
            {
                yield return new ValidationResult(result.ErrorMessage, [path]);
            }
            else
            {
                yield return new ValidationResult(
                    result.ErrorMessage,
                    memberNames.Select(n => string.Concat(path, ".", ToCamelCasePath(n))));
            }
        }

        if (remainingDepth == 1)
        {
            yield break;
        }

        var valueType = value.GetType();

#pragma warning disable IL2070 // The property cache is populated from types discovered at runtime during recursive validation
        var properties = _propertyCache.GetOrAdd(valueType, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
#pragma warning restore IL2070

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead)
            {
                continue;
            }

            var childValue = property.GetValue(value);

            if (childValue == null)
            {
                continue;
            }

            var propertyType = property.PropertyType;
            var childPath = string.Concat(path, ".", ToCamelCase(property.Name));

            if (IsEnumerableType(propertyType))
            {
                var index = 0;

                foreach (var item in (IEnumerable)childValue)
                {
                    if (item != null && IsValidatableComplexType(item.GetType()))
                    {
                        foreach (var result in ValidateObject(
                            item,
                            string.Concat(childPath, "[", index.ToString(), "]"),
                            parentContext,
                            remainingDepth - 1,
                            visited))
                        {
                            yield return result;
                        }
                    }

                    index++;
                }
            }
            else if (IsValidatableComplexType(propertyType))
            {
                foreach (var result in ValidateObject(
                    childValue,
                    childPath,
                    parentContext,
                    remainingDepth - 1,
                    visited))
                {
                    yield return result;
                }
            }
        }
    }

    private static bool IsEnumerableType(Type type)
    {
        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static bool IsValidatableComplexType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsPrimitive || underlying.IsEnum)
        {
            return false;
        }

        if (underlying == typeof(string) || underlying == typeof(decimal) ||
            underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) ||
            underlying == typeof(TimeSpan) || underlying == typeof(Guid) || underlying == typeof(Uri))
        {
            return false;
        }

        var ns = underlying.Namespace;

        if (!string.IsNullOrEmpty(ns) && (
                ns.StartsWith("System", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft", StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
    }

    private static string ToCamelCasePath(string memberName)
    {
        return string.Join(".", memberName.Split('.').Select(ToCamelCase));
    }
}
