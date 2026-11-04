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
public sealed class ApiOperationContext : IHttpOperationContext
{
    // What CanExecuteAsync answers a caller with no authenticated principal, as an execution refuses one
    private static readonly ExecutionAllowed _anonymousCaller = ExecutionAllowed.No(
        "The caller has no authenticated principal, so no resource authorization provider can judge it",
        "Please log in",
        ExecutionAllowedFailureType.Authentication);

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
    /// Gets a value indicating whether the <see cref="Data"/> dictionary has been initialized,
    /// allowing callers to skip a read without triggering lazy allocation.
    /// </summary>
    public bool HasData => this._data is not null;

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
    /// Gets or sets the stable base URI for this request (e.g. <c>https://api.example.com/v1/</c>).
    /// Computed once per request from the HTTP host, scheme, and path base. Used by link generators,
    /// a resource event's href and redirect targets.
    /// </summary>
    /// <remarks>
    /// The router sets it. A host that executes operations without the router, such as a test harness, sets it to the
    /// base URI links and hrefs should be made under, as legacy Blueprint's testing harness did; left unset, no href is
    /// made. A nested context takes its parent's.
    /// </remarks>
    public string? BaseUri { get; set; }

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
    /// Gets the <see cref="System.Security.Claims.ClaimsIdentity"/> of the authenticated user, or
    /// <see langword="null"/> when no authenticated identity is available. This is a convenience
    /// shortcut equivalent to <c>UserPrincipal?.Identity as ClaimsIdentity</c>.
    /// </summary>
    public ClaimsIdentity? ClaimsIdentity => this.UserPrincipal?.Identity as ClaimsIdentity;

    /// <summary>
    /// Gets or sets the application-specific authorization context for the current user. Populated
    /// by the generated pipeline when an <see cref="IUserAuthorisationContextFactory"/> is registered
    /// in DI. Authorizers (<see cref="IApiAuthoriser"/>) and operation handlers read this to inspect
    /// application-level identity properties (tenant, roles, account status).
    /// </summary>
    public IUserAuthorisationContext? UserAuthorisationContext { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authorization checks should be skipped for this
    /// operation execution, which should be used with care: the framework sets it for a resource event's self query,
    /// whose resource the operation has just changed, as legacy Blueprint did.
    /// </summary>
    public bool SkipAuthorisation { get; set; }

    /// <summary>
    /// Creates a nested execution context for dispatching a child operation from within a handler or
    /// middleware. The nested context inherits the current scope's service provider and user identity
    /// but targets a different operation and descriptor.
    /// </summary>
    /// <remarks>
    /// The nested operation is authorised by default, with the user's context it inherits rather than one loaded again,
    /// as legacy Blueprint's <c>CreateNested</c> left <c>SkipAuthorisation</c> false and its nested pipeline kept the
    /// authorisation middleware but not the user context loader (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint/ApiOperationContext.cs:163-180</c>, <c>Authorisation/AuthorisationMiddlewareBuilder.cs:21</c>,
    /// <c>Authorisation/UserContextLoaderMiddlewareBuilder.cs:11</c>). The parent's <see cref="SkipAuthorisation"/> is not
    /// inherited, as legacy's was not.
    /// </remarks>
    /// <param name="operation">The child operation to execute.</param>
    /// <param name="skipAuthorisation">
    /// Whether to skip authorization for the nested operation. Defaults to <c>false</c>, as legacy did: pass <c>true</c>
    /// only where the parent's authorisation is enough, as the framework does for a resource event's self query.
    /// </param>
    /// <returns>A new context configured for the nested operation.</returns>
    /// <exception cref="InvalidOperationException">If no descriptor is found for the operation type.</exception>
    public ApiOperationContext CreateNested(IOperation operation, bool skipAuthorisation = false)
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

            // Share the mutable state dictionary with the parent. Using the property
            // getter ensures the parent allocates its dictionary if it hasn't yet, so
            // parent and nested context always share the same reference.
            _data = this.Data,
        };

        return nested;
    }

    /// <summary>
    /// Decides whether this context's caller may execute <typeparamref name="TOperation"/> against
    /// <paramref name="resourceKey"/>, as that operation's own execution would decide it: for a handler that must filter
    /// what it returns, or what it offers, by what the caller may do with other operations (Symprex/blueprint#129).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every registered <see cref="IResourceAuthorizationProvider"/> is asked, all-of in registration order, in
    /// <see cref="ResourceAuthorizationMode.Execute"/> mode for <typeparamref name="TOperation"/>'s descriptor, so the
    /// same providers, grant sources, expanders and checker decide it as decide the execution. Each of the operation's
    /// scope pairs with a template is given <paramref name="resourceKey"/> as its resolved key, whatever the template;
    /// a pair with none is given no key, as at execution. Any one pair may satisfy a provider, as at execution.
    /// </para>
    /// <para>
    /// The providers are given a nested context for <typeparamref name="TOperation"/>, whose <see cref="Parent"/> is this
    /// one and whose <see cref="Operation"/> is a default instance of it, made by its descriptor: the check has no instance,
    /// so a provider that reads the operation's properties reads their defaults. It shares this context's caller,
    /// <see cref="UserAuthorisationContext"/>, <see cref="HttpContext"/>, <see cref="BaseUri"/> and <see cref="Data"/>.
    /// A provider that declares it reads the instance, through
    /// <see cref="IResourceAuthorizationProvider.ReadsOperation(ApiOperationDescriptor)"/>, is not handed one: the check
    /// is refused as <see cref="ExecutionAllowedFailureType.Authorisation"/>, no provider is asked, and the caller must
    /// ask <see cref="CanExecuteAsync(IOperation, CancellationToken)"/> with the instance.
    /// </para>
    /// <para>
    /// The caller is <see cref="UserPrincipal"/>. With no authenticated principal it is refused as anonymous and no
    /// provider is asked, as an execution refuses it; with no provider registered it is refused, as an execution with
    /// scope pairs is. <see cref="SkipAuthorisation"/> does not apply: it is about this context's own execution, and the
    /// question is about the caller.
    /// </para>
    /// <para>
    /// Only the providers are asked, not the <see cref="IApiAuthoriser"/>s, which judge an operation instance this has
    /// none of. The denial is returned, not logged: its reason may name claims and keys, so log it only where the
    /// caller cannot see it.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOperation">The operation asked about, which must declare scope pairs.</typeparam>
    /// <param name="resourceKey">The key of the resource the operation would act on, absolute or partial.</param>
    /// <param name="cancellationToken">Cancellation for the decision; <see cref="OperationCancelled"/> when not given.</param>
    /// <returns><see cref="ExecutionAllowed.Yes"/>, or the first provider's denial.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TOperation"/> is not an operation of <see cref="App"/>, or declares no scope pairs, so there is
    /// nothing for a key to be judged against; or <paramref name="resourceKey"/> is the empty key.
    /// </exception>
    public ValueTask<ExecutionAllowed> CanExecuteAsync<TOperation>(ResourceKey resourceKey, CancellationToken cancellationToken = default)
    {
        return this.CanExecuteAsync(typeof(TOperation), resourceKey, cancellationToken);
    }

    /// <summary>
    /// Decides whether this context's caller may execute the operation of type <paramref name="operationType"/> against
    /// <paramref name="resourceKey"/>, as <see cref="CanExecuteAsync{TOperation}(ResourceKey, CancellationToken)"/> does.
    /// </summary>
    /// <param name="operationType">The type of the operation asked about, which must declare scope pairs.</param>
    /// <param name="resourceKey">The key of the resource the operation would act on, absolute or partial.</param>
    /// <param name="cancellationToken">Cancellation for the decision; <see cref="OperationCancelled"/> when not given.</param>
    /// <returns><see cref="ExecutionAllowed.Yes"/>, or the first provider's denial.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="operationType"/> is not an operation of <see cref="App"/>, or declares no scope pairs; or
    /// <paramref name="resourceKey"/> is the empty key.
    /// </exception>
    public ValueTask<ExecutionAllowed> CanExecuteAsync(Type operationType, ResourceKey resourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationType);

        if (resourceKey.Value.Length == 0)
        {
            throw new ArgumentException("A resource key must be given; default(ResourceKey) is the empty key, which no grant can cover.", nameof(resourceKey));
        }

        var (descriptor, authorization) = this.FindJudgedOperation(operationType, nameof(operationType));

        if (this.UserPrincipal is not { Identity.IsAuthenticated: true } principal)
        {
            return new ValueTask<ExecutionAllowed>(_anonymousCaller);
        }

        if (this.GetProviders() is not { } providers)
        {
            return new ValueTask<ExecutionAllowed>(NoProvider(operationType));
        }

        // A provider that reads the operation would judge a default instance here, so the check refuses rather than guess
        foreach (var provider in providers)
        {
            if (provider.ReadsOperation(descriptor))
            {
                return new ValueTask<ExecutionAllowed>(ExecutionAllowed.No(
                    $"{provider.GetType().Name} reads the operation instance to judge {operationType.Name}, so it cannot be judged by a resource key alone; use CanExecuteAsync(IOperation) with the instance",
                    "You do not have permission to perform this operation",
                    ExecutionAllowedFailureType.Authorisation));
            }
        }

        var scopePairs = new ResolvedScopePair[authorization.ScopePairs.Length];

        for (var i = 0; i < scopePairs.Length; i++)
        {
            // A pair with no template resolves to no key at execution, so it is given none here either
            var pair = authorization.ScopePairs[i];
            scopePairs[i] = new ResolvedScopePair(pair.Noun, pair.Verb, pair.ResourceKeyTemplate, pair.ResourceKeyTemplate is null ? null : resourceKey.Value);
        }

        return AskProvidersAsync(
            this.CreateTarget(descriptor, null),
            principal,
            providers,
            ResourceAuthorizationRequest.Execute(descriptor, scopePairs),
            cancellationToken == default ? this.OperationCancelled : cancellationToken);
    }

    /// <summary>
    /// Decides whether this context's caller may execute <paramref name="operation"/>, exactly as its own execution would
    /// decide it: each scope pair's template is expanded from this instance and the caller, as the generated check expands
    /// it, and the providers are given a context whose <see cref="Operation"/> is this instance (Symprex/blueprint#129).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use it in place of <see cref="CanExecuteAsync{TOperation}(ResourceKey, CancellationToken)"/> wherever the instance
    /// is to hand, and always where a provider judges by the operation's properties: given only a key, such a provider reads
    /// a default instance.
    /// </para>
    /// <para>
    /// Every registered <see cref="IResourceAuthorizationProvider"/> is asked, all-of in registration order, in
    /// <see cref="ResourceAuthorizationMode.Execute"/> mode for the operation's descriptor. The context they are given is
    /// nested in this one, as <see cref="CreateNested"/> makes it, and shares this context's caller,
    /// <see cref="UserAuthorisationContext"/>, <see cref="HttpContext"/>, <see cref="BaseUri"/> and <see cref="Data"/>.
    /// The caller, refusals and the denial returned are as for
    /// <see cref="CanExecuteAsync{TOperation}(ResourceKey, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// The instance is judged as given, and is not changed. Execution's population stage, which runs before it authorises
    /// (tenant resolution, <see cref="Middleware.IPropertyPopulator"/>s, <c>[FromAuthContext]</c> and HTTP binding), is not run, so set
    /// any property a template reads that execution would fill to the value execution would give it. One left unset
    /// expands to an empty segment and the key is matched like any other, so it is not guaranteed to be refused: a grant on
    /// an ancestor, such as <c>/accounts/account-9/*</c>, still covers <c>/accounts/account-9/widgets/</c>. One set to
    /// another value is trusted, though execution would overwrite a <c>[FromAuthContext]</c> property, so never build the
    /// instance from untrusted input.
    /// </para>
    /// </remarks>
    /// <param name="operation">The operation asked about, whose type must declare scope pairs.</param>
    /// <param name="cancellationToken">Cancellation for the decision; <see cref="OperationCancelled"/> when not given.</param>
    /// <returns><see cref="ExecutionAllowed.Yes"/>, or the first provider's denial.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="operation"/> is not an operation of <see cref="App"/>, or its type declares no scope pairs.
    /// </exception>
    public ValueTask<ExecutionAllowed> CanExecuteAsync(IOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var operationType = operation.GetType();
        var (descriptor, _) = this.FindJudgedOperation(operationType, nameof(operation));

        if (!descriptor.TryGetFeatureData<OperationExecutionAuthorizationFeatureData>(out var execution))
        {
            throw new ArgumentException(
                $"{operationType.Name} has no generated execution resolver, so its keys cannot be expanded from an instance. Rebuild it with this version of Blueprint's source generator.",
                nameof(operation));
        }

        if (this.UserPrincipal is not { Identity.IsAuthenticated: true } principal)
        {
            return new ValueTask<ExecutionAllowed>(_anonymousCaller);
        }

        if (this.GetProviders() is not { } providers)
        {
            return new ValueTask<ExecutionAllowed>(NoProvider(operationType));
        }

        var target = this.CreateTarget(descriptor, operation);

        return AskProvidersAsync(
            target,
            principal,
            providers,
            ResourceAuthorizationRequest.Execute(descriptor, execution.Resolve(operation, name => ApiAuthoriserAggregator.CallerValue(target, principal, name))),
            cancellationToken == default ? this.OperationCancelled : cancellationToken);
    }

    private static ExecutionAllowed NoProvider(Type operationType)
    {
        return ExecutionAllowed.No(
            $"No IResourceAuthorizationProvider is registered to judge {operationType.Name}",
            "You do not have permission to perform this operation",
            ExecutionAllowedFailureType.Authorisation);
    }

    private static async ValueTask<ExecutionAllowed> AskProvidersAsync(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        IResourceAuthorizationProvider[] providers,
        ResourceAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            var result = await provider.AuthorizeAsync(context, principal, request, cancellationToken).ConfigureAwait(false);

            if (!result.IsAllowed)
            {
                return result;
            }
        }

        return ExecutionAllowed.Yes;
    }

    // The descriptor of an operation CanExecuteAsync is asked about, which must be the app's and declare scope pairs
    private (ApiOperationDescriptor Descriptor, OperationAuthorizationFeatureData Authorization) FindJudgedOperation(Type operationType, string parameterName)
    {
        if (!this.App.TryFindOperation(operationType, out var descriptor))
        {
            throw new ArgumentException($"{operationType.Name} is not an operation of {this.App.GetType().Name}.", parameterName);
        }

        if (!descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorization) || authorization.ScopePairs.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                $"{operationType.Name} declares no scope pairs ([Resource] or [FromResource]), so there is nothing to judge its caller by.",
                parameterName);
        }

        return (descriptor, authorization);
    }

    // Every registered provider, or null when none is, which an execution with scope pairs refuses
    private IResourceAuthorizationProvider[]? GetProviders()
    {
        var providers = this.ServiceProvider.GetServices<IResourceAuthorizationProvider>().ToArray();

        return providers.Length == 0 ? null : providers;
    }

    // The providers see a context for the operation asked about, as at its execution, not the asking one. With no instance
    // given, its operation is the default one its descriptor makes.
    private ApiOperationContext CreateTarget(ApiOperationDescriptor descriptor, object? operation)
    {
        var target = new ApiOperationContext(this.ServiceProvider, this.App, descriptor, this.OperationCancelled)
        {
            Parent = this,
            UserPrincipal = this.UserPrincipal,
            UserAuthorisationContext = this.UserAuthorisationContext,
            HttpContext = this.HttpContext,
            BaseUri = this.BaseUri,
            _data = this.Data,
        };

        if (operation is not null)
        {
            target.Operation = operation;
        }

        return target;
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

    /// <summary>
    /// Gets the <see cref="Microsoft.AspNetCore.Http.HttpContext"/> for the current operation,
    /// throwing <see cref="InvalidOperationException"/> when the operation is not being executed
    /// via the HTTP host. This mirrors the legacy Blueprint <c>GetHttpContext()</c> extension
    /// method for Signature 365 compatibility.
    /// </summary>
    /// <returns>The non-null HTTP context.</returns>
    /// <exception cref="InvalidOperationException">The operation is not HTTP-hosted.</exception>
    public HttpContext GetHttpContext()
    {
        return this.HttpContext ?? throw new InvalidOperationException(
            "HttpContext is not available. This operation is not being executed via the HTTP host.");
    }
}
