using System.Buffers;
using System.Net;

namespace Blueprint.Model;

/// <summary>
/// Content that sends a stream a read at a time, writing and flushing each read before making the next, so the receiver
/// gets the body as the stream yields it, whatever the stream's own <c>CopyToAsync</c> would do. See
/// <see cref="HttpRequestBodies.CreateStreamContent"/>.
/// </summary>
/// <remarks>
/// A 307 or 308 redirect, or a retrying handler, sends a request's content again. A seekable stream is sent again from where
/// it stood when the content was made, so every send is the same body of the same length. A stream that cannot seek has
/// nothing left to send after the first, so a second send fails rather than sending an empty or truncated body.
/// </remarks>
internal sealed class ForwardingStreamContent : HttpContent
{
    private const int BufferSize = 81920;

    private readonly Stream _source;
    private readonly long _start;
    private bool _sent;

    /// <summary>
    /// Initialises a new instance of the <see cref="ForwardingStreamContent"/> class.
    /// </summary>
    /// <param name="source">The stream to send, from its current position, which this content owns and disposes.</param>
    public ForwardingStreamContent(Stream source)
    {
        this._source = source;
        this._start = source.CanSeek ? source.Position : 0;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return this.SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        if (this._sent)
        {
            if (!this._source.CanSeek)
            {
                throw new InvalidOperationException(
                    "This request body is a stream that cannot seek and has already been sent, so it cannot be sent again (by a redirect or a retry).");
            }

            this._source.Position = this._start;
        }

        this._sent = true;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int read;
            while ((read = await this._source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
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
        // A memory or file stream's Length is its whole content, the same for every send from where it started. Being able to
        // seek is not enough: a request body after EnableBuffering() seeks, but its Length is only what it has buffered so far,
        // which used to be advertised as the Content-Length of a body then sent whole. Any other stream is sent chunked.
        if (this._source is MemoryStream or FileStream && this._source.CanSeek)
        {
            length = this._source.Length - this._start;
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
            this._source.Dispose();
        }

        base.Dispose(disposing);
    }
}
