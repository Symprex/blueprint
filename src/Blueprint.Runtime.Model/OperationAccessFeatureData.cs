namespace Blueprint.Model;

/// <summary>
/// Records which access attribute an operation declares, determined at compile time. An operation that declares none has
/// no <see cref="OperationAccessFeatureData"/>.
/// </summary>
/// <remarks>
/// <see cref="OperationAuthenticationFeatureData"/> says what the pipeline enforces, and an HTTP operation that declares
/// nothing is authenticated by default, so it cannot tell <c>[MustBeAuthenticated]</c> from no declaration. This can, which
/// an app needs when it treats the explicitly declared access modes specially, as a role model that maps only permissioned
/// operations to roles does.
/// </remarks>
/// <param name="Mode">The declared access mode.</param>
public sealed record OperationAccessFeatureData(OperationAccessMode Mode);

/// <summary>
/// The access an operation declares.
/// </summary>
public enum OperationAccessMode
{
    /// <summary>
    /// <c>[AllowAnonymous]</c>: anyone may call it.
    /// </summary>
    Anonymous,

    /// <summary>
    /// <c>[MustBeAuthenticated]</c> and no scope pairs: any authenticated caller may call it.
    /// </summary>
    Authenticated,

    /// <summary>
    /// <c>[Resource]</c> or <c>[FromResource]</c>: the caller must satisfy one of its scope pairs. This wins over
    /// <c>[MustBeAuthenticated]</c> on the same operation, which permissioned access already implies.
    /// </summary>
    Permissioned,
}
