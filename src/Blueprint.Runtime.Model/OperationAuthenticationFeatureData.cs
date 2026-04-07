namespace Blueprint.Model;

/// <summary>
/// Describes the authentication requirement for an operation, determined at compile time from
/// declarative attributes such as <c>[MustBeAuthenticated]</c>.
/// </summary>
/// <param name="Requirement">The level of identity required before the operation can execute.</param>
public sealed record OperationAuthenticationFeatureData(AuthenticationRequirement Requirement);

/// <summary>
/// The level of identity required for an operation.
/// </summary>
public enum AuthenticationRequirement
{
    /// <summary>
    /// The operation does not require an authenticated identity.
    /// </summary>
    Anonymous,

    /// <summary>
    /// The operation requires an authenticated identity. Unauthenticated requests are rejected
    /// before the operation handler executes.
    /// </summary>
    Authenticated,
}
