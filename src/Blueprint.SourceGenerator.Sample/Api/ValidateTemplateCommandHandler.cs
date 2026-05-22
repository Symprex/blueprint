using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.Validation;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handler that exercises handler-level exception translation by throwing
/// <see cref="ValidationException"/>, <see cref="NotFoundException"/>, or
/// <see cref="BadRequestException"/> depending on the input values.
/// </summary>
public class ValidateTemplateCommandHandler
{
    /// <summary>
    /// Validates the template based on input values and throws appropriate exceptions
    /// to prove the generated pipeline translates them correctly.
    /// </summary>
    /// <param name="operation">The command to validate.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>A <see cref="NoResultOperationResult"/> on success.</returns>
    public Task<OperationResult> HandleAsync(ValidateTemplateCommand operation, ApiOperationContext context)
    {
        if (operation.Id == "not-found")
        {
            throw new NotFoundException($"Template '{operation.Id}' was not found");
        }

        if (operation.Name is "INVALID")
        {
            throw new ValidationException("Name", "The name INVALID is not allowed");
        }

        if (operation.Name is "BAD")
        {
            throw new BadRequestException("The template data is malformed");
        }

        return Task.FromResult<OperationResult>(NoResultOperationResult.Instance);
    }
}
