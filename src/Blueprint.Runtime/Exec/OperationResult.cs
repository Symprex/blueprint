using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Represents the result of an operation that has passed through the API pipeline, providing a means
/// of execution against an output destination (i.e. HTTP output stream) depending on what
/// is to be achieved.
/// </summary>
public abstract class OperationResult
{
    /// <summary>
    /// Executes this result.
    /// </summary>
    /// <param name="context">The context representing the current Blueprint operation execution.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <remarks>
    /// Direct execution remains the legacy compatibility path while hosts migrate to explicit result executors/adapters.
    /// New host integrations should prefer those host-owned translation seams rather than expanding self-executing result behavior.
    /// </remarks>
    public abstract Task ExecuteAsync(ApiOperationContext context);

    /// <summary>
    /// Attempts to "unwrap" this operation result to the given type, throwing an exception if the result is not
    /// of the correct type.
    /// </summary>
    /// <typeparam name="T">The type to unwrap to.</typeparam>
    /// <returns>The unwrapped value.</returns>
    public virtual T Unwrap<T>()
    {
        // Handle the case of wanting to "unwrap" to an actual result type (typical in HTTP clients or integration
        // tests). For example calling `Unwrap<PlainTextResult>()` on a `PlainTextResult` would return the result itself.
        return this is T asType
            ? asType
            : throw new InvalidOperationException(
            $"Unable to unwrap operation result of type {this.GetType().Name} to {typeof(T).Name}.");
    }
}
