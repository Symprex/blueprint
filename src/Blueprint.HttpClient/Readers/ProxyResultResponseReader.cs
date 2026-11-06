using Blueprint.Http;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a forward's success response as a <see cref="ProxyResult"/> over the response itself, unread: the transport hands
/// the response over at its headers, and the result streams the body to the gateway's caller and disposes the response.
/// </summary>
/// <remarks>
/// Chosen only by <see cref="ForwardResponseReaders"/>, for a forward declaring <see cref="ProxyResult"/> whatever the inner
/// operation returns, so a stream or file result is forwarded as it arrives, never buffered (D18). A failure is mapped by
/// the transport as for any call, so an upstream problem is read and thrown rather than streamed.
/// </remarks>
internal sealed class ProxyResultResponseReader : ResponseReader<ProxyResult>
{
    /// <inheritdoc />
    internal override bool TakesResponse => true;

    /// <inheritdoc />
    internal override ValueTask<ProxyResult> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new ProxyResult(context.Response));
    }
}
