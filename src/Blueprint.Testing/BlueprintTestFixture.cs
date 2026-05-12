using Blueprint.Model;
using Blueprint.Runtime.Exec;

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
    /// Gets the in-memory unit of work tracker, or <c>null</c> if not registered.
    /// </summary>
    public InMemoryUnitOfWork? UnitOfWork { get; }

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

    /// <summary>
    /// Executes a query operation and unwraps the result. Convenience alias that mirrors
    /// the S365 <c>QueryAsync</c> pattern.
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
    /// Executes a command operation and unwraps the result. Convenience alias that mirrors
    /// the S365 <c>CommandAsync</c> pattern.
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
    /// Clears all captured test doubles (tasks, messages, unit of work state).
    /// Call between test executions in shared fixture scenarios.
    /// </summary>
    public void ClearCollectors()
    {
        this.Tasks?.ClearAll();
        this.Messages?.ClearAll();
        this.UnitOfWork?.Reset();
    }

    /// <summary>
    /// Wraps the user-supplied configure callback to prepend ambient authentication.
    /// </summary>
    /// <param name="configure">The optional user-supplied callback.</param>
    /// <returns>A callback that applies ambient auth then the user callback.</returns>
    private Action<BlueprintTestContext> WrapConfigure(Action<BlueprintTestContext>? configure)
    {
        return ctx =>
        {
            // Apply ambient auth first — per-call configure can override
            this.Authentication.Apply(ctx);
            configure?.Invoke(ctx);
        };
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
