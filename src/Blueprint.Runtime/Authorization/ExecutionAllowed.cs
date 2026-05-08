namespace Blueprint.Authorization;

/// <summary>
/// Represents the result of an authorization check. A value of <see cref="Yes"/> indicates the
/// operation may proceed; a failure created via <see cref="No"/> carries a diagnostic reason,
/// a user-facing message, and a <see cref="ExecutionAllowedFailureType"/> that determines whether
/// the pipeline throws <see cref="UnauthorizedException"/> (identity) or
/// <see cref="ForbiddenException"/> (permission).
/// </summary>
/// <remarks>
/// This is a struct to avoid allocations on the happy path where <see cref="Yes"/> is returned.
/// Legacy Blueprint uses the same struct-based pattern for zero-allocation authorization checks.
/// </remarks>
public readonly struct ExecutionAllowed
{
    /// <summary>
    /// A pre-allocated success result indicating the operation is allowed to proceed.
    /// </summary>
    public static readonly ExecutionAllowed Yes = new(isAllowed: true, reason: null!, message: null!, failureType: null);

    /// <summary>
    /// A cached <see cref="Task{ExecutionAllowed}"/> wrapping <see cref="Yes"/> so that
    /// synchronous authorizers can return without allocating a new Task.
    /// </summary>
    public static readonly Task<ExecutionAllowed> YesTask = Task.FromResult(Yes);

    private ExecutionAllowed(bool isAllowed, string reason, string message, ExecutionAllowedFailureType? failureType)
    {
        this.IsAllowed = isAllowed;
        this.Reason = reason;
        this.Message = message;
        this.FailureType = failureType;
    }

    /// <summary>
    /// Gets a value indicating whether the operation is allowed to execute.
    /// </summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// Gets the machine-readable reason the operation was denied (e.g. "missing_scope", "inactive_account").
    /// Only meaningful when <see cref="IsAllowed"/> is <see langword="false"/>.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the user-facing message explaining why the operation was denied.
    /// Only meaningful when <see cref="IsAllowed"/> is <see langword="false"/>.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the failure classification that determines the HTTP status code (401 vs 403).
    /// Only meaningful when <see cref="IsAllowed"/> is <see langword="false"/>.
    /// </summary>
    public ExecutionAllowedFailureType? FailureType { get; }

    /// <summary>
    /// Creates a denial result with a diagnostic reason, user-facing message, and failure classification.
    /// </summary>
    /// <param name="reason">A machine-readable reason for the denial (logged for diagnostics).</param>
    /// <param name="message">A user-facing message describing why access was denied.</param>
    /// <param name="failureType">Whether the failure is an authentication or authorization issue.</param>
    /// <returns>An <see cref="ExecutionAllowed"/> indicating the operation is not permitted.</returns>
    public static ExecutionAllowed No(string reason, string message, ExecutionAllowedFailureType failureType)
    {
        return new ExecutionAllowed(isAllowed: false, reason: reason, message: message, failureType: failureType);
    }
}
