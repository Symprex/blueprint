using Blueprint.Model;

namespace Blueprint.HttpClient;

/// <summary>
/// Compatibility surface for legacy generated clients that were compiled against the old non-generic
/// <c>BlueprintHttpClient</c> type. New Blueprint code should continue to prefer
/// <see cref="BlueprintHttpClient{TApp}"/>, while migration callers can hold a non-generic reference
/// and still dispatch the same <c>CallAsync</c> overloads.
/// </summary>
public abstract class BlueprintHttpClient
{
    /// <summary>
    /// Adapts a typed <see cref="BlueprintHttpClient{TApp}"/> to the legacy non-generic surface.
    /// This keeps factory-based registrations or hand-written gateway constructors small during migration.
    /// </summary>
    /// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
    /// <param name="client">The typed client to expose through the compatibility surface.</param>
    /// <returns>The same client instance, viewed through the non-generic compatibility type.</returns>
    public static BlueprintHttpClient From<TApp>(BlueprintHttpClient<TApp> client)
        where TApp : BlueprintApp
    {
        ArgumentNullException.ThrowIfNull(client);
        return client;
    }

    /// <summary>
    /// Dispatches an operation that returns a typed payload.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response payload, or <c>default</c> for no-content responses.</returns>
    public async Task<TResponse?> CallAsync<TResponse>(
        IReturn<TResponse> operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var callTask = this.CallAsyncCore(operation, options, cancellationToken);
        ArgumentNullException.ThrowIfNull(callTask);
        return await callTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches an operation that returns a typed payload without per-call options.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response payload, or <c>default</c> for no-content responses.</returns>
    public Task<TResponse?> CallAsync<TResponse>(
        IReturn<TResponse> operation,
        CancellationToken cancellationToken = default)
    {
        return this.CallAsync(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Dispatches an operation that completes without a typed payload.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    public Task CallAsync(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return this.CallAsyncCore(operation, options, cancellationToken);
    }

    /// <summary>
    /// Dispatches an operation that completes without a typed payload and without per-call options.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    public Task CallAsync(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.CallAsync(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Implements the typed call path for the compatibility surface.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response payload, which may be <c>null</c>.</returns>
    protected abstract Task<TResponse> CallAsyncCore<TResponse>(
        IReturn<TResponse> operation,
        CallOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Implements the void call path for the compatibility surface.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    protected abstract Task CallAsyncCore(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken);
}
