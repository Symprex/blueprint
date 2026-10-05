using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
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
    /// Indicates whether this result represents a successful pipeline outcome that should allow
    /// post-execution side effects (persistence saves, background-task flushes, link population, etc.)
    /// to proceed. Failure results override this to return <see langword="false"/>, which prevents
    /// downstream modules from acting on an already-failed pipeline.
    /// </summary>
    /// <remarks>
    /// Semantic non-2xx responses like <c>NotFoundResult</c> or <c>ConflictResult</c> are still considered
    /// successful because they carry a valid response payload. Only pipeline-level failures (validation,
    /// authentication, binding, unhandled exceptions) are excluded.
    /// <para>
    /// It is not part of any result's JSON: a legacy <c>ResourceEvent</c>, a result that is itself written as the response,
    /// was a plain object with no such member.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public virtual bool WasSuccessful => true;

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
    [UnconditionalSuppressMessage("Trimming", "IL2087", Justification = "Results<> union types are referenced by operation contracts and will not be trimmed.")]
    public virtual T Unwrap<T>()
    {
        // Handle the case of wanting to "unwrap" to an actual result type (typical in HTTP clients or integration
        // tests). For example calling `Unwrap<PlainTextResult>()` on a `PlainTextResult` would return the result itself.
        if (this is T asType)
        {
            return asType;
        }

        // Handle Results<T1, T2> union types: if the desired type is a Results<> struct and this result
        // is one of the union members, use the implicit conversion operator to construct the union value.
        // This reflection path is intentionally non-trimmed — it serves test fixtures and HTTP clients,
        // not AOT-compiled hot paths.
        var targetType = typeof(T);

        if (targetType.IsValueType && targetType.IsGenericType)
        {
            var genDef = targetType.GetGenericTypeDefinition();

            if (genDef.FullName?.StartsWith("Blueprint.Model.Results`", StringComparison.Ordinal) == true)
            {
                return this.TryUnwrapToResultsUnion<T>(targetType);
            }
        }

        throw new InvalidOperationException(
            $"Unable to unwrap operation result of type {this.GetType().Name} to {typeof(T).Name}.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Results<> union types are referenced by operation contracts and will not be trimmed.")]
    private T TryUnwrapToResultsUnion<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type targetType)
    {
        var typeArgs = targetType.GetGenericArguments();

        for (var i = 0; i < typeArgs.Length; i++)
        {
            if (typeArgs[i].IsAssignableFrom(this.GetType()))
            {
                var implicitOp = targetType.GetMethod(
                    "op_Implicit",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeArgs[i]],
                    null);

                if (implicitOp != null)
                {
                    return (T)implicitOp.Invoke(null, [this])!;
                }
            }
        }

        throw new InvalidOperationException(
            $"Unable to unwrap operation result of type {this.GetType().Name} to {typeof(T).Name}.");
    }
}
