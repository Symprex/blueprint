namespace Blueprint.Runtime.Exec;

/// <summary>
/// Provides nested sealed result types that carry only an HTTP status code with no response body.
/// These mirror the legacy <c>StatusCodeResult.Accepted</c>, <c>StatusCodeResult.Created</c>, and
/// <c>StatusCodeResult.NoContent</c> patterns, enabling frictionless migration from the runtime-middleware
/// Blueprint to the source-generated Blueprint.
/// </summary>
/// <remarks>
/// <para>
/// Each nested type exposes a singleton <see cref="StatusCodeResult.Accepted.Instance"/> (and equivalents)
/// so that handler return expressions remain a single static field read. The HTTP result executor maps
/// these to the correct status code and writes no response body.
/// </para>
/// <para>
/// When an operation genuinely produces a payload alongside the status code, prefer the richer result
/// types such as <see cref="AcceptedResult{T}"/>, <see cref="CreatedResult{T}"/>, or <see cref="OkResult{T}"/>
/// instead.
/// </para>
/// </remarks>
public abstract class StatusCodeResult : OperationResult
{
    /// <summary>
    /// Gets the HTTP status code that this result represents.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCodeResult"/> class.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    protected StatusCodeResult(int statusCode)
    {
        this.StatusCode = statusCode;
    }

    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException(
            $"StatusCodeResult ({this.StatusCode}) must be translated by a host-specific result executor.");
    }

    /// <summary>
    /// A bodyless 200 OK result. Prefer <see cref="OkResult{T}"/> when the response carries a payload.
    /// </summary>
    public sealed class OK : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly OK Instance = new();

        private OK()
            : base(200)
        {
        }
    }

    /// <summary>
    /// A bodyless 201 Created result. Prefer <see cref="CreatedResult{T}"/> when the response carries a payload.
    /// </summary>
    public sealed class Created : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly Created Instance = new();

        private Created()
            : base(201)
        {
        }
    }

    /// <summary>
    /// A bodyless 202 Accepted result indicating the request was accepted for asynchronous processing.
    /// Prefer <see cref="AcceptedResult{T}"/> when the response carries a payload.
    /// </summary>
    public sealed class Accepted : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly Accepted Instance = new();

        private Accepted()
            : base(202)
        {
        }
    }

    /// <summary>
    /// A 204 No Content result indicating successful processing with no response body.
    /// </summary>
    public sealed class NoContent : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly NoContent Instance = new();

        private NoContent()
            : base(204)
        {
        }
    }

    /// <summary>
    /// A bodyless 400 Bad Request result for malformed or semantically invalid requests
    /// that fall outside standard validation (which uses <see cref="ValidationFailedResult"/>).
    /// </summary>
    public sealed class BadRequest : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly BadRequest Instance = new();

        private BadRequest()
            : base(400)
        {
        }
    }

    /// <summary>
    /// A bodyless 501 Not Implemented result for operations that are declared but not yet functional.
    /// </summary>
    public sealed class NotImplemented : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly NotImplemented Instance = new();

        private NotImplemented()
            : base(501)
        {
        }
    }

    /// <summary>
    /// A bodyless 503 Service Unavailable result for operations that are temporarily offline.
    /// </summary>
    public sealed class ServiceUnavailable : StatusCodeResult
    {
        /// <summary>
        /// The singleton instance.
        /// </summary>
        public static readonly ServiceUnavailable Instance = new();

        private ServiceUnavailable()
            : base(503)
        {
        }
    }
}
