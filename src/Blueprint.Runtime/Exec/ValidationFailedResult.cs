using System.ComponentModel.DataAnnotations;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a canonical semantic validation-failure outcome that carries either
/// <see cref="ValidationResult"/> instances (DataAnnotations path) or a pre-built
/// <see cref="Errors"/> dictionary (property → messages), or both. The HTTP executor
/// prefers the <see cref="Errors"/> dictionary when present, falling back to expanding
/// <see cref="ValidationResults"/>.
/// </summary>
public class ValidationFailedResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedResult"/> class
    /// from DataAnnotations <see cref="ValidationResult"/> instances.
    /// </summary>
    /// <param name="validationResults">The validation results that describe the input-contract failure.</param>
    public ValidationFailedResult(IEnumerable<ValidationResult> validationResults)
    {
        ArgumentNullException.ThrowIfNull(validationResults);

        this.ValidationResults = validationResults as List<ValidationResult> ?? validationResults.ToList();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedResult"/> class
    /// from a <see cref="Blueprint.Validation.ValidationException"/> that may carry
    /// either an <see cref="Blueprint.Validation.ValidationException.Errors"/> dictionary,
    /// <see cref="Blueprint.Validation.ValidationException.ValidationResults"/>, or both.
    /// </summary>
    /// <param name="validationException">The validation exception to convert.</param>
    public ValidationFailedResult(Validation.ValidationException validationException)
    {
        ArgumentNullException.ThrowIfNull(validationException);

        this.ValidationResults = validationException.ValidationResults;
        this.Errors = validationException.Errors;
    }

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <summary>
    /// Gets the DataAnnotations validation results, if available. May be empty when
    /// the result was constructed from an error dictionary.
    /// </summary>
    public List<ValidationResult> ValidationResults { get; } = [];

    /// <summary>
    /// Gets the pre-built property → messages error dictionary, if available. When non-null
    /// the HTTP executor uses this directly instead of expanding <see cref="ValidationResults"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}
