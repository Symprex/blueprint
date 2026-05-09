using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="Type"/> that simplify working with generic
/// type hierarchies at runtime.
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// Returns the full type hierarchy of a type: the type itself, all implemented
    /// interfaces, and all base types up to <see cref="object"/>.
    /// </summary>
    /// <param name="type">The type whose hierarchy to enumerate.</param>
    /// <returns>An enumerable of all types in the hierarchy.</returns>
    public static IEnumerable<Type> GetTypeHierarchy(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type type)
    {
        yield return type;

        if (type.BaseType == null)
        {
            yield break;
        }

        foreach (var i in type.GetInterfaces())
        {
            yield return i;
        }

        var currentBaseType = type.BaseType;

        while (currentBaseType != null)
        {
            yield return currentBaseType;
            currentBaseType = currentBaseType.BaseType;
        }
    }

    /// <summary>
    /// Checks whether the given type is a generic type with a single type argument
    /// matching the specified generic type definition.
    /// </summary>
    /// <param name="typeToCheck">The type to check.</param>
    /// <param name="genericType">The generic type definition to check against.</param>
    /// <param name="typeArgument">The single type argument if matched; otherwise <c>null</c>.</param>
    /// <returns><c>true</c> if the type matches with one generic argument.</returns>
    public static bool IsGenericTypeWithOneArgument(
        this Type typeToCheck,
        Type genericType,
        [NotNullWhen(true)] out Type? typeArgument)
    {
        if (!typeToCheck.IsGenericType)
        {
            typeArgument = null;
            return false;
        }

        var typeDefinition = typeToCheck.GetGenericTypeDefinition();

        if (typeDefinition != genericType)
        {
            typeArgument = null;
            return false;
        }

        typeArgument = typeToCheck.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    /// Checks whether this <see cref="Type"/> is assignable to the given generic type definition.
    /// </summary>
    /// <param name="typeToCheck">The concrete type to check (e.g. <c>List&lt;string&gt;</c>).</param>
    /// <param name="genericType">The generic type definition (e.g. <c>List&lt;&gt;</c>).</param>
    /// <returns><c>true</c> if the type matches the generic definition.</returns>
    public static bool IsOfGenericType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type typeToCheck,
        Type genericType)
    {
        return typeToCheck.IsOfGenericType(genericType, out _);
    }

    /// <summary>
    /// Checks whether this <see cref="Type"/> is assignable to the given generic type definition,
    /// returning the concrete generic type that matched.
    /// </summary>
    /// <param name="typeToCheck">The concrete type to check (e.g. <c>List&lt;string&gt;</c>).</param>
    /// <param name="genericType">The generic type definition (e.g. <c>List&lt;&gt;</c>).</param>
    /// <param name="concreteGenericType">Set to the actual concrete type found when matched.</param>
    /// <returns><c>true</c> if the type matches the generic definition.</returns>
    public static bool IsOfGenericType(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] this Type typeToCheck,
        Type genericType,
        [NotNullWhen(true)] out Type? concreteGenericType)
    {
        ArgumentNullException.ThrowIfNull(genericType);

        if (!genericType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                "The definition needs to be a GenericTypeDefinition (i.e typeof(List<>))",
                nameof(genericType));
        }

        while (true)
        {
            concreteGenericType = null;

            if (typeToCheck == typeof(object))
            {
                return false;
            }

            if (typeToCheck == genericType)
            {
                concreteGenericType = typeToCheck;
                return true;
            }

            if ((typeToCheck.IsGenericType ? typeToCheck.GetGenericTypeDefinition() : typeToCheck) == genericType)
            {
                concreteGenericType = typeToCheck;
                return true;
            }

            if (genericType.IsInterface)
            {
#pragma warning disable IL2072 // Interfaces from GetInterfaces() may not have trimming annotations
                foreach (var i in typeToCheck.GetInterfaces())
                {
                    if (i.IsOfGenericType(genericType, out concreteGenericType))
                    {
                        return true;
                    }
                }
#pragma warning restore IL2072
            }

            typeToCheck = typeToCheck.BaseType!;

            if (typeToCheck == null)
            {
                return false;
            }
        }
    }
}
