using System.Diagnostics;
using System.Runtime.CompilerServices;

using JetBrains.Annotations;

namespace Blueprint;

/// <summary>
/// Argument validation helpers that throw standard <see cref="ArgumentException"/> subtypes
/// when preconditions are not met. Adds richer comparisons and collection checks beyond
/// what the built-in <c>ArgumentNullException.ThrowIfNull</c> family provides.
/// </summary>
[DebuggerStepThrough]
[PublicAPI]
public static class Guard
{
    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> when the value is <c>null</c>.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void NotNull(
        [NoEnumeration] object? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the string is <c>null</c> or empty.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void NotNullOrEmpty(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the string is <c>null</c>,
    /// empty, or consists only of white-space characters.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the value is not a defined member of
    /// the enum type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The enum type to validate against.</typeparam>
    /// <param name="value">The enum value to check.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void EnumDefined<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException(
                $"Value must be a defined member of {typeof(T).Name}. Was '{value}'.",
                paramName);
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the value is less than the minimum.
    /// </summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="minimum">The inclusive lower bound.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void GreaterThanOrEqual<T>(
        T value,
        T minimum,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(minimum) < 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be greater than or equal to {minimum}.");
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the value is less than or equal
    /// to the minimum.
    /// </summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="minimum">The exclusive lower bound.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void GreaterThan<T>(
        T value,
        T minimum,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(minimum) <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be greater than {minimum}.");
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the value is greater than the maximum.
    /// </summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="maximum">The inclusive upper bound.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void LessThanOrEqual<T>(
        T value,
        T maximum,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be less than or equal to {maximum}.");
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> when the value is greater than or equal
    /// to the maximum.
    /// </summary>
    /// <typeparam name="T">A comparable type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="maximum">The exclusive upper bound.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void LessThan<T>(
        T value,
        T maximum,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(maximum) >= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Value must be less than {maximum}.");
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the collection contains no elements.
    /// </summary>
    /// <typeparam name="T">The element type of the collection.</typeparam>
    /// <param name="value">The collection to check.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void NotEmpty<T>(
        IEnumerable<T> value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);

        using var enumerator = value.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("Collection must contain at least one element.", paramName);
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the condition is <c>false</c>.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="message">The error message when the condition is not met.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void IsTrue(
        bool condition,
        string message,
        [CallerArgumentExpression(nameof(condition))] string? paramName = null)
    {
        if (!condition)
        {
            throw new ArgumentException(message, paramName);
        }
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the condition is <c>true</c>.
    /// </summary>
    /// <param name="condition">The condition to evaluate.</param>
    /// <param name="message">The error message when the condition is met.</param>
    /// <param name="paramName">
    /// The parameter name, automatically inferred via <see cref="CallerArgumentExpressionAttribute"/>.
    /// </param>
    public static void IsFalse(
        bool condition,
        string message,
        [CallerArgumentExpression(nameof(condition))] string? paramName = null)
    {
        if (condition)
        {
            throw new ArgumentException(message, paramName);
        }
    }
}
