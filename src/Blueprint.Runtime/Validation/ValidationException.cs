using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace Blueprint.Validation;

/// <summary>
/// Exception thrown when validation fails. Supports both <see cref="ValidationResult"/>-based
/// and dictionary-based error shapes for compatibility with DataAnnotations and manual validation.
/// </summary>
/// <remarks>
/// Generated validation pipelines short-circuit to <c>ValidationFailedResult</c> without throwing.
/// This type exists for handler-level validation where throwing is more natural (e.g. calling
/// <c>throw new ValidationException("OrderBy", "Cannot order by that field")</c>).
/// </remarks>
[PublicAPI]
public class ValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class
    /// for a single form-level error message.
    /// </summary>
    /// <param name="formLevelMessage">The form-level error message.</param>
    public ValidationException(string formLevelMessage)
        : base(formLevelMessage)
    {
        this.Errors = new Dictionary<string, string[]>
        {
            [string.Empty] = [formLevelMessage],
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class
    /// for a single property validation failure.
    /// </summary>
    /// <param name="property">The property name that the failure applies to.</param>
    /// <param name="message">The validation error message.</param>
    public ValidationException(string property, string message)
        : base(message)
    {
        this.Errors = new Dictionary<string, string[]>
        {
            [property] = [message],
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class
    /// from a set of <see cref="ValidationResult"/> instances (DataAnnotations pattern).
    /// </summary>
    /// <param name="validationResults">The DataAnnotations validation results.</param>
    public ValidationException(IEnumerable<ValidationResult> validationResults)
        : base("Validation failed")
    {
        var results = validationResults.ToList();
        this.ValidationResults = results;
        this.Errors = BuildErrors(results);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class
    /// from a set of <see cref="ValidationResult"/> instances with a custom message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="validationResults">The DataAnnotations validation results.</param>
    public ValidationException(string message, IEnumerable<ValidationResult> validationResults)
        : base(message)
    {
        var results = validationResults.ToList();
        this.ValidationResults = results;
        this.Errors = BuildErrors(results);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class
    /// from a pre-built error dictionary.
    /// </summary>
    /// <param name="errors">A dictionary of property names to their error messages.</param>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base(BuildMessage(errors))
    {
        this.Errors = errors;
    }

    /// <summary>
    /// Gets the DataAnnotations validation results, if this exception was created from
    /// <see cref="ValidationResult"/> instances. May be empty for dictionary-based construction.
    /// </summary>
    public List<ValidationResult> ValidationResults { get; } = [];

    /// <summary>
    /// Gets the validation errors as a dictionary of property names to their error messages.
    /// An empty-string key represents form-level errors.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Creates a new <see cref="ValidationException"/> with the property keys renamed
    /// according to the supplied mapping. Keys not present in <paramref name="keyMappings"/>
    /// are carried over unchanged.
    /// </summary>
    /// <param name="keyMappings">A mapping from old property key → new property key.</param>
    /// <returns>A new <see cref="ValidationException"/> with the remapped keys.</returns>
    public ValidationException Remap(IDictionary<string, string> keyMappings)
    {
        var newErrors = new Dictionary<string, string[]>(this.Errors);

        foreach (var (oldKey, newKey) in keyMappings)
        {
            if (newErrors.Remove(oldKey, out var value))
            {
                newErrors[newKey] = value;
            }
        }

        return new ValidationException(newErrors);
    }

    private static Dictionary<string, string[]> BuildErrors(List<ValidationResult> results)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var result in results)
        {
            var members = result.MemberNames.Any()
                ? result.MemberNames
                : [string.Empty];

            foreach (var member in members)
            {
                if (errors.TryGetValue(member, out var existing))
                {
                    errors[member] = [.. existing, result.ErrorMessage ?? "Validation failed"];
                }
                else
                {
                    errors[member] = [result.ErrorMessage ?? "Validation failed"];
                }
            }
        }

        return errors;
    }

    private static string BuildMessage(IReadOnlyDictionary<string, string[]> errors)
    {
        return string.Join(
            Environment.NewLine,
            errors.SelectMany(
                static kvp => kvp.Value,
                static (kvp, msg) => kvp.Key.Length > 0 ? $"{kvp.Key}: {msg}" : msg));
    }
}
