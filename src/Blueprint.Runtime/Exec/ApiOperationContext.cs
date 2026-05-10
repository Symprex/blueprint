using System.Diagnostics;
using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Http;

namespace Blueprint.Runtime.Exec;

/// <summary>
/// The context of an API operation, the object that is passed to all middleware and final operation handlers
/// to allow sharing of state, for example the operation being executed, or an authentication context that
/// gets created.
/// </summary>
public class ApiOperationContext : IHttpOperationContext
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ApiOperationContext" /> class, using
    /// <see cref="ApiOperationDescriptor.CreateInstance" /> to construct the operation
    /// instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider (typically a nested scope) for this context.</param>
    /// <param name="app">The data model that represents the API in which this context is being executed.</param>
    /// <param name="operationDescriptor">A descriptor for the operation that is being executed.</param>
    /// <param name="token">A cancellation token to indicate the operation should stop.</param>
    public ApiOperationContext(
        IServiceProvider serviceProvider,
        BlueprintApp app,
        ApiOperationDescriptor operationDescriptor,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(operationDescriptor);

        this.Descriptor = operationDescriptor;
        this.OperationCancelled = token;
        this.App = app;
        this.ServiceProvider = serviceProvider;
        this.Operation = operationDescriptor.CreateInstance();
        this.Activity = Activity.Current;
    }

    /// <summary>
    /// Gets the data model for this operation.
    /// </summary>
    public BlueprintApp App { get; }

    /// <summary>
    /// Gets the operation descriptor that described the operation that is currently being executed.
    /// </summary>
    public ApiOperationDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the cancellation token that is used to indicate / request cancellation of an operation.
    /// </summary>
    public CancellationToken OperationCancelled { get; }

    /// <summary>
    /// Gets the operation that is currently being executed.
    /// </summary>
    public object Operation { get; set; }

    /// <summary>
    /// The <see cref="IServiceProvider" /> associated with this operation execution, allowing middleware and
    /// handlers to get runtime dependencies out of the correctly scoped container.
    /// </summary>
    public IServiceProvider ServiceProvider { get; set; }

    /// <summary>
    /// Gets the parent <see cref="ApiOperationContext" /> of this context, which may be null.
    /// </summary>
    public ApiOperationContext? Parent { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="Activity" /> for this context, which may be <c>null</c> if the host
    /// of an operation has not integrated with APM tooling.
    /// </summary>
    public Activity? Activity { get; set; }

    /// <summary>
    /// Gets a value indicating whether this is a nested context, meaning it has been created as a child of an
    /// existing context, used to execute another operation in the context of an existing one.
    /// </summary>
    public bool IsNested => this.Parent != null;

    private Dictionary<string, object>? _data;

    /// <summary>
    /// Provides a generic means of middleware components or hosts to store data related to an API operation
    /// to be used throughout the processing pipeline. Lazy-initialized to avoid allocation when not used.
    /// </summary>
    public Dictionary<string, object> Data
    {
        get => this._data ??= new Dictionary<string, object>();
        private set => this._data = value;
    }

    /// <summary>
    /// Gets the stable base URI for this request (e.g. <c>https://api.example.com/v1</c>).
    /// Computed once per request from the HTTP host, scheme, and path base. Used by link generators
    /// and redirect targets.
    /// </summary>
    public string? BaseUri { get; internal set; }

    /// <summary>
    /// Gets or sets the <see cref="Microsoft.AspNetCore.Http.HttpContext"/> associated with this operation
    /// when the operation is being executed via the HTTP host.
    /// </summary>
    public HttpContext? HttpContext { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="ClaimsPrincipal"/> representing the authenticated user for this operation.
    /// Set by the HTTP host from <see cref="Microsoft.AspNetCore.Http.HttpContext.User"/>.
    /// </summary>
    public ClaimsPrincipal? UserPrincipal { get; set; }

    /// <summary>
    /// Gets or sets the application-specific authorization context for the current user. Populated
    /// by the generated pipeline when an <see cref="IUserAuthorisationContextFactory"/> is registered
    /// in DI. Authorizers (<see cref="IApiAuthoriser"/>) and operation handlers read this to inspect
    /// application-level identity properties (tenant, roles, account status).
    /// </summary>
    public IUserAuthorisationContext? UserAuthorisationContext { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authorization checks should be skipped for this
    /// operation execution. Used for nested/child operations that inherit the parent's authorization
    /// context and should not be re-evaluated.
    /// </summary>
    public bool SkipAuthorisation { get; set; }

    /// <summary>
    /// Creates a nested execution context for dispatching a child operation from within a handler or
    /// middleware. The nested context inherits the current scope's service provider and user identity
    /// but targets a different operation and descriptor.
    /// </summary>
    /// <param name="operation">The child operation to execute.</param>
    /// <param name="skipAuthorisation">
    /// Whether to skip authorization for the nested operation. Defaults to <c>true</c> because
    /// the parent operation has already been authorized.
    /// </param>
    /// <returns>A new context configured for the nested operation.</returns>
    /// <exception cref="InvalidOperationException">If no descriptor is found for the operation type.</exception>
    public ApiOperationContext CreateNested(IOperation operation, bool skipAuthorisation = true)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var operationType = operation.GetType();
        var descriptor = this.App.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is registered in the BlueprintApp.");

        var nested = new ApiOperationContext(
            this.ServiceProvider,
            this.App,
            descriptor,
            this.OperationCancelled)
        {
            Operation = operation,
            SkipAuthorisation = skipAuthorisation,
            Parent = this,
            UserPrincipal = this.UserPrincipal,
            UserAuthorisationContext = this.UserAuthorisationContext,
            HttpContext = this.HttpContext,
            BaseUri = this.BaseUri,

            // Always share the mutable state dictionary with the parent so nested contexts
            // see the same data store. This triggers lazy creation if the parent hasn't used
            // Data yet, but nested operations are comparatively rare.
            _data = this.Data,
        };

        return nested;
    }

    /// <summary>
    /// Gets the primary <see cref="ClaimsIdentity"/> of the current user, or <c>null</c> when no
    /// authenticated identity is available. Derived from <see cref="UserPrincipal"/> so the two
    /// cannot diverge.
    /// </summary>
    public ClaimsIdentity? UserIdentity => this.UserPrincipal?.Identity as ClaimsIdentity;

    /// <inheritdoc />
    HttpContext IHttpOperationContext.HttpContext =>
        this.HttpContext ?? throw new InvalidOperationException(
            "HttpContext is not available. This operation is not being executed via the HTTP host.");
}
