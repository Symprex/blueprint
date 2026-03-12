using System.ComponentModel.DataAnnotations;

namespace Blueprint.Middleware;

/// <summary>
/// Helper methods for validation middleware.
/// </summary>
public static class ValidationMiddlewareBuilder
{
    /// <summary>
    /// Converts a <see cref="ValidationException"/> to a <see cref="ValidationFailedOperationResult"/>.
    /// </summary>
    /// <param name="exception">The validation exception.</param>
    /// <returns>A <see cref="ValidationFailedOperationResult"/>.</returns>
    public static ValidationFailedOperationResult ToValidationFailedOperationResult(ValidationException exception)
    {
        var validationResults = new[]
        {
            new ValidationResult(exception.Message, exception.ValidationResult?.MemberNames),
        };

        return new ValidationFailedOperationResult(validationResults);
    }
}
