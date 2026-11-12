using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace Blueprint.Mcp;

/// <summary>
/// The response an operation's endpoint writes for a tool call, standing in for the server's: its status, headers and
/// body, and the callbacks a server runs. <c>OnStarting</c> callbacks run, last registered first, before the first
/// byte of the body is written or when the response completes without one, and <c>OnCompleted</c> callbacks, which
/// include the resources registered for disposal, run once the call has its result.
/// </summary>
internal sealed class ToolResponse : IHttpResponseFeature, IHttpResponseBodyFeature, IDisposable
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
    private readonly List<(Func<object, Task> Callback, object State)> _onCompleted = [];
    private PipeWriter? _writer;
    private bool _starting;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResponse"/> class.
    /// </summary>
    /// <param name="maxTextBytes">The most bytes of a text body kept.</param>
    /// <param name="discardBody">Whether the body is discarded, as a server discards it in answer to HEAD.</param>
    public ToolResponse(int maxTextBytes, bool discardBody = false)
    {
        this.Captured = new ToolResponseBody(() => this.Headers.ContentType, maxTextBytes, this.StartAsync, discardBody, () => this.Headers.ContentEncoding);
    }

    /// <summary>Gets the body as it was written.</summary>
    public ToolResponseBody Captured { get; }

    /// <inheritdoc />
    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    /// <inheritdoc />
    public string? ReasonPhrase { get; set; }

    /// <inheritdoc />
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    /// <inheritdoc />
    public bool HasStarted { get; private set; }

    /// <inheritdoc />
    public Stream Stream => this.Captured;

    /// <inheritdoc />
    public PipeWriter Writer => this._writer ??= PipeWriter.Create(this.Captured, new StreamPipeWriterOptions(leaveOpen: true));

    /// <inheritdoc />
    [Obsolete("Use IHttpResponseBodyFeature.Stream instead.")]
    public Stream Body
    {
        get => this.Captured;
        set => throw new NotSupportedException("A tool call's response body cannot be replaced through IHttpResponseFeature.");
    }

    /// <inheritdoc />
    public void OnStarting(Func<object, Task> callback, object state)
    {
        if (this.HasStarted)
        {
            throw new InvalidOperationException("The response has already started.");
        }

        this._onStarting.Add((callback, state));
    }

    /// <inheritdoc />
    public void OnCompleted(Func<object, Task> callback, object state)
    {
        this._onCompleted.Add((callback, state));
    }

    /// <inheritdoc />
    public void DisableBuffering()
    {
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (this.HasStarted || this._starting)
        {
            return;
        }

        this._starting = true;

        for (var i = this._onStarting.Count - 1; i >= 0; i--)
        {
            await this._onStarting[i].Callback(this._onStarting[i].State).ConfigureAwait(false);
        }

        this.HasStarted = true;
    }

    /// <inheritdoc />
    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        await this.StartAsync(cancellationToken).ConfigureAwait(false);
        await SendFileFallback.SendFileAsync(this.Captured, path, offset, count, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteAsync()
    {
        await this.StartAsync().ConfigureAwait(false);

        // Completing the writer writes what it has buffered and returns the pooled buffers it rents, as a server
        // completes the response's writer once the response has ended.
        if (this._writer is not null)
        {
            await this._writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the <c>OnCompleted</c> callbacks, last registered first, as a server does once a response has been sent.
    /// One that throws does not stop the others.
    /// </summary>
    /// <param name="logger">Where a callback's exception is logged.</param>
    /// <returns>A task that completes when every callback has run.</returns>
    public async Task RunCompletedCallbacksAsync(ILogger logger)
    {
        for (var i = this._onCompleted.Count - 1; i >= 0; i--)
        {
            try
            {
                await this._onCompleted[i].Callback(this._onCompleted[i].State).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "An OnCompleted callback of a tool call's response threw.");
            }
        }

        this._onCompleted.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // A response never completed, as when the operation threw, still returns the writer's buffers. Completing a
        // completed writer does nothing.
        this._writer?.Complete();
        this.Captured.Dispose();
    }
}
