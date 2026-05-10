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
        configure?.Invoke(testContext);

        return await this.Executor.ExecuteAsync(context);
    }

    /// <summary>
    /// Executes the given operation and unwraps the result to the expected type.
    /// Throws <see cref="BlueprintTestExecutionException"/> if the result cannot be unwrapped
    /// (e.g., if validation failed unexpectedly).
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
