using System.Net;
using System.Security.Claims;
using System.Text;
using Blueprint.EndToEnd.Tests.Harness;
using Blueprint.Http;
using Blueprint.SourceGenerator.Sample.Api;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// A forward declared as <see cref="ProxyResult"/> streams the upstream body through the gateway (D18): the gateway answers
/// at the upstream's headers, copies the body as it arrives, and disposes the upstream response once written or when its
/// caller goes away.
/// </summary>
/// <remarks>
/// Each test starts its own inner app and gateway, as the inner app here holds every download mid-body until the test lets
/// it finish, and the gateway reports when it disposes an upstream response. Neither is a change to either app's behaviour:
/// the hold is a stall on the inner app's own socket writes, and the report is a wrapper over the upstream content.
/// </remarks>
public sealed class StreamingForwardTests : IAsyncLifetime
{
    /// <summary>Far more than the sockets, Kestrel and the gateway's client buffer between them.</summary>
    private const int LargeBodyLength = 8 * 1024 * 1024;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly TestJwtIssuer _issuer = new();
    private readonly InnerDownloadGate _gate = new();
    private readonly UpstreamResponseTracker _tracker = new();
    private MultiAppHost _host = null!;
    private RunningApp _inner = null!;
    private RunningApp _gateway = null!;

    [Fact]
    public async Task Response_headers_arrive_before_the_inner_app_finishes_writing_a_large_body()
    {
        var content = CreateLargeContent();
        var id = this.SeedTemplate(content);
        using var caller = this.CreateGatewayCaller();

        using var response = await caller
            .SendAsync(new HttpRequestMessage(HttpMethod.Get, $"gateway/templates/{id}/download"), HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(Patience);

        // The inner app is still held after its first chunk, so these headers cannot have waited for the body.
        this._gate.InnerFinished.IsCompleted.Should().BeFalse();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"template-{id}.txt");

        this._gate.Release();
        var body = await response.Content.ReadAsByteArrayAsync().WaitAsync(Patience);

        body.Should().HaveCount(LargeBodyLength);
        Encoding.UTF8.GetString(body).Should().Be(content);
        await this._gate.InnerFinished.WaitAsync(Patience);
    }

    [Fact]
    public async Task The_upstream_response_is_disposed_once_the_body_is_written()
    {
        var id = this.SeedTemplate(CreateLargeContent());
        using var caller = this.CreateGatewayCaller();
        this._gate.Release();

        using var response = await caller.GetAsync($"gateway/templates/{id}/download").WaitAsync(Patience);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().HaveCount(LargeBodyLength);
        await this._tracker.Disposed.WaitAsync(Patience);
    }

    [Fact]
    public async Task A_caller_that_goes_away_mid_body_ends_the_upstream_response()
    {
        var id = this.SeedTemplate(CreateLargeContent());
        using var caller = this.CreateGatewayCaller();

        var response = await caller
            .SendAsync(new HttpRequestMessage(HttpMethod.Get, $"gateway/templates/{id}/download"), HttpCompletionOption.ResponseHeadersRead)
            .WaitAsync(Patience);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The inner app is held, so the gateway is waiting on the upstream body: only the caller's abort can end its copy.
        response.Dispose();
        caller.Dispose();

        await this._tracker.Disposed.WaitAsync(Patience);
        this._gate.InnerFinished.IsCompleted.Should().BeFalse();
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        this._host = new MultiAppHost(this._issuer);

        this._inner = await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>(SampleSystem.SampleSystemFixture.InnerName, inner =>
        {
            inner.ConfigureServices = services => services.AddSingleton<IStartupFilter>(new InnerDownloadGateFilter(this._gate));
        });

        this._gateway = await this._host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>(SampleSystem.SampleSystemFixture.GatewayName, gateway =>
        {
            gateway.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.TemplateServiceAddressKey] = new Uri(this._inner.BaseAddress, "api/").ToString();

            // No harness-filled Authorization header: the download forward sends the caller's credential itself.
            gateway.ConfigureServices = services => services.ConfigureHttpClientDefaults(
                client => client.AddHttpMessageHandler(() => new UpstreamResponseTrackingHandler(this._tracker)));
        });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // Let a held inner download finish, so stopping the inner app does not wait on it.
        this._gate.Release();
        await this._host.DisposeAsync();
    }

    private static string CreateLargeContent()
    {
        var builder = new StringBuilder(LargeBodyLength);

        while (builder.Length < LargeBodyLength)
        {
            builder.Append((char)('a' + (builder.Length % 26)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates a template directly in the inner app's store, as its create command caps the HTML at far less than a large
    /// body.
    /// </summary>
    private string SeedTemplate(string htmlSource)
    {
        return this._inner.Services.GetRequiredService<TemplateStore>().Create("Large Download", htmlSource, "Large Download").Id;
    }

    private System.Net.Http.HttpClient CreateGatewayCaller()
    {
        return this._gateway.CreateClient(this._issuer.CreateToken(
            [new Claim("sub", "e2e-user"), .. SampleSystem.SampleSystemFixture.TemplateScopes.Select(static s => new Claim("scope", s))]));
    }

    /// <summary>
    /// Holds each inner download after its first chunk until <see cref="Release"/>, and says when the inner app has written
    /// the whole of one.
    /// </summary>
    private sealed class InnerDownloadGate
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _innerFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Released => this._released.Task;

        public Task InnerFinished => this._innerFinished.Task;

        public void Release()
        {
            this._released.TrySetResult();
        }

        public void MarkInnerFinished()
        {
            this._innerFinished.TrySetResult();
        }
    }

    private sealed class InnerDownloadGateFilter(InnerDownloadGate gate) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path.Value?.EndsWith("/download", StringComparison.Ordinal) != true)
                    {
                        await nextMiddleware(context);

                        return;
                    }

                    context.Response.Body = new HeldResponseStream(context.Response.Body, gate);
                    await nextMiddleware(context);
                    gate.MarkInnerFinished();
                });

                next(app);
            };
        }
    }

    /// <summary>
    /// Writes and flushes the first chunk of the inner app's body, then waits for the gate before writing the rest.
    /// </summary>
    private sealed class HeldResponseStream(Stream inner, InnerDownloadGate gate) : Stream
    {
        private const int ChunkLength = 64 * 1024;

        private bool _firstChunkWritten;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (!buffer.IsEmpty)
            {
                if (this._firstChunkWritten)
                {
                    await gate.Released.WaitAsync(cancellationToken);
                }

                var chunk = buffer[..Math.Min(ChunkLength, buffer.Length)];
                await inner.WriteAsync(chunk, cancellationToken);
                await inner.FlushAsync(cancellationToken);
                this._firstChunkWritten = true;
                buffer = buffer[chunk.Length..];
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
        }

        public override void Flush()
        {
            throw new NotSupportedException("The inner app writes its body asynchronously.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The inner app writes its body asynchronously.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Reports when the gateway disposes the upstream response of a download.
    /// </summary>
    private sealed class UpstreamResponseTracker
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => this._disposed.Task;

        public void MarkDisposed()
        {
            this._disposed.TrySetResult();
        }
    }

    private sealed class UpstreamResponseTrackingHandler(UpstreamResponseTracker tracker) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (request.RequestUri?.AbsolutePath.EndsWith("/download", StringComparison.Ordinal) == true)
            {
                response.Content = new TrackedContent(response.Content, tracker);
            }

            return response;
        }
    }

    /// <summary>
    /// The upstream content, read through unchanged, that reports its disposal. Disposing a response disposes its content.
    /// </summary>
    private sealed class TrackedContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly UpstreamResponseTracker _tracker;

        public TrackedContent(HttpContent inner, UpstreamResponseTracker tracker)
        {
            this._inner = inner;
            this._tracker = tracker;

            foreach (var (name, values) in inner.Headers)
            {
                this.Headers.TryAddWithoutValidation(name, values);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            return this._inner.CopyToAsync(stream);
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            return this._inner.CopyToAsync(stream, cancellationToken);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return this._inner.ReadAsStreamAsync();
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            return this._inner.ReadAsStreamAsync(cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this._tracker.MarkDisposed();
                this._inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
