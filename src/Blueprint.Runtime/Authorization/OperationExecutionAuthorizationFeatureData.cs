using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Authorization;

/// <summary>
/// Resolves an operation's scope pairs from an instance of it and the caller, as the operation's execution resolves them,
/// so <see cref="ApiOperationContext.CanExecuteAsync(IOperation, CancellationToken)"/> asks the providers exactly what the
/// execution would. Generated for every operation that declares scope pairs.
/// </summary>
/// <remarks>
/// The generator builds it from the same expansion as the execution's generated check, with the instance in place of the
/// pipeline's operation: <c>{Prop}</c> tokens read the instance, and <c>{Auth.X}</c> tokens read the context's
/// <see cref="ApiOperationContext.UserAuthorisationContext"/> and then its principal's claims. It lives here rather than
/// beside the other authorization feature data because it reads an <see cref="ApiOperationContext"/>.
/// </remarks>
/// <param name="resolve">Given an instance of the operation and the context it is judged in, its resolved scope pairs.</param>
public sealed class OperationExecutionAuthorizationFeatureData(
    Func<object, ApiOperationContext, IReadOnlyList<ResolvedScopePair>> resolve)
{
    /// <summary>
    /// Resolves the operation's scope pairs from <paramref name="operation"/> and the caller of <paramref name="context"/>.
    /// </summary>
    /// <param name="operation">An instance of the operation, whose properties the templates read.</param>
    /// <param name="context">The context the operation is judged in, whose caller the templates' <c>{Auth.X}</c> tokens read.</param>
    /// <returns>The scope pairs, each template expanded as at execution; a pair with no template has no key.</returns>
    public IReadOnlyList<ResolvedScopePair> Resolve(object operation, ApiOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        return resolve(operation, context);
    }
}
