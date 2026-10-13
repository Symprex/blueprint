using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a result write that faults <em>after</em> it has already started the response is not swallowed into a
/// clean, completed response: the connection is aborted, as legacy Blueprint left it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) caught a write failure only when the response had not yet started
/// (<c>Blueprint.Http/OkResultOperationExecutor.cs:48</c>, <c>when (httpContext.Response.HasStarted == false)</c>); its
/// router had no catch of its own. A fault reached after the response had started propagated unhandled, and Kestrel
/// aborted the connection — the client saw an incomplete response, not a clean 200.
/// </para>
/// <para>
/// R105's <c>AwaitWriteTaskAsync</c> catch (and the pre-existing, structurally identical catch in
/// <c>RouteOperationSlowPathAsync</c>) had no such filter: any write fault, started or not, was swallowed and logged,
/// and <c>HandleUnexpectedExceptionAsync</c> writes its problem body only when the response has not started — so a
/// fault after the response started was silently absorbed, and the request delegate completed as if nothing had gone
/// wrong.
/// </para>
/// <para>
/// Both tests return a <see cref="PlainTextResult"/>, written through <c>HttpResponse.WriteAsync(string)</c> with no
/// explicit <c>Content-Length</c> — unlike the JSON path, which sets one up front and lets Kestrel itself detect and
/// fault on a short write regardless of this fix. With no declared length the swallowed fault would leave the request
/// delegate completing normally and Kestrel closing the chunked response as if it had finished, so the discriminator
/// here is the fix itself, not Kestrel's own length check. Both tests split the router's single body write in two: the
/// first half reaches the real socket and is flushed (starting the response), then the stream faults before the
/// second half is written.
/// </para>
/// </remarks>
public sealed class StartedResponseWriteFaultWireTests
{
    [Fact]
    public async Task A_sync_path_write_that_faults_after_starting_the_response_aborts_the_connection()
    {
        // GetStartedWriteFaultSyncQuery completes synchronously, so BlueprintApiRouter takes HandleResultSyncPath and,
        // because the write itself does not complete synchronously, AwaitWriteTaskAsync.
        await using var host = await StartedResponseWriteFaultHost.StartAsync("started-write-fault-sync");

        var act = () => host.GetAsync();

        await act.Should().ThrowAsync<Exception>("the connection was aborted mid-body, not completed cleanly");
    }

    [Fact]
    public async Task An_async_path_write_that_faults_after_starting_the_response_aborts_the_connection()
    {
        // GetStartedWriteFaultAsyncQuery yields before returning, so BlueprintApiRouter takes RouteOperationSlowPathAsync
        // for both handler dispatch and the write.
        await using var host = await StartedResponseWriteFaultHost.StartAsync("started-write-fault-async");

        var act = () => host.GetAsync();

        await act.Should().ThrowAsync<Exception>("the connection was aborted mid-body, not completed cleanly");
    }

    /// <summary>The integration app, served by Kestrel, with its response body swapped for one that starts, then faults.</summary>
    private sealed class StartedResponseWriteFaultHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;
        private readonly string _path;

        private StartedResponseWriteFaultHost(WebApplication app, System.Net.Http.HttpClient client, string path)
        {
            this._app = app;
            this._client = client;
            this._path = path;
        }

        public static async Task<StartedResponseWriteFaultHost> StartAsync(string path)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();

            app.Use(async (httpContext, next) =>
            {
                if (httpContext.Request.Path.Value?.Contains("started-write-fault", StringComparison.Ordinal) == true)
                {
                    httpContext.Response.Body = new FaultsAfterPartialWriteStream(httpContext.Response.Body);
                }

                await next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new StartedResponseWriteFaultHost(app, client, path);
        }

        public Task<System.Net.Http.HttpResponseMessage> GetAsync()
        {
            return this._client.GetAsync(this._path);
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>
    /// A write-only stream whose first <see cref="WriteAsync"/> forwards half the buffer to <paramref name="inner"/> and
    /// flushes it — reaching the real socket and starting the response — then yields once and throws, without ever
    /// writing the rest. <see cref="Microsoft.AspNetCore.Http.HttpResponse.HasStarted"/> is true by the time the fault is
    /// caught, unlike <c>SyncPathWriteFaultWireTests.FaultsOnFirstWriteStream</c>, which never reaches the socket at all.
    /// </summary>
    private sealed class FaultsAfterPartialWriteStream(Stream inner) : Stream
    {
        private bool _hasFaulted;

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
            if (!this._hasFaulted)
            {
                this._hasFaulted = true;

                var half = buffer.Length / 2;
                await inner.WriteAsync(buffer[..half], cancellationToken).ConfigureAwait(false);
                await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
                await Task.Yield();

                throw new InvalidOperationException("boom-after-response-started");
            }

            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            // Overridden so ASP.NET Core's own header-start/flush calls reach the real stream's async flush.
            // Stream's base FlushAsync otherwise wraps the synchronous Flush() above, which Kestrel disallows by default
            // (AllowSynchronousIO) once the response has started.
            return inner.FlushAsync(cancellationToken);
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

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}

/// <summary>A query whose handler completes synchronously, so <c>RouteOperationAsync</c> takes the sync-execution path.</summary>
[AllowAnonymous]
[RootLink("/started-write-fault-sync")]
public partial class GetStartedWriteFaultSyncQuery : IQuery<PlainTextResult>
{
    public static PlainTextResult Handle(GetStartedWriteFaultSyncQuery query)
    {
        return new PlainTextResult("a moderately long response body so splitting it in half leaves bytes on both sides");
    }
}

/// <summary>A query whose handler yields, so <c>RouteOperationAsync</c> takes the async slow path.</summary>
[AllowAnonymous]
[RootLink("/started-write-fault-async")]
public partial class GetStartedWriteFaultAsyncQuery : IQuery<PlainTextResult>
{
    public static async Task<PlainTextResult> HandleAsync(GetStartedWriteFaultAsyncQuery query)
    {
        await Task.Yield();

        return new PlainTextResult("a moderately long response body so splitting it in half leaves bytes on both sides");
    }
}
