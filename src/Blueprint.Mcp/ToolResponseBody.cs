namespace Blueprint.Mcp;

/// <summary>
/// The body of the response an operation's endpoint writes for a tool call. A text body is kept, up to a limit, to be
/// the tool's result; any other body, and text past the limit, is only counted, as a tool result does not carry it.
/// </summary>
/// <remarks>
/// Whether the body is text is decided by the response's content type at its first write, by which time an endpoint,
/// and any <c>OnStarting</c> callback, has set it.
/// </remarks>
internal sealed class ToolResponseBody : Stream
{
    private readonly Func<string?> _contentType;
    private readonly int _maxTextBytes;
    private readonly Func<CancellationToken, Task>? _start;
    private readonly bool _discard;
    private MemoryStream? _text;
    private bool _decided;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResponseBody"/> class.
    /// </summary>
    /// <param name="contentType">Gives the response's content type.</param>
    /// <param name="maxTextBytes">The most bytes of a text body kept.</param>
    /// <param name="start">Starts the response, as a server does before it writes the first byte of its body.</param>
    /// <param name="discard">Whether every byte written is discarded uncounted, as a server discards a HEAD response's body.</param>
    public ToolResponseBody(Func<string?> contentType, int maxTextBytes, Func<CancellationToken, Task>? start = null, bool discard = false)
    {
        this._contentType = contentType;
        this._maxTextBytes = maxTextBytes;
        this._start = start;
        this._discard = discard;
    }

    /// <summary>Gets the number of bytes written, kept or not.</summary>
    public long BytesWritten { get; private set; }

    /// <summary>Gets the most bytes of a text body kept.</summary>
    public int MaxTextBytes => this._maxTextBytes;

    /// <summary>Gets whether the body is text, so that it is kept, up to <see cref="MaxTextBytes"/>.</summary>
    public bool IsText => this._text is not null;

    /// <summary>Gets whether every byte written was kept: the body was text, within the limit.</summary>
    public bool IsComplete => this._text is not null && this._text.Length == this.BytesWritten;

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Gets the bytes kept.</summary>
    /// <returns>The text body, or as much of it as the limit allows; empty for a body that is not text.</returns>
    public ReadOnlyMemory<byte> GetText()
    {
        return this._text is null ? ReadOnlyMemory<byte>.Empty : this._text.GetBuffer().AsMemory(0, (int)this._text.Length);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        this.Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!this._decided)
        {
            this._start?.Invoke(CancellationToken.None).GetAwaiter().GetResult();
            this.Decide();
        }

        this.Keep(buffer);
    }

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!this._decided && this._start is not null)
        {
            await this._start(cancellationToken).ConfigureAwait(false);
        }

        this.Decide();
        this.Keep(buffer.Span);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private void Decide()
    {
        if (this._decided)
        {
            return;
        }

        this._decided = true;

        if (OperationToolResult.IsText(this._contentType()))
        {
            this._text = new MemoryStream();
        }
    }

    private void Keep(ReadOnlySpan<byte> buffer)
    {
        if (this._discard)
        {
            return;
        }

        this.BytesWritten += buffer.Length;

        if (this._text is not null && this._text.Length < this._maxTextBytes)
        {
            this._text.Write(buffer[..(int)Math.Min(buffer.Length, this._maxTextBytes - this._text.Length)]);
        }
    }

    /// <summary>
    /// Starts the response, as a server does when a body is flushed, so its <c>OnStarting</c> callbacks run and it has
    /// started, though nothing has been written.
    /// </summary>
    public override void Flush()
    {
        this._start?.Invoke(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Starts the response, as a server does when a body is flushed, so its <c>OnStarting</c> callbacks run and it has
    /// started, though nothing has been written.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes once the response has started.</returns>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return this._start?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._text?.Dispose();
        }

        base.Dispose(disposing);
    }
}
