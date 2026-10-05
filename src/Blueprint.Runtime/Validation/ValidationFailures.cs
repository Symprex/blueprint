using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;

using JetBrains.Annotations;

namespace Blueprint.Validation;

/// <summary>
/// A mutable builder for collecting validation failures before throwing a
/// <see cref="ValidationException"/> or returning a validation-failed result.
/// </summary>
/// <remarks>
/// <para>
/// This type defers dictionary allocation until the first failure is recorded, on the
/// assumption that most operations pass validation. Call <see cref="AddFailure(string, string)"/>
/// or <see cref="AddFailure(ValidationResult)"/> to accumulate errors, then inspect
/// <see cref="HasFailures"/> and convert via <see cref="AsDictionary"/> or
/// <see cref="ToException"/>.
/// </para>
/// <para>
/// The static <see cref="None"/> instance represents a permanently empty collection and
/// must not be mutated.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ValidationFailures
{
    private static readonly IReadOnlyDictionary<string, string[]> _noFailures =
        FrozenDictionary<string, string[]>.Empty;

    /// <summary>
    /// The property key used for form-level (non-property-specific) validation messages: <c>form</c>, as legacy
    /// Blueprint's <c>ValidationFailures.FormLevelPropertyName</c> was (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/Validation/ValidationFailures.cs:14-18</c>).
    /// </summary>
    public const string FormLevelPropertyName = "form";

    /// <summary>
    /// A shared empty instance representing no failures. Must not be mutated.
    /// </summary>
    public static readonly ValidationFailures None = new();

    private Dictionary<string, List<string>>? _failures;

    /// <summary>
    /// Gets the number of distinct properties that have recorded failures.
    /// </summary>
    public int Count => this._failures?.Count ?? 0;

    /// <summary>
    /// Gets a value indicating whether any failures have been recorded.
    /// </summary>
    public bool HasFailures => this._failures is { Count: > 0 };

    /// <summary>
    /// Records a <see cref="ValidationResult"/> from DataAnnotations validation.
    /// If the result has no <see cref="ValidationResult.MemberNames"/>, the error
    /// is recorded as a form-level failure.
    /// </summary>
    /// <param name="validationResult">The DataAnnotations validation result to record.</param>
    public void AddFailure(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var hasMembers = false;

        foreach (var memberName in validationResult.MemberNames)
        {
            hasMembers = true;
            this.AddFailure(memberName, validationResult.ErrorMessage ?? "Validation failed");
        }

        if (!hasMembers)
        {
            this.AddFailure(FormLevelPropertyName, validationResult.ErrorMessage ?? "Validation failed");
        }
    }

    /// <summary>
    /// Records a validation failure for a specific property.
    /// </summary>
    /// <param name="propertyName">
    /// The property name this failure applies to. Use <see cref="FormLevelPropertyName"/>
    /// for form-level errors.
    /// </param>
    /// <param name="errorMessage">The human-readable error message.</param>
    public void AddFailure(string propertyName, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(errorMessage);

        this._failures ??= new Dictionary<string, List<string>>();

        if (!this._failures.TryGetValue(propertyName, out var errors))
        {
            errors = new List<string>(2);
            this._failures[propertyName] = errors;
        }

        errors.Add(errorMessage);
    }

    /// <summary>
    /// Returns this collection as a read-only dictionary of property names to their error messages.
    /// </summary>
    /// <returns>
    /// A dictionary keyed by property name (<see cref="FormLevelPropertyName"/> for form-level errors) with arrays
    /// of error messages. Returns a shared empty dictionary when no failures exist.
    /// </returns>
    public IReadOnlyDictionary<string, string[]> AsDictionary()
    {
        if (this._failures is null or { Count: 0 })
        {
            return _noFailures;
        }

        var result = new Dictionary<string, string[]>(this._failures.Count);

        foreach (var kvp in this._failures)
        {
            result[kvp.Key] = kvp.Value.ToArray();
        }

        return result;
    }

    /// <summary>
    /// Converts this collection into a <see cref="ValidationException"/> if any failures
    /// have been recorded.
    /// </summary>
    /// <returns>
    /// A <see cref="ValidationException"/> containing all recorded failures, or <c>null</c>
    /// if no failures exist.
    /// </returns>
    public ValidationException? ToException()
    {
        return this.HasFailures ? new ValidationException(this.AsDictionary()) : null;
    }

    /// <summary>
    /// Throws a <see cref="ValidationException"/> if any failures have been recorded.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when <see cref="HasFailures"/> is <c>true</c>.</exception>
    public void ThrowIfFailed()
    {
        if (this.HasFailures)
        {
            throw new ValidationException(this.AsDictionary());
        }
    }
}
