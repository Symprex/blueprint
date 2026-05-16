using Blueprint.Authorization;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// Base class for operation handlers that provides property-injected pipeline context.
/// Signature 365-style handler bases can inherit from this type to avoid colliding with
/// their own <c>ApiOperations</c> class names.
/// </summary>
/// <remarks>
/// <para>
/// The source generator detects public settable properties on handler classes (including
/// those inherited from base classes) that match well-known pipeline types. It emits
/// assignment statements in the generated pipeline to populate them before handler
/// execution. This eliminates the need for constructor injection of pipeline infrastructure.
/// </para>
/// <para>
/// Computed properties (<see cref="HttpContext"/>, <see cref="HttpRequest"/>,
/// <see cref="HttpResponse"/>) derive from the injected <see cref="Context"/> and throw
/// <see cref="InvalidOperationException"/> when the operation is not HTTP-hosted (e.g.
/// Rebus message handlers). Handlers that run outside an HTTP pipeline should not access
/// these properties.
/// </para>
/// </remarks>
public abstract class BlueprintApiOperations
{
    /// <summary>
    /// Gets or sets the <see cref="ApiOperationContext"/> for the current operation.
    /// Injected by the generated pipeline as <c>context</c>.
    /// </summary>
    public ApiOperationContext Context { get; set; } = null!;

    /// <summary>
    /// Gets the <see cref="Microsoft.AspNetCore.Http.HttpContext"/> for HTTP-hosted operations.
    /// Throws <see cref="InvalidOperationException"/> when the operation is not HTTP-hosted.
    /// </summary>
    public HttpContext HttpContext => this.Context.GetHttpContext();

    /// <summary>
    /// Gets the <see cref="Microsoft.AspNetCore.Http.HttpRequest"/> for HTTP-hosted operations.
    /// </summary>
    public HttpRequest HttpRequest => this.HttpContext.Request;

    /// <summary>
    /// Gets the <see cref="Microsoft.AspNetCore.Http.HttpResponse"/> for HTTP-hosted operations.
    /// </summary>
    public HttpResponse HttpResponse => this.HttpContext.Response;

    /// <summary>
    /// Gets or sets the user authorization context for the current request. Injected by the
    /// generated pipeline; falls back to <see cref="AnonymousUserAuthorisationContext"/> for
    /// unauthenticated or non-HTTP operations.
    /// </summary>
    public IUserAuthorisationContext Identity { get; set; } = null!;

    /// <summary>
    /// Gets or sets a logger scoped to the current pipeline. Injected by the generated pipeline.
    /// </summary>
    public ILogger Logger { get; set; } = null!;

    /// <summary>
    /// Gets or sets the cancellation token for the current operation. Injected by the
    /// generated pipeline as <c>context.OperationCancelled</c>.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}
