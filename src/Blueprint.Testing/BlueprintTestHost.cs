using System.Security.Claims;
using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.Testing;

/// <summary>
/// Provides a lightweight, in-memory host for executing Blueprint operations through the source-generated
/// pipeline without requiring an HTTP server. This is the primary entry point for consumer unit and
/// integration tests that need to exercise operations end-to-end through the compiled pipeline.
/// <para>
/// The host manages DI scopes, context creation, and result unwrapping so test code can focus on
/// operation setup and result assertions rather than infrastructure wiring.
/// </para>
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass discovered by <c>[BlueprintAppScan]</c>.</typeparam>
/// <typeparam name="TExec">The source-generated executor type that registers per-operation pipelines.</typeparam>
public sealed class BlueprintTestHost<TApp, TExec> : IDisposable, IAsyncDisposable
    where TApp : BlueprintApp
    where TExec : BlueprintAppExecutor
{
    private readonly ServiceProvider _rootProvider;
    private bool _disposed;

    /// <summary>
    /// Gets the app model that describes all registered operations.
    /// </summary>
    public TApp App { get; }

    /// <summary>
    /// Gets the source-generated executor that owns per-operation pipelines.
    /// </summary>
    public TExec Executor { get; }

    /// <summary>
    /// Gets the root service provider. Prefer the <c>ExecuteAsync</c> methods
    /// which create a scoped provider automatically.
    /// </summary>
    public IServiceProvider Services => this._rootProvider;

    /// <summary>
    /// Gets the ambient authentication manager. Set a principal here to have it applied
    /// automatically to every subsequent operation execution. This eliminates the need
    /// for per-call <c>ctx.WithUser(...)</c> in tests that run many operations under
    /// the same identity.
    /// <para>
    /// <strong>S365 migration note</strong>: this replaces <c>ApiFixture.Authentication</c>.
    /// Use <c>Authentication.Set(identity)</c> for permanent auth or
    /// <c>using (host.Authentication.As(identity))</c> for scoped auth, exactly as before.
    /// </para>
    /// </summary>
    public TestAuthenticationManager Authentication { get; } = new();

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintTestHost{TApp,TExec}"/> class.
    /// </summary>
    /// <param name="rootProvider">The DI root service provider that owns the host's lifetime.</param>
    /// <param name="app">The Blueprint app metadata.</param>
    /// <param name="executor">The source-generated executor.</param>
    internal BlueprintTestHost(ServiceProvider rootProvider, TApp app, TExec executor)
    {
        this._rootProvider = rootProvider;
        this.App = app;
        this.Executor = executor;
    }

    /// <summary>
    /// Executes the given operation through the source-generated pipeline, returning the raw
    /// <see cref="OperationResult"/>. A fresh DI scope is created per execution.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution,
    /// for example to set up user identity or register test doubles.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public async Task<OperationResult> ExecuteAsync(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var operationType = operation.GetType();
        var descriptor = this.App.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is included in the [BlueprintAppScan] assembly.");

        using var scope = this._rootProvider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var context = new ApiOperationContext(
            scope.ServiceProvider,
            this.App,
            descriptor,
            cancellationToken)
        {
            Operation = operation,
            HttpContext = httpContext,
        };

        var testContext = new BlueprintTestContext(context, scope.ServiceProvider);

        // Apply ambient authentication before user's callback so per-call overrides take precedence
        this.Authentication.Apply(testContext);

        configure?.Invoke(testContext);

        return await this.Executor.ExecuteAsync(context);
    }

    /// <summary>
    /// Executes the given operation and unwraps the result to the expected type.
    /// Throws <see cref="BlueprintTestExecutionException"/> if the result cannot be unwrapped
    /// (e.g., if validation failed unexpectedly).
    /// <para>
    /// <strong>Exception propagation</strong>: when the pipeline produces an
    /// <see cref="UnhandledExceptionResult"/>, the original exception is rethrown directly
    /// so callers can catch the specific exception type. This matches the S365 pattern
    /// where <c>ThrowApiExceptionAsync()</c> catches <see cref="ApiException"/> from
    /// handler code.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped result.</returns>
    public async Task<TResult> ExecuteAsync<TResult>(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var result = await this.ExecuteAsync(operation, configure, cancellationToken);

        // Rethrow handler exceptions directly so test code can catch specific exception
        // types (e.g. ApiException, ValidationException) without unwrapping result wrappers.
        if (result is UnhandledExceptionResult unhandled)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unhandled.Exception).Throw();
        }

        if (result is ValidationFailedResult validationFailed)
        {
            throw new BlueprintTestExecutionException(
                $"Expected result of type {typeof(TResult).Name} but got ValidationFailedResult. " +
                $"Validation errors: {FormatValidationErrors(validationFailed)}",
                result);
        }

        try
        {
            return result.Unwrap<TResult>();
        }
        catch (InvalidOperationException ex)
        {
            throw new BlueprintTestExecutionException(
                $"Could not unwrap result of type {result.GetType().Name} to {typeof(TResult).Name}. " +
                $"Actual result: {result}",
                result,
                ex);
        }
    }

    /// <summary>
    /// Executes the given operation and expects a <see cref="ValidationFailedResult"/>.
    /// Throws if the result is NOT a validation failure.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation failure result for further assertions.</returns>
    public async Task<ValidationFailedResult> ExecuteExpectingValidationFailureAsync(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var result = await this.ExecuteAsync(operation, configure, cancellationToken);

        return result as ValidationFailedResult
            ?? throw new BlueprintTestExecutionException(
                $"Expected ValidationFailedResult but got {result.GetType().Name}.",
                result);
    }

    /// <summary>
    /// Executes a query operation and unwraps the result to the expected type.
    /// This is a convenience alias for <see cref="ExecuteAsync{TResult}"/> that mirrors
    /// the S365 <c>QueryAsync</c> test pattern to minimise migration churn.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="query">The query operation instance.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped query result.</returns>
    public Task<TResult> QueryAsync<TResult>(
        IOperation query,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync<TResult>(query, configure, cancellationToken);
    }

    /// <summary>
    /// Executes a command operation and unwraps the result to the expected type.
    /// This is a convenience alias for <see cref="ExecuteAsync{TResult}"/> that mirrors
    /// the S365 <c>CommandAsync</c> test pattern to minimise migration churn.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="command">The command operation instance.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped command result.</returns>
    public Task<TResult> CommandAsync<TResult>(
        IOperation command,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync<TResult>(command, configure, cancellationToken);
    }

    /// <summary>
    /// Executes a command operation that has no meaningful result payload (e.g., commands that
    /// return <c>NoResultOperationResult</c> or <c>AcceptedResult</c>). Returns the raw
    /// <see cref="OperationResult"/> for status assertions.
    /// </summary>
    /// <param name="command">The command operation instance.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> CommandAsync(
        IOperation command,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this.ExecuteAsync(command, configure, cancellationToken);
    }

    /// <summary>
    /// Executes a message (a published event or command that would be dispatched by a message bus)
    /// through the pipeline as an anonymous user. This is a migration shim for S365's
    /// <c>ApiFixture.MessageAsync(object, Action, int)</c>.
    /// <para>
    /// Messages are always executed without authentication context, matching the behaviour of
    /// bus-dispatched message handlers in production. If <paramref name="retryAttempts"/> is
    /// greater than zero, execution retries on unhandled exceptions with a 1-second delay.
    /// </para>
    /// </summary>
    /// <param name="message">The message/event instance to dispatch. Must be a registered operation.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="retryAttempts">
    /// Number of retry attempts if the handler throws. Default is 0 (no retries).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the message has been handled.</returns>
    public async Task MessageAsync(
        IOperation message,
        Action<BlueprintTestContext>? configure = null,
        int retryAttempts = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        Action<BlueprintTestContext> messageConfig = ctx =>
        {
            // Messages are always anonymous (no auth context)
            ctx.AsAnonymous();
            configure?.Invoke(ctx);
        };

        OperationResult result;

        if (retryAttempts <= 0)
        {
            result = await this.ExecuteAsync(message, messageConfig, cancellationToken);
        }
        else
        {
            var attempt = 0;

            while (true)
            {
                result = await this.ExecuteAsync(message, messageConfig, cancellationToken);

                if (result is not UnhandledExceptionResult || attempt >= retryAttempts)
                {
                    break;
                }

                attempt++;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        // Force exceptions to propagate (matches S365 MessageAsync behaviour)
        if (result is UnhandledExceptionResult unhandled)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unhandled.Exception).Throw();
        }

        if (result is ValidationFailedResult validationFailed)
        {
            throw new BlueprintTestExecutionException(
                $"MessageAsync failed: validation errors occurred while handling {message.GetType().Name}.",
                validationFailed);
        }
    }

    /// <summary>
    /// Executes a background task through the pipeline without authentication context.
    /// This is a migration shim for S365's <c>ApiFixture.BackgroundTaskAsync(IBackgroundTask, Action)</c>.
    /// <para>
    /// Background tasks are always executed without authentication, matching the production
    /// behaviour where scheduled/recurring tasks run outside a user request context.
    /// </para>
    /// </summary>
    /// <param name="task">The background task operation instance.</param>
    /// <param name="configure">
    /// An optional callback to configure the <see cref="BlueprintTestContext"/> before execution.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the background task has been handled.</returns>
    public async Task BackgroundTaskAsync(
        IOperation task,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var result = await this.ExecuteAsync(
            task,
            ctx =>
            {
                // Background tasks never have an authentication context
                ctx.AsAnonymous();
                configure?.Invoke(ctx);
            },
            cancellationToken);

        // Force exceptions to propagate (matches S365 BackgroundTaskAsync behaviour)
        if (result is UnhandledExceptionResult unhandled)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(unhandled.Exception).Throw();
        }

        if (result is ValidationFailedResult validationFailed)
        {
            throw new BlueprintTestExecutionException(
                $"BackgroundTaskAsync failed: validation errors occurred while handling {task.GetType().Name}.",
                validationFailed);
        }
    }

    /// <summary>
    /// Executes an operation with an authenticated user built from the provided claims.
    /// This is a direct migration shim for S365's <c>TestApiOperationExecutor.ExecuteWithAuth(operation, claims...)</c>.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="claims">One or more claims to build the authenticated identity from.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> ExecuteWithAuth(
        IOperation operation,
        params Claim[] claims)
    {
        return this.ExecuteAsync(operation, ctx => ctx.WithUser(c =>
        {
            foreach (var claim in claims)
            {
                c.Add(claim);
            }
        }));
    }

    /// <summary>
    /// Executes an operation with an authenticated user built from the provided claims and
    /// unwraps the result to the expected type. This is a direct migration shim for S365's
    /// <c>TestApiOperationExecutor.ExecuteWithAuth(operation, claims...)</c>.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="claims">One or more claims to build the authenticated identity from.</param>
    /// <returns>The unwrapped result.</returns>
    public Task<TResult> ExecuteWithAuth<TResult>(
        IOperation operation,
        params Claim[] claims)
    {
        return this.ExecuteAsync<TResult>(operation, ctx => ctx.WithUser(c =>
        {
            foreach (var claim in claims)
            {
                c.Add(claim);
            }
        }));
    }

    private static string FormatValidationErrors(ValidationFailedResult validationFailed)
    {
        var errors = validationFailed.Errors;

        if (errors is null || errors.Count == 0)
        {
            return "(no errors)";
        }

        return string.Join("; ", errors.Select(static e => $"[{e.Key}] {string.Join(", ", e.Value)}"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!this._disposed)
        {
            this._rootProvider.Dispose();
            this._disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!this._disposed)
        {
            await this._rootProvider.DisposeAsync();
            this._disposed = true;
        }
    }
}
