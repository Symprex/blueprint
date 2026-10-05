using Blueprint.Authorization;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Indicates the request was rejected because the authenticated caller does not have
/// sufficient permissions for the requested resource operation.
/// The HTTP host translates this to a 403 Forbidden response.
/// </summary>
public sealed class ForbiddenResult : OperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenResult"/> class for a denial with no authoriser's detail.
    /// </summary>
    public ForbiddenResult()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForbiddenResult"/> class for the denial an authoriser returned.
    /// </summary>
    /// <param name="failure">The denying authoriser's result.</param>
    public ForbiddenResult(ExecutionAllowed failure)
    {
        this.Failure = failure;
    }

    /// <summary>
    /// Gets a shared instance for a denial with no authoriser's detail (a resource authorization provider's), to avoid
    /// allocating a new object per denied request. Safe because <see cref="ForbiddenResult"/> is immutable.
    /// </summary>
    public static ForbiddenResult Instance { get; } = new();

    /// <summary>
    /// Gets the denying authoriser's result, or <see langword="null"/> for a denial with no authoriser's detail.
    /// </summary>
    /// <remarks>
    /// The HTTP host writes it as legacy Blueprint's <c>ForbiddenException(result, exposeSensitiveDetail: false)</c>:
    /// <see cref="ExecutionAllowed.Message"/> as the problem's title, type <c>unauthorized</c>. The
    /// <see cref="ExecutionAllowed.Reason"/> is never written: legacy exposed it only for a <c>ClaimsRequiredApiAuthoriser</c>
    /// in development, and it is logged instead.
    /// </remarks>
    public ExecutionAllowed? Failure { get; }

    /// <inheritdoc />
    public override bool WasSuccessful => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        return Task.CompletedTask;
    }
}
