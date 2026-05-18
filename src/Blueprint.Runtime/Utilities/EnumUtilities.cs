using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Utilities;

/// <summary>
/// Utility methods for working with enum types, including description-based
/// lookup using <see cref="DescriptionAttribute"/>.
/// </summary>
public static class EnumUtilities
{
    private static readonly ConcurrentDictionary<Enum, string> _descriptionCache = new();

    /// <summary>
    /// Gets the <see cref="DescriptionAttribute"/> value for the given enum member,
    /// falling back to <see cref="Enum.ToString()"/> when no description is defined.
    /// </summary>
    /// <param name="value">The enum value to describe.</param>
    /// <returns>The description or string representation.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Enum description lookup reads public enum-member metadata for caller-supplied enum values that remain available to Blueprint runtime helpers.")]
    public static string GetDescription(this Enum value)
    {
        Guard.NotNull(value);

        return _descriptionCache.GetOrAdd(value, static v =>
        {
            var stringValue = v.ToString();
            var memberInfo = v.GetType().GetMember(stringValue);

            if (memberInfo.Length > 0)
            {
                var attrs = memberInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attrs.Length > 0 && attrs[0] is DescriptionAttribute descriptionAttribute)
                {
                    return descriptionAttribute.Description;
                }
            }

            return stringValue;
        });
    }

    /// <summary>
    /// Returns all defined values for the specified enum type.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <returns>All defined values.</returns>
    public static IEnumerable<T> GetAllItems<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>();
    }

    /// <summary>
    /// Gets descriptions for all values of the specified enum type.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <returns>Descriptions for all defined values.</returns>
    public static IEnumerable<string> GetDescriptions<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>().Select(static e => ((Enum)(object)e).GetDescription());
    }

    /// <summary>
    /// Gets descriptions for all values of the specified enum type as a dictionary,
    /// keyed by the <see cref="Enum.ToString()"/> representation.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <returns>A dictionary mapping enum names to descriptions.</returns>
    public static Dictionary<string, string> GetDescriptionsWithKeys<T>()
        where T : struct, Enum
    {
        return Enum.GetValues<T>()
            .ToDictionary(static e => e.ToString()!, static e => ((Enum)(object)e).GetDescription());
    }

    /// <summary>
    /// Gets descriptions for all values of the specified enum type.
    /// </summary>
    /// <param name="enumType">The enum type.</param>
    /// <returns>Descriptions for all defined values.</returns>
    [Obsolete("Use the generic overload GetDescriptions<T>() for AOT compatibility.")]
    [RequiresDynamicCode("Enum.GetValues(Type) cannot create arrays of arbitrary enum types at AOT compile time.")]
    public static IEnumerable<string> GetDescriptions(Type enumType)
    {
        return Enum.GetValues(enumType).Cast<Enum>().Select(static e => e.GetDescription());
    }

    /// <summary>
    /// Gets descriptions for all values of the specified enum type as a dictionary,
    /// keyed by the <see cref="Enum.ToString()"/> representation.
    /// </summary>
    /// <param name="enumType">The enum type.</param>
    /// <returns>A dictionary mapping enum names to descriptions.</returns>
    [Obsolete("Use the generic overload GetDescriptionsWithKeys<T>() for AOT compatibility.")]
    [RequiresDynamicCode("Enum.GetValues(Type) cannot create arrays of arbitrary enum types at AOT compile time.")]
    public static Dictionary<string, string> GetDescriptionsWithKeys(Type enumType)
    {
        return Enum.GetValues(enumType)
            .Cast<Enum>()
            .ToDictionary(static e => e.ToString(), static e => e.GetDescription());
    }

    /// <summary>
    /// Converts the specified string to an enum value, matching on the enum name
    /// or its <see cref="DescriptionAttribute"/> value.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="description">The string to match.</param>
    /// <param name="stringComparison">The comparison type (defaults to <see cref="StringComparison.OrdinalIgnoreCase"/>).</param>
    /// <returns>The matching enum value.</returns>
    /// <exception cref="ArgumentException">If no match is found.</exception>
    public static T FromDescription<T>(
        string description,
        StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        where T : struct, Enum
    {
        if (TryFromDescription(description, stringComparison, out T found))
        {
            return found;
        }

        throw new ArgumentException($"Could not convert '{description}' to enum '{typeof(T).Name}'");
    }

    /// <summary>
    /// Attempts to convert the specified string to an enum value using
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="description">The string to match.</param>
    /// <param name="value">The matched value if successful.</param>
    /// <returns><c>true</c> if the conversion succeeded.</returns>
    public static bool TryFromDescription<T>(string description, out T value)
        where T : struct, Enum
    {
        return TryFromDescription(description, StringComparison.OrdinalIgnoreCase, out value);
    }

    /// <summary>
    /// Attempts to convert the specified string to an enum value, matching on
    /// the enum name or its <see cref="DescriptionAttribute"/> value.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="description">The string to match.</param>
    /// <param name="stringComparison">The comparison type.</param>
    /// <param name="value">The matched value if successful.</param>
    /// <returns><c>true</c> if the conversion succeeded.</returns>
    public static bool TryFromDescription<T>(
        string description,
        StringComparison stringComparison,
        out T value)
        where T : struct, Enum
    {
        if (!string.IsNullOrEmpty(description))
        {
            foreach (var e in Enum.GetValues<T>())
            {
                var asEnum = (Enum)(object)e;

                if (e.ToString()!.Equals(description, stringComparison) ||
                    asEnum.GetDescription().Equals(description, stringComparison))
                {
                    value = e;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
