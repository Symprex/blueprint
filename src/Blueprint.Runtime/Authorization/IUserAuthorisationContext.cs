namespace Blueprint.Authorization;

/// <summary>
/// Represents the authorization context of the current user, providing identity and account
/// information that authorizers can inspect when making access control decisions.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are resolved from the scoped <see cref="IServiceProvider"/> at pipeline
/// execution time. The generated pipeline reads this from the <c>ApiOperationContext</c> and
/// passes it to <see cref="IApiAuthoriser"/> implementations.
/// </para>
/// <para>
/// A <see langword="null"/> context or one where <see cref="IsAnonymous"/> is <see langword="true"/>
/// indicates an unauthenticated caller. Operations that require authentication will reject
/// such callers with an <see cref="UnauthorizedException"/>.
/// </para>
/// </remarks>
public interface IUserAuthorisationContext
{
    /// <summary>
    /// Gets a value indicating whether the user account is active and able to use the system.
    /// An inactive account may be authenticated but should be denied access to most operations.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets a value indicating whether the user is anonymous (not authenticated).
    /// </summary>
    bool IsAnonymous { get; }

    /// <summary>
    /// Gets the application-level user identifier (e.g. database primary key).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the identity provider's identifier for the user (e.g. Auth0 sub claim).
    /// </summary>
    string? IdentityId { get; }

    /// <summary>
    /// Gets the account or tenant identifier the user belongs to.
    /// </summary>
    string AccountId { get; }

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Populates metadata about this user onto an external store (e.g. activity tags, log scopes).
    /// </summary>
    /// <param name="add">A callback that receives key-value pairs to record.</param>
    void PopulateMetadata(Action<string, object> add);

    /// <summary>
    /// Populates metadata about this user onto an external store, passing through a
    /// <typeparamref name="TState"/> parameter so the callback can be a <c>static</c> delegate
    /// (zero closure allocation per request).
    /// </summary>
    /// <typeparam name="TState">The type of the state passed through to the callback.</typeparam>
    /// <param name="add">A static callback that receives key, value, and state.</param>
    /// <param name="state">The state to pass through to each callback invocation.</param>
    void PopulateMetadata<TState>(Action<string, object, TState> add, TState state)
    {
        this.PopulateMetadata((key, value) => add(key, value, state));
    }
}
