using System.Buffers;
using System.Net;

namespace Blueprint.Model;

/// <summary>
/// Content that sends a stream a read at a time, writing and flushing each read before making the next, so the receiver
/// gets the body as the stream yields it, whatever the stream's own <c>CopyToAsync</c> would do. See
/// <see cref="HttpRequestBodies.CreateStreamContent"/>.
/// </summary>
/// <param name="source">The stream to send, which this content owns and disposes.</param>
internal sealed class ForwardingStreamContent(Stream source) : HttpContent
{
    private const int BufferSize = 81920;

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return this.SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        // A seekable stream, such as a file, says how much is left; any other is sent chunked, as it is read.
        if (source.CanSeek)
        {
            length = source.Length - source.Position;
            return true;
        }

        length = 0;
        return false;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            source.Dispose();
        }

        base.Dispose(disposing);
    }
}
