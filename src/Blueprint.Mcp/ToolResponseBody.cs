namespace Blueprint.Mcp;

/// <summary>
/// The body of the response an operation's endpoint writes for a tool call. A text body is kept, up to a limit, to be
/// the tool's result; any other body, and text past the limit, is only counted, as a tool result does not carry it.
/// </summary>
/// <remarks>
/// Whether the body is text is decided by the response's content type at its first write, by which time an endpoint
/// has set it.
/// </remarks>
internal sealed class ToolResponseBody : Stream
{
    private readonly Func<string?> _contentType;
    private readonly int _maxTextBytes;
    private MemoryStream? _text;
    private bool _decided;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResponseBody"/> class.
    /// </summary>
    /// <param name="contentType">Gives the response's content type.</param>
    /// <param name="maxTextBytes">The most bytes of a text body kept.</param>
    public ToolResponseBody(Func<string?> contentType, int maxTextBytes)
    {
        this._contentType = contentType;
        this._maxTextBytes = maxTextBytes;
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
            this._decided = true;

            if (OperationToolResult.IsText(this._contentType()))
            {
                this._text = new MemoryStream();
            }
        }

        this.BytesWritten += buffer.Length;

        if (this._text is not null && this._text.Length < this._maxTextBytes)
        {
            this._text.Write(buffer[..(int)Math.Min(buffer.Length, this._maxTextBytes - this._text.Length)]);
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        this.Write(buffer.AsSpan(offset, count));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.Write(buffer.Span);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
