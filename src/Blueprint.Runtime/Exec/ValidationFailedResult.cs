using System.ComponentModel.DataAnnotations;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Represents a canonical semantic validation-failure outcome while broader host translation and exception-model
/// handling continue to migrate away from legacy compatibility surfaces.
/// </summary>
public class ValidationFailedResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedResult"/> class.
    /// </summary>
    /// <param name="validationResults">The validation results that describe the input-contract failure.</param>
    public ValidationFailedResult(IEnumerable<ValidationResult> validationResults)
    {
        ArgumentNullException.ThrowIfNull(validationResults);

        this.ValidationResults = validationResults.ToList();
    }

    /// <summary>
    /// Gets the validation results that explain the failure.
    /// </summary>
    public List<ValidationResult> ValidationResults { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}
