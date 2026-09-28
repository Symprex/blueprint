using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that, on the router's synchronous-execution fast path, a result write that faults after it has already left
/// the synchronous part of <c>TryExecuteAsync</c> is answered with the same exception body an unhandled exception gets,
/// as legacy Blueprint wrote it — not an empty 500.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) wrote the exception its result writing threw
/// (<c>Blueprint.Http/OkResultOperationExecutor.cs:48-65</c>, <c>UnhandledExceptionOperationResultExecutor.cs:30-68</c>),
/// negotiating its content type with <see cref="Blueprint.Runtime.Http.BlueprintHttpOptions.NegotiateErrorContentType"/>
/// switched on (R83).
/// </para>
/// <para>
/// <c>BlueprintApiRouter.HandleResultSyncPath</c> takes this pipeline's fast path when handler execution completes
/// synchronously. When the result executor's own write does not complete synchronously — the common case for a real
/// socket write — <c>AwaitWriteTaskAsync</c> awaited the write with no <c>try</c>/<c>catch</c>, so a fault reaching it
/// after the `await` propagated out of the request delegate unhandled: ASP.NET Core's own top-level handling then
/// answers with a bare 500 and no body, since the framework's own catch (in
/// <c>RouteOperationSlowPathAsync</c>, taken only when the *handler* itself does not complete synchronously) never
/// runs for this path.
/// </para>
/// <para>
/// The test forces a synchronously-completing handler (so the sync path is taken) and a write that yields once before
/// throwing, without ever touching the real response stream — so the response has not started when the fault is
/// caught, and the fix can still write the exception body.
/// </para>
/// </remarks>
public sealed class SyncPathWriteFaultWireTests
{
    [Fact]
    public async Task A_write_that_faults_asynchronously_on_the_sync_path_is_answered_with_the_exception_body()
    {
        await using var host = await SyncPathWriteFaultHost.StartAsync();

        var (status, contentType, body) = await host.GetAsync();

        status.Should().Be(500);
        contentType.Should().StartWith("application/json");
        body.Should().Contain("boom-during-write");
    }

    /// <summary>The integration app, served by Kestrel, with its response body swapped for one that faults after a yield.</summary>
    private sealed class SyncPathWriteFaultHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private SyncPathWriteFaultHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<SyncPathWriteFaultHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.Configure<Blueprint.Runtime.Http.BlueprintHttpOptions>(options =>
            {
                options.NegotiateErrorContentType = true;
                options.ExposeExceptionDetailsInErrorResponses = true;
            });
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();

            // Swaps the response body for one that never reaches the real socket: it yields once (so the write does not
            // complete synchronously, taking BlueprintApiRouter's AwaitWriteTaskAsync branch) then faults, without ever
            // forwarding bytes downstream — so the response has not started when the router's exception handling runs.
            app.Use(async (httpContext, next) =>
            {
                if (httpContext.Request.Path.Value?.Contains("sync-path-write-fault", StringComparison.Ordinal) == true)
                {
                    httpContext.Response.Body = new FaultsOnFirstWriteStream(httpContext.Response.Body);
                }

                await next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new SyncPathWriteFaultHost(app, client);
        }

        public async Task<(int Status, string? ContentType, string Body)> GetAsync()
        {
            using var response = await this._client.GetAsync("sync-path-write-fault");
            var contentType = response.Content.Headers.ContentType?.ToString();

            return ((int)response.StatusCode, contentType, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>
    /// A write-only stream whose first <see cref="WriteAsync"/> yields once, then throws, without ever touching the real
    /// socket (<paramref name="inner"/>) — so the response has not started when the fault is caught. Every write after
    /// the first forwards to <paramref name="inner"/> as normal, so the router's own retry (the exception body) reaches
    /// the client.
    /// </summary>
    private sealed class FaultsOnFirstWriteStream(Stream inner) : Stream
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
                await Task.Yield();
                throw new InvalidOperationException("boom-during-write");
            }

            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            inner.Flush();
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
[RootLink("/sync-path-write-fault")]
public partial class GetSyncPathWriteFaultQuery : IQuery<string>
{
    public static string Handle(GetSyncPathWriteFaultQuery query)
    {
        return "ok";
    }
}
