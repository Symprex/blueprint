namespace Blueprint.Model;

/// <summary>
/// Resolves an operation's scope pairs from an instance of it and the caller, as the operation's execution resolves them,
/// so a check of that instance asks the resource authorization providers exactly what its execution would. Generated for
/// every operation that declares scope pairs.
/// </summary>
/// <remarks>
/// The generator builds it from the same expansion as the execution's generated check, with the instance in place of the
/// pipeline's operation: <c>{Prop}</c> tokens read the instance, with the same fallbacks, value types and nested paths.
/// <c>{Auth.X}</c> tokens call the runtime's caller lookup, which resolves them as the generated check does: from the
/// user's authorisation context for the names the generator knows, then from the principal's claim of that name. It takes
/// a lookup rather than an operation context so a contract assembly that references only this one still compiles.
/// </remarks>
/// <param name="resolve">
/// Given an instance of the operation and a lookup of the caller's values by <c>{Auth.X}</c> name, its resolved scope pairs.
/// </param>
public sealed class OperationExecutionAuthorizationFeatureData(
    Func<object, Func<string, string?>, IReadOnlyList<ResolvedScopePair>> resolve)
{
    /// <summary>
    /// Resolves the operation's scope pairs from <paramref name="operation"/> and the caller.
    /// </summary>
    /// <param name="operation">An instance of the operation, whose properties the templates read.</param>
    /// <param name="callerValue">Gives the caller's value for an <c>{Auth.X}</c> token's name, or <see langword="null"/>.</param>
    /// <returns>The scope pairs, each template expanded as at execution; a pair with no template has no key.</returns>
    public IReadOnlyList<ResolvedScopePair> Resolve(object operation, Func<string, string?> callerValue)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(callerValue);

        return resolve(operation, callerValue);
    }
}
