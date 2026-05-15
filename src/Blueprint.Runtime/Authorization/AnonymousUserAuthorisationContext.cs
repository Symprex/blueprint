namespace Blueprint.Authorization;

/// <summary>
/// A sentinel <see cref="IUserAuthorisationContext"/> that represents an anonymous (unauthenticated)
/// user. This is used as the default context when no <see cref="IUserAuthorisationContextFactory"/>
/// is registered or when the caller has no authentication credentials.
/// </summary>
/// <remarks>
/// <para>
/// Operations marked with <c>[AllowAnonymous]</c> will proceed with this context, while
/// operations that require authentication (the default for HTTP) will reject it with an
/// <see cref="UnauthorizedException"/>.
/// </para>
/// <para>
/// The singleton <see cref="Instance"/> should be used instead of creating new instances.
/// </para>
/// </remarks>
public sealed class AnonymousUserAuthorisationContext : IUserAuthorisationContext
{
    /// <summary>
    /// Gets the singleton anonymous context instance.
    /// </summary>
    public static readonly AnonymousUserAuthorisationContext Instance = new();

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public bool IsAnonymous => true;

    /// <inheritdoc />
    public string Id => string.Empty;

    /// <inheritdoc />
    public string IdentityId => string.Empty;

    /// <inheritdoc />
    public string AccountId => string.Empty;

    /// <inheritdoc />
    public string Email => string.Empty;

    /// <inheritdoc />
    public string Name => "Anonymous";

    /// <inheritdoc />
    public void PopulateMetadata(Action<string, object> add)
    {
        add("enduser.anonymous", true);
    }

    /// <inheritdoc />
    public void PopulateMetadata<TState>(Action<string, object, TState> add, TState state)
    {
        add("enduser.anonymous", true, state);
    }
}
