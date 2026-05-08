namespace Blueprint.Authorization;

/// <summary>
/// Classifies why an authorization check failed, allowing the pipeline to throw
/// the appropriate HTTP-mapped exception (<see cref="UnauthorizedException"/> for
/// identity failures, <see cref="ForbiddenException"/> for permission failures).
/// </summary>
public enum ExecutionAllowedFailureType
{
    /// <summary>
    /// The caller's identity could not be verified (maps to HTTP 401).
    /// </summary>
    Authentication,

    /// <summary>
    /// The caller is authenticated but lacks the required permissions (maps to HTTP 403).
    /// </summary>
    Authorisation,
}
