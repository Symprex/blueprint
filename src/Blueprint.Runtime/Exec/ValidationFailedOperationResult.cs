using System.ComponentModel.DataAnnotations;
using Blueprint.Runtime.Exec;

namespace Blueprint.Middleware;

/// <summary>
/// Compatibility alias for legacy callers that still refer to the pre-canonical validation-failure result name.
/// </summary>
public sealed class ValidationFailedOperationResult : ValidationFailedResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedOperationResult"/> class.
    /// </summary>
    /// <param name="validationResults">The validation results.</param>
    public ValidationFailedOperationResult(IEnumerable<ValidationResult> validationResults)
        : base(validationResults)
    {
    }
}
