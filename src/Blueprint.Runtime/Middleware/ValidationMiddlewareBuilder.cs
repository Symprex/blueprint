using System.ComponentModel.DataAnnotations;

namespace Blueprint.Middleware;

/// <summary>
/// Helper methods for validation middleware.
/// </summary>
public static class ValidationMiddlewareBuilder
{
    /// <summary>
    /// Converts a <see cref="ValidationException"/> to the canonical <see cref="Blueprint.Runtime.Exec.ValidationFailedResult"/>.
    /// </summary>
    /// <param name="exception">The validation exception.</param>
    /// <returns>A <see cref="Blueprint.Runtime.Exec.ValidationFailedResult"/>.</returns>
    public static Blueprint.Runtime.Exec.ValidationFailedResult ToValidationFailedResult(ValidationException exception)
    {
        // A nested member's result keeps the path of the members' own names, which the HTTP host names its 422 key from.
        ValidationResult[] validationResults =
        [
            exception.ValidationResult is Blueprint.Validation.MemberPathValidationResult nested
                ? new Blueprint.Validation.MemberPathValidationResult(exception.Message, nested.MemberNames.First(), nested.MemberPath)
                : new ValidationResult(exception.Message, exception.ValidationResult?.MemberNames),
        ];

        return new Blueprint.Runtime.Exec.ValidationFailedResult(validationResults);
    }

    /// <summary>
    /// Converts a <see cref="ValidationException"/> to the legacy compatibility alias for validation failures.
    /// </summary>
    /// <param name="exception">The validation exception.</param>
    /// <returns>A <see cref="ValidationFailedOperationResult"/>.</returns>
    public static ValidationFailedOperationResult ToValidationFailedOperationResult(ValidationException exception)
    {
        var canonicalResult = ToValidationFailedResult(exception);

        return new ValidationFailedOperationResult(canonicalResult.ValidationResults);
    }
}
