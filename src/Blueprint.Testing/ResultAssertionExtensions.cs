using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Testing;

/// <summary>
/// Provides assertion helper methods for <see cref="OperationResult"/> instances in test code.
/// These are framework-agnostic (no dependency on FluentAssertions or xUnit) so any test
/// framework can use them.
/// </summary>
public static class ResultAssertionExtensions
{
    /// <summary>
    /// Asserts the result is an <see cref="OkResult{T}"/> and returns the unwrapped content.
    /// Throws a descriptive exception if the result is a different type.
    /// </summary>
    /// <typeparam name="T">The expected content type.</typeparam>
    /// <param name="result">The operation result to unwrap.</param>
    /// <returns>The content value from the <see cref="OkResult{T}"/>.</returns>
    public static T ShouldBeOk<T>(this OperationResult result) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is OkResult<T> okResult)
        {
            return okResult.Content;
        }

        if (result is ValidationFailedResult validationFailed)
        {
            var errors = validationFailed.Errors is { Count: > 0 }
                ? string.Join("; ", validationFailed.Errors.Select(static e => $"[{e.Key}] {string.Join(", ", e.Value)}"))
                : "(no errors)";

            throw new BlueprintTestExecutionException(
                $"Expected OkResult<{typeof(T).Name}> but got ValidationFailedResult. Errors: {errors}",
                result);
        }

        throw new BlueprintTestExecutionException(
            $"Expected OkResult<{typeof(T).Name}> but got {result.GetType().Name}.",
            result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ResourceEvent{TResource}"/> and returns it for further assertions.
    /// Provides a descriptive error when the result is a validation failure or other type.
    /// </summary>
    /// <typeparam name="TResource">The expected resource type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <returns>The resource event result.</returns>
    public static ResourceEvent<TResource> ShouldBeResourceEvent<TResource>(this OperationResult result)
        where TResource : ApiResource
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is ResourceEvent<TResource> resourceEvent)
        {
            return resourceEvent;
        }

        if (result is ValidationFailedResult validationFailed)
        {
            string errors;

            if (validationFailed.Errors is { Count: > 0 })
            {
                errors = string.Join("; ", validationFailed.Errors.Select(static e => $"[{e.Key}] {string.Join(", ", e.Value)}"));
            }
            else if (validationFailed.ValidationResults is { Count: > 0 })
            {
                errors = string.Join("; ", validationFailed.ValidationResults.Select(static vr =>
                {
                    var members = vr.MemberNames.Any() ? string.Join(",", vr.MemberNames) : "(none)";
                    return $"[{members}] {vr.ErrorMessage}";
                }));
            }
            else
            {
                errors = "(no errors)";
            }

            throw new BlueprintTestExecutionException(
                $"Expected ResourceEvent<{typeof(TResource).Name}> but got ValidationFailedResult. Errors: {errors}",
                result);
        }

        throw new BlueprintTestExecutionException(
            $"Expected ResourceEvent<{typeof(TResource).Name}> but got {result.GetType().Name}.",
            result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ResourceEvent"/> (non-generic) and returns it.
    /// Use this when the resource type doesn't matter or for untyped assertions.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The resource event result.</returns>
    public static ResourceEvent ShouldBeResourceEvent(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as ResourceEvent
            ?? throw new BlueprintTestExecutionException(
                $"Expected ResourceEvent but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="StatusCodeResult"/> with the specified HTTP status code.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <param name="expectedStatusCode">The expected HTTP status code (e.g. 204, 202, 404).</param>
    /// <returns>The status code result for further assertions.</returns>
    public static StatusCodeResult ShouldBeStatusCode(this OperationResult result, int expectedStatusCode)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is StatusCodeResult statusCodeResult)
        {
            if (statusCodeResult.StatusCode != expectedStatusCode)
            {
                throw new BlueprintTestExecutionException(
                    $"Expected status code {expectedStatusCode} but got {statusCodeResult.StatusCode}.",
                    result);
            }

            return statusCodeResult;
        }

        throw new BlueprintTestExecutionException(
            $"Expected StatusCodeResult({expectedStatusCode}) but got {result.GetType().Name}.",
            result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ValidationFailedResult"/> and returns it for further assertions.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The validation failure result.</returns>
    public static ValidationFailedResult ShouldBeValidationFailed(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as ValidationFailedResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected ValidationFailedResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ValidationFailedResult"/> that contains an error for the
    /// specified property name. Checks both the <see cref="ValidationFailedResult.Errors"/> dictionary
    /// and the <see cref="ValidationFailedResult.ValidationResults"/> member names.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <param name="propertyName">The property name expected in the validation errors.</param>
    /// <returns>The validation failure result for further assertions.</returns>
    public static ValidationFailedResult ShouldBeValidationFailedFor(
        this OperationResult result,
        string propertyName)
    {
        var validationFailed = result.ShouldBeValidationFailed();

        // Check the Errors dictionary first (used when constructed from ValidationException)
        if (validationFailed.Errors is { Count: > 0 } errors && errors.ContainsKey(propertyName))
        {
            return validationFailed;
        }

        // Fall back to checking ValidationResults member names (DataAnnotations path)
        if (validationFailed.ValidationResults.Any(vr => vr.MemberNames.Contains(propertyName)))
        {
            return validationFailed;
        }

        var actualErrors = validationFailed.Errors is { Count: > 0 }
            ? string.Join(", ", validationFailed.Errors.Keys)
            : validationFailed.ValidationResults.Count > 0
                ? string.Join(", ", validationFailed.ValidationResults.SelectMany(static vr => vr.MemberNames).Distinct())
                : "(no errors)";

        throw new BlueprintTestExecutionException(
            $"Expected validation error for property '{propertyName}' but actual error properties are: {actualErrors}.",
            result);
    }

    /// <summary>
    /// Asserts the result is of the exact specified type and returns it cast.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <returns>The result cast to the expected type.</returns>
    public static TResult ShouldBeResultType<TResult>(this OperationResult result)
        where TResult : OperationResult
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as TResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected {typeof(TResult).Name} but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result was successful (i.e., <see cref="OperationResult.WasSuccessful"/> is <c>true</c>).
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The same result for chaining.</returns>
    public static OperationResult ShouldBeSuccessful(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.WasSuccessful)
        {
            throw new BlueprintTestExecutionException(
                $"Expected successful result but got {result.GetType().Name} (WasSuccessful=false).",
                result);
        }

        return result;
    }

    /// <summary>
    /// Asserts the result was not successful (i.e., <see cref="OperationResult.WasSuccessful"/> is <c>false</c>).
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The same result for chaining.</returns>
    public static OperationResult ShouldNotBeSuccessful(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.WasSuccessful)
        {
            throw new BlueprintTestExecutionException(
                $"Expected unsuccessful result but got {result.GetType().Name} (WasSuccessful=true).",
                result);
        }

        return result;
    }

    /// <summary>
    /// Asserts the result is a <see cref="NotFoundResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The not-found result for further assertions.</returns>
    public static NotFoundResult ShouldBeNotFound(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as NotFoundResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected NotFoundResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="CreatedResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The created result for further assertions.</returns>
    public static CreatedResult ShouldBeCreated(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as CreatedResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected CreatedResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="CreatedResult{T}"/> and returns the created content.
    /// </summary>
    /// <typeparam name="T">The expected content type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <returns>The created content.</returns>
    public static T ShouldBeCreated<T>(this OperationResult result) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result is CreatedResult<T> createdResult)
        {
            return createdResult.Content;
        }

        throw new BlueprintTestExecutionException(
            $"Expected CreatedResult<{typeof(T).Name}> but got {result.GetType().Name}.",
            result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ForbiddenResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The forbidden result for further assertions.</returns>
    public static ForbiddenResult ShouldBeForbidden(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as ForbiddenResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected ForbiddenResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="UnauthenticatedResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The unauthenticated result for further assertions.</returns>
    public static UnauthenticatedResult ShouldBeUnauthenticated(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as UnauthenticatedResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected UnauthenticatedResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="ConflictResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The conflict result for further assertions.</returns>
    public static ConflictResult ShouldBeConflict(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as ConflictResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected ConflictResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is an <see cref="AcceptedResult"/> and returns it.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The accepted result for further assertions.</returns>
    public static AcceptedResult ShouldBeAccepted(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as AcceptedResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected AcceptedResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is a <see cref="NoResultOperationResult"/>.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The no-result for further assertions.</returns>
    public static NoResultOperationResult ShouldBeNoResult(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as NoResultOperationResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected NoResultOperationResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Asserts the result is an <see cref="UnhandledExceptionResult"/> and returns it.
    /// Useful for verifying exception propagation in pipeline tests.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <returns>The unhandled exception result for further assertions.</returns>
    public static UnhandledExceptionResult ShouldBeUnhandledException(this OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result as UnhandledExceptionResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected UnhandledExceptionResult but got {result.GetType().Name}.",
                result);
    }
}
