namespace Blueprint.HttpClient.Tests;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that intercepts outgoing HTTP requests and returns
/// pre-configured responses, allowing tests to verify request shape without a real server.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    /// <summary>
    /// Initialises a new instance of the <see cref="FakeHttpHandler"/> class.
    /// </summary>
    /// <param name="handler">A function that receives the outgoing request and returns a response.</param>
    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        this._handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(this._handler(request));
    }
}
