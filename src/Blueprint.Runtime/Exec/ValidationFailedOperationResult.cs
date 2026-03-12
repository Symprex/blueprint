using System.ComponentModel.DataAnnotations;
using Blueprint.Runtime.Exec;

namespace Blueprint.Middleware;

/// <summary>
/// An <see cref="OperationResult"/> that represents validation failures.
/// </summary>
public class ValidationFailedOperationResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedOperationResult"/> class.
    /// </summary>
    /// <param name="validationResults">The validation results.</param>
    public ValidationFailedOperationResult(IEnumerable<ValidationResult> validationResults)
    {
        this.ValidationResults = validationResults.ToList();
    }

    /// <summary>
    /// Gets the validation results.
    /// </summary>
    public List<ValidationResult> ValidationResults { get; }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        // In a real implementation, this would write validation errors to the response
        // For now, just complete
        return Task.CompletedTask;
    }
}
