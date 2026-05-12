using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Tasks;
using Microsoft.Extensions.Options;

namespace Blueprint.Testing;

/// <summary>
/// A high-level test fixture that wraps <see cref="BlueprintTestHost{TApp,TExec}"/> with ambient
/// authentication, direct test-double accessors, and convenience methods that minimise per-test
/// boilerplate. This matches the Signature 365 <c>ApiFixture</c> shape so migration churn is
/// limited to fixture construction rather than every test method.
/// <para>
/// <strong>Ambient auth</strong>: set <see cref="Authentication"/> once in the fixture and it
/// applies automatically to every <c>ExecuteAsync</c>/<c>QueryAsync</c>/<c>CommandAsync</c> call.
/// Per-call overrides via the <c>configure</c> callback take precedence.
/// </para>
/// <para>
/// <strong>Test doubles</strong>: <see cref="Tasks"/>, <see cref="Messages"/>, and
/// <see cref="UnitOfWork"/> are auto-registered when created via
/// <see cref="BlueprintTestHostBuilder{TApp,TExec}.WithTestDefaults"/> and exposed as properties
/// for direct assertion without DI lookups.
/// </para>
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass discovered by <c>[BlueprintAppScan]</c>.</typeparam>
/// <typeparam name="TExec">The source-generated executor type.</typeparam>
public sealed class BlueprintTestFixture<TApp, TExec> : IDisposable, IAsyncDisposable
    where TApp : BlueprintApp
    where TExec : BlueprintAppExecutor
{
    private readonly BlueprintTestHost<TApp, TExec> _host;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintTestFixture{TApp,TExec}"/> class.
    /// </summary>
    /// <param name="host">The underlying test host that owns the DI container and executor.</param>
    internal BlueprintTestFixture(BlueprintTestHost<TApp, TExec> host)
    {
        this._host = host;
        this.Authentication = new TestAuthenticationManager();

        // Resolve optional test doubles from DI — null when not registered
        this.Tasks = host.Services.GetService(typeof(BackgroundTaskCollector)) as BackgroundTaskCollector;
        this.Messages = host.Services.GetService(typeof(TestMessageCollector)) as TestMessageCollector;
        this.UnitOfWork = host.Services.GetService(typeof(InMemoryUnitOfWork)) as InMemoryUnitOfWork;
    }

    /// <summary>
    /// Gets the ambient authentication manager. Set the default user once and it applies
    /// to all operation executions without needing per-call configuration.
    /// </summary>
    public TestAuthenticationManager Authentication { get; }

    /// <summary>
    /// Gets the root service provider for resolving services directly.
    /// </summary>
    public IServiceProvider Services => this._host.Services;

    /// <summary>
    /// Gets the app model describing all registered operations.
    /// </summary>
    public TApp App => this._host.App;

    /// <summary>
    /// Gets the captured background tasks, or <c>null</c> if <see cref="BackgroundTaskCollector"/>
    /// was not registered (use <see cref="BlueprintTestHostBuilder{TApp,TExec}.WithTestDefaults"/>
    /// to auto-register).
    /// </summary>
    public BackgroundTaskCollector? Tasks { get; }

    /// <summary>
    /// Gets the captured messages (publish/send), or <c>null</c> if <see cref="TestMessageCollector"/>
    /// was not registered.
    /// </summary>
    public TestMessageCollector? Messages { get; }

    /// <summary>
    /// Alias for <see cref="Messages"/> that mirrors the S365 <c>ApiFixture.Bus</c> property,
    /// reducing churn when migrating tests that use <c>api.Bus.Should().HavePublished&lt;T&gt;()</c>.
    /// </summary>
    public TestMessageCollector? Bus => this.Messages;

    /// <summary>
    /// Gets the in-memory unit of work tracker, or <c>null</c> if not registered.
    /// </summary>
    public InMemoryUnitOfWork? UnitOfWork { get; }

    // ──────────────────────────────────────────────
    //  Core execution methods
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes the given operation through the source-generated pipeline, returning the raw
    /// <see cref="OperationResult"/>. Ambient authentication is applied automatically before
    /// any per-call <paramref name="configure"/> callback.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> ExecuteAsync(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync(operation, this.WrapConfigure(configure), cancellationToken);
    }

    /// <summary>
    /// Executes the given operation and unwraps the result to the expected type.
    /// Throws <see cref="BlueprintTestExecutionException"/> if unwrapping fails.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped result.</returns>
    public Task<TResult> ExecuteAsync<TResult>(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync<TResult>(operation, this.WrapConfigure(configure), cancellationToken);
    }

    /// <summary>
    /// Executes the given operation and expects a <see cref="ValidationFailedResult"/>.
    /// Throws if the result is NOT a validation failure.
    /// </summary>
    /// <param name="operation">The operation instance to execute.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation failure result for further assertions.</returns>
    public Task<ValidationFailedResult> ExecuteExpectingValidationFailureAsync(
        IOperation operation,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteExpectingValidationFailureAsync(operation, this.WrapConfigure(configure), cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  QueryAsync — typed and untyped overloads
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes a query operation and unwraps the result. Accepts any <see cref="IOperation"/>.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="query">The query operation instance.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped query result.</returns>
    public Task<TResult> QueryAsync<TResult>(
        IOperation query,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync<TResult>(query, this.WrapConfigure(configure), cancellationToken);
    }

    /// <summary>
    /// Executes a query operation constrained to <see cref="IQuery{TResult}"/> and unwraps the result.
    /// This overload matches the S365 <c>ApiFixture.QueryAsync&lt;TResult&gt;(IQuery&lt;TResult&gt;)</c>
    /// signature so migrated tests compile without changes.
    /// </summary>
    /// <typeparam name="TResult">The query result type.</typeparam>
    /// <param name="query">The strongly-typed query.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped query result.</returns>
    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync<TResult>(query, this.WrapConfigure(configure), cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  CommandAsync — typed and untyped overloads
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes a command operation and unwraps the result. Accepts any <see cref="IOperation"/>.
    /// </summary>
    /// <typeparam name="TResult">The expected result type.</typeparam>
    /// <param name="command">The command operation instance.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped command result.</returns>
    public Task<TResult> CommandAsync<TResult>(
        IOperation command,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync<TResult>(command, this.WrapConfigure(configure), cancellationToken);
    }

    /// <summary>
    /// Executes a command constrained to <see cref="ICommand{TResult}"/> and unwraps the result.
    /// This overload matches the S365 <c>ApiFixture.CommandAsync&lt;TResult&gt;(ICommand&lt;TResult&gt;)</c>
    /// signature so migrated tests compile without changes.
    /// </summary>
    /// <typeparam name="TResult">The command result type.</typeparam>
    /// <param name="command">The strongly-typed command.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unwrapped command result.</returns>
    public Task<TResult> CommandAsync<TResult>(
        ICommand<TResult> command,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync<TResult>(command, this.WrapConfigure(configure), cancellationToken);
    }

    /// <summary>
    /// Executes a command with no meaningful result payload and returns the raw
    /// <see cref="OperationResult"/> for status assertions.
    /// </summary>
    /// <param name="command">The command operation instance.</param>
    /// <param name="configure">Optional per-call context configuration (applied after ambient auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> CommandAsync(
        IOperation command,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync(command, this.WrapConfigure(configure), cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  BackgroundTaskAsync — clears auth by default
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes a background task operation through the pipeline. Background tasks run without
    /// authentication context — the ambient auth identity is temporarily cleared for the duration
    /// of the call, matching the S365 <c>BackgroundTaskAsync</c> behavior where
    /// <c>ClaimsIdentity</c> and <c>UserAuthorisationContext</c> are nulled.
    /// </summary>
    /// <param name="task">The background task operation instance.</param>
    /// <param name="configure">Optional per-call context configuration (applied after auth clearing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> BackgroundTaskAsync(
        IOperation task,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync(task, this.WrapConfigureAnonymous(configure), cancellationToken);
    }

    /// <summary>
    /// Executes a background task constrained to <see cref="IBackgroundTask"/>. This overload
    /// matches the S365 <c>ApiFixture.BackgroundTaskAsync(IBackgroundTask)</c> signature.
    /// Authentication is automatically cleared for the execution.
    /// </summary>
    /// <param name="task">The strongly-typed background task.</param>
    /// <param name="configure">Optional per-call context configuration (applied after auth clearing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public Task<OperationResult> BackgroundTaskAsync(
        IBackgroundTask task,
        Action<BlueprintTestContext>? configure = null,
        CancellationToken cancellationToken = default)
    {
        return this._host.ExecuteAsync(task, this.WrapConfigureAnonymous(configure), cancellationToken);
    }

    // ──────────────────────────────────────────────
    //  MessageAsync — with optional retry
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes a message/event handler operation through the pipeline, returning the raw
    /// <see cref="OperationResult"/>. Messages execute anonymously (no authentication) matching
    /// the S365 pattern where message handlers run as system operations. Failed results
    /// (<see cref="ValidationFailedResult"/> or <see cref="UnhandledExceptionOperationResult"/>)
    /// automatically throw so tests fail fast.
    /// </summary>
    /// <param name="message">The message operation instance.</param>
    /// <param name="configure">Optional per-call context configuration (applied after auth clearing).</param>
    /// <param name="retryAttempts">
    /// Number of retry attempts for transient failures (default 0 = no retries). When greater
    /// than zero, <see cref="UnhandledExceptionOperationResult"/> triggers a retry with 1-second
    /// constant back-off. Useful for concurrency tests.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw operation result.</returns>
    public async Task<OperationResult> MessageAsync(
        IOperation message,
        Action<BlueprintTestContext>? configure = null,
        int retryAttempts = 0,
        CancellationToken cancellationToken = default)
    {
        var wrappedConfigure = this.WrapConfigureAnonymous(configure);

        OperationResult result;

        if (retryAttempts <= 0)
        {
            result = await this._host.ExecuteAsync(message, wrappedConfigure, cancellationToken);
        }
        else
        {
            result = await ExecuteWithRetryAsync(
                () => this._host.ExecuteAsync(message, wrappedConfigure, cancellationToken),
                retryAttempts,
                cancellationToken);
        }

        if (result is ValidationFailedResult or UnhandledExceptionOperationResult)
        {
            result.Unwrap<object>();
        }

        return result;
    }

    // ──────────────────────────────────────────────
    //  Service and configuration helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Resolves a service from the root DI container.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The resolved service.</returns>
    public T GetRequiredService<T>() where T : notnull
    {
        return this.Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Resolves <see cref="IOptions{T}"/> from the root DI container and returns the
    /// <see cref="IOptions{T}.Value"/>. Matches the S365 <c>ApiFixture.GetOptions&lt;T&gt;()</c> pattern.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The configured options value.</returns>
    public T GetOptions<T>() where T : class
    {
        return this.Services.GetRequiredService<IOptions<T>>().Value;
    }

    /// <summary>
    /// Resolves a service by its interface type and casts to the expected implementation type.
    /// This is a convenience shorthand for the test-double pattern where a concrete type
    /// (e.g. <c>FakeBus</c>) is registered as an interface (e.g. <c>IBus</c>) and test code
    /// needs the concrete type for assertions.
    /// <para>
    /// Example: <c>var fakeBus = fixture.GetServiceAs&lt;IBus, FakeBus&gt;();</c>
    /// </para>
    /// </summary>
    /// <typeparam name="TService">The service interface type to resolve from DI.</typeparam>
    /// <typeparam name="TImpl">The expected implementation type to cast to.</typeparam>
    /// <returns>The resolved service cast to <typeparamref name="TImpl"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resolved service is not of type <typeparamref name="TImpl"/>
    /// or is not registered.
    /// </exception>
    public TImpl GetServiceAs<TService, TImpl>()
        where TService : notnull
        where TImpl : class, TService
    {
        var service = this.Services.GetRequiredService<TService>();

        return service as TImpl
            ?? throw new InvalidOperationException(
                $"Expected service {typeof(TService).Name} to be of type {typeof(TImpl).Name}, " +
                $"but the registered implementation is {service.GetType().Name}.");
    }

    /// <summary>
    /// Clears all captured test doubles (tasks, messages, unit of work state).
    /// Call between test executions in shared fixture scenarios.
    /// </summary>
    public void ClearCollectors()
    {
        this.Tasks?.ClearAll();
        this.Messages?.ClearAll();
        this.UnitOfWork?.Reset();
    }

    // ──────────────────────────────────────────────
    //  Message dispatch helpers (for integration tests)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Re-executes all published messages through the pipeline, simulating bus delivery.
    /// Each published message is executed via <see cref="MessageAsync"/> (anonymous, no auth).
    /// Failures from individual messages are silently swallowed to mirror the S365
    /// <c>RunAllPublishedMessagesAsync</c> pattern where bus handlers tolerate individual failures.
    /// </summary>
    /// <returns>The published messages that were dispatched.</returns>
    public async Task<IReadOnlyList<TestMessageCollector.MessageEnvelope>> RunAllPublishedMessagesAsync()
    {
        if (this.Messages == null)
        {
            return Array.Empty<TestMessageCollector.MessageEnvelope>();
        }

        var published = this.Messages.Published.ToList();

        foreach (var envelope in published)
        {
            if (envelope.Message is IOperation operation)
            {
                try
                {
                    await this.MessageAsync(operation).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow: mirrors S365 pattern where individual handler failures don't
                    // stop the remaining messages from being dispatched.
                }
            }
        }

        return published;
    }

    /// <summary>
    /// Re-executes all sent (point-to-point) messages through the pipeline, simulating
    /// local bus delivery. Each sent message is executed via <see cref="MessageAsync"/>.
    /// </summary>
    /// <returns>The sent messages that were dispatched.</returns>
    public async Task<IReadOnlyList<TestMessageCollector.MessageEnvelope>> RunAllSentMessagesAsync()
    {
        if (this.Messages == null)
        {
            return Array.Empty<TestMessageCollector.MessageEnvelope>();
        }

        var sent = this.Messages.Sent.ToList();

        foreach (var envelope in sent)
        {
            if (envelope.Message is IOperation operation)
            {
                await this.MessageAsync(operation).ConfigureAwait(false);
            }
        }

        return sent;
    }

    /// <summary>
    /// Re-executes all published messages and then clears the message collector, preventing
    /// re-processing on subsequent calls. This matches the S365
    /// <c>RunAndConsumeAllPublishedMessagesAsync</c> pattern for multi-step integration tests.
    /// </summary>
    /// <returns>The published messages that were dispatched.</returns>
    public async Task<IReadOnlyList<TestMessageCollector.MessageEnvelope>> RunAndConsumeAllPublishedMessagesAsync()
    {
        var published = await this.RunAllPublishedMessagesAsync().ConfigureAwait(false);
        this.Messages?.ClearAll();
        return published;
    }

    // ──────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────

    private static readonly Action<BlueprintTestContext> _noOp = static _ => { };

    /// <summary>
    /// Wraps the user-supplied configure callback to prepend ambient authentication.
    /// </summary>
    private Action<BlueprintTestContext> WrapConfigure(Action<BlueprintTestContext>? configure)
    {
        return ctx =>
        {
            this.Authentication.Apply(ctx);
            configure?.Invoke(ctx);
        };
    }

    /// <summary>
    /// Wraps the user-supplied configure callback with anonymous execution semantics:
    /// no ambient auth is applied. Used by background task and message methods where the
    /// operation runs without a user identity.
    /// </summary>
    private Action<BlueprintTestContext> WrapConfigureAnonymous(Action<BlueprintTestContext>? configure)
    {
        return configure ?? _noOp;
    }

    /// <summary>
    /// Executes an async operation with simple constant-delay retry for
    /// <see cref="UnhandledExceptionOperationResult"/> outcomes.
    /// </summary>
    private static async Task<OperationResult> ExecuteWithRetryAsync(
        Func<Task<OperationResult>> execute,
        int maxRetryAttempts,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            var result = await execute();

            if (result is not UnhandledExceptionOperationResult || attempt >= maxRetryAttempts)
            {
                return result;
            }

            attempt++;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this._host.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._host.DisposeAsync();
    }
}
