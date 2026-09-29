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
    /// Gets the inner HTTP client the calls are sent with, as legacy's client exposed it (Signature365
    /// <c>Common.Core/Api/BlueprintHttpClient.cs:47-50</c>), so a caller can read or set its default headers.
    /// </summary>
    public abstract System.Net.Http.HttpClient HttpClient { get; }

    /// <summary>
    /// Adapts a typed <see cref="BlueprintHttpClient{TApp}"/> to the legacy non-generic surface
    /// so that callers holding a <see cref="BlueprintHttpClient"/> reference can dispatch operations
    /// without knowing the concrete app type.
    /// </summary>
    /// <typeparam name="TApp">The <see cref="BlueprintApp"/> subclass that the client targets.</typeparam>
    /// <param name="client">The typed client instance to adapt.</param>
    /// <returns>The same instance widened to the non-generic base type.</returns>
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
    /// <param name="operation">The operation to send (must implement <see cref="IReturn{TResponse}"/>).</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response payload.</returns>
    public async Task<TResponse> CallAsync<TResponse>(
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
    /// <returns>The deserialized response payload.</returns>
    public Task<TResponse> CallAsync<TResponse>(
        IReturn<TResponse> operation,
        CancellationToken cancellationToken = default)
    {
        return this.CallAsync(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Dispatches an operation that completes without a typed payload.
    /// Named <c>SendVoidAsync</c> instead of <c>CallAsync</c> to avoid overload ambiguity with the
    /// generic variant when the operation implements both <see cref="IReturn{T}"/> and <see cref="IOperation"/>.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    public Task SendVoidAsync(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return this.SendVoidAsyncCore(operation, options, cancellationToken);
    }

    /// <summary>
    /// Dispatches an operation that completes without a typed payload and without per-call options.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    public Task SendVoidAsync(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendVoidAsync(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Implements the typed call path for the compatibility surface. Subclasses route this
    /// through the concrete <see cref="BlueprintHttpClient{TApp}"/> send pipeline.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task producing the deserialized response.</returns>
    protected abstract Task<TResponse> CallAsyncCore<TResponse>(
        IReturn<TResponse> operation,
        CallOptions? options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Implements the void send path for the compatibility surface. Subclasses route this
    /// through the concrete <see cref="BlueprintHttpClient{TApp}"/> send pipeline.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional per-call request customization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the remote call succeeds.</returns>
    protected abstract Task SendVoidAsyncCore(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken);
}
