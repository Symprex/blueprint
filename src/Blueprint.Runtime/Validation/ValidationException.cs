using System.ComponentModel.DataAnnotations;

namespace Blueprint.Validation;

/// <summary>
/// Exception thrown when validation fails.
/// Retained as a compatibility surface for legacy callers; generated validation now short-circuits
/// to <c>ValidationFailedOperationResult</c> instead of throwing this type.
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="validationResults">The validation results.</param>
    public ValidationException(IEnumerable<ValidationResult> validationResults)
        : base("Validation failed")
    {
        this.ValidationResults = validationResults.ToList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="validationResults">The validation results.</param>
    public ValidationException(string message, IEnumerable<ValidationResult> validationResults)
        : base(message)
    {
        this.ValidationResults = validationResults.ToList();
    }

    /// <summary>
    /// Gets the validation results.
    /// </summary>
    public List<ValidationResult> ValidationResults { get; }
}
