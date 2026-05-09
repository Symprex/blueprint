namespace Blueprint.Utilities;

/// <summary>
/// Utility methods for reflection operations including nullable type
/// unwrapping and human-readable type name formatting.
/// </summary>
public static class ReflectionUtilities
{
    /// <summary>
    /// If the given type is <see cref="Nullable{T}"/> returns the underlying type;
    /// otherwise returns the input unchanged.
    /// </summary>
    /// <param name="type">The type to potentially unwrap.</param>
    /// <returns>The underlying type or the original type.</returns>
    public static Type GetUnderlyingTypeIfNullable(Type type)
    {
        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <summary>
    /// Produces a human-readable type name with actual generic argument names
    /// instead of the CLR arity notation (e.g. <c>IGeneric`1</c> → <c>IGeneric&lt;String&gt;</c>).
    /// </summary>
    /// <param name="t">The type to format.</param>
    /// <returns>A readable type name.</returns>
    public static string PrettyTypeName(Type t)
    {
        if (t.IsArray)
        {
            return PrettyTypeName(t.GetElementType()!) + "[]";
        }

        if (t.IsGenericType)
        {
            var baseGenericName = t.Name[..t.Name.LastIndexOf('`')];
            var arguments = string.Join(", ", t.GetGenericArguments().Select(PrettyTypeName));

            return $"{baseGenericName}<{arguments}>";
        }

        return t.Name;
    }
}
