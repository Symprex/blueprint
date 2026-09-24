using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves, over real Kestrel, that a response whose status forbids a body is written without one.
/// </summary>
/// <remarks>
/// Kestrel refuses a body, and even a <c>Content-Length</c>, on a 204, 205 or 304 response ("Setting the header
/// Content-Length is not allowed on responses with status code 204"), failing the request with a 500, and a client could
/// not read one anyway. Legacy Blueprint never wrote a body on those statuses. The in-memory endpoint harness and TestServer do not
/// enforce the rule, so these tests run the app on Kestrel and watch the server as well as the client: a broken
/// response can still reach the client as a clean status once the server has thrown.
/// </remarks>
public sealed class KestrelBodylessStatusTests
{
    [Fact]
    public async Task A_deleted_resource_event_is_a_204_with_no_body()
    {
        await using var host = await KestrelHost.StartAsync();

        using var content = new StringContent("""{"resourceId":"del-001"}""", Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync("api/e2e/events-delete", content);

        await ShouldBeBodilessAsync(host, response, HttpStatusCode.NoContent);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_deferred_resource_event_is_a_204_with_no_body()
    {
        await using var host = await KestrelHost.StartAsync();

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync("api/e2e/deferred-events-delete", content);

        await ShouldBeBodilessAsync(host, response, HttpStatusCode.NoContent);
        host.ServerErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task Plain_text_with_a_status_that_forbids_a_body_is_written_without_one(HttpStatusCode status)
    {
        await using var host = await KestrelHost.StartAsync();

        using var response = await host.Client.GetAsync($"api/e2e/plain-text-status?status={(int)status}");

        await ShouldBeBodilessAsync(host, response, status);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Plain_text_with_a_status_that_allows_a_body_is_written_with_it()
    {
        await using var host = await KestrelHost.StartAsync();

        using var response = await host.Client.GetAsync("api/e2e/plain-text-status?status=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(E2EPlainTextStatusQuery.Text);
        host.ServerErrors.Should().BeEmpty();
    }

    private static async Task ShouldBeBodilessAsync(KestrelHost host, HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        response.Content.Headers.ContentType.Should().BeNull();
        (response.Content.Headers.ContentLength ?? 0).Should().Be(0);
    }

    /// <summary>The integration app, mapped as a consumer maps it, served by Kestrel on a free local port.</summary>
    private sealed class KestrelHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private KestrelHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<Exception> serverErrors)
        {
            this._app = app;
            this.Client = client;
            this.ServerErrors = serverErrors;
        }

        public System.Net.Http.HttpClient Client { get; }

        /// <summary>Gets every exception the server raised or logged while handling a request.</summary>
        public ConcurrentQueue<Exception> ServerErrors { get; }

        public static async Task<KestrelHost> StartAsync()
        {
            var serverErrors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ExceptionRecordingLoggerProvider(serverErrors));
            builder.Services
                .AddSingleton<IntegrationTestBlueprintApp>()
                .AddSingleton<IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (Exception exception)
                {
                    serverErrors.Enqueue(exception);
                    throw;
                }
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new KestrelHost(app, client, serverErrors);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    private sealed class ExceptionRecordingLoggerProvider(ConcurrentQueue<Exception> exceptions) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ExceptionRecordingLogger(exceptions);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ExceptionRecordingLogger(ConcurrentQueue<Exception> exceptions) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                exceptions.Enqueue(exception ?? new InvalidOperationException(formatter(state, exception)));
            }
        }
    }
}

/// <summary>Deletes a resource, returning a deferred event whose self query names it.</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/deferred-events-delete")]
[Blueprint.Model.Attributes.HttpPost]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EDeferredResourceEventDeleteCommand : ICommand<DeferredResourceEventResult<Generators.Pipelines.SelfQueryTestApiResource>>
{
    /// <summary>Returns a deferred resource event with the Deleted change type.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The deferred event.</returns>
    public static DeferredResourceEventResult<Generators.Pipelines.SelfQueryTestApiResource> Handle(E2EDeferredResourceEventDeleteCommand command)
    {
        return new DeferredResourceEventResult<Generators.Pipelines.SelfQueryTestApiResource>(
            ChangeType.Deleted,
            "selfquery-test-resource.deleted",
            new Generators.Pipelines.GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>Returns plain text with the status the request names, whether or not that status allows a body.</summary>
[Blueprint.Model.Attributes.RootLink("/e2e/plain-text-status")]
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class E2EPlainTextStatusQuery : IQuery<PlainTextResult>
{
    /// <summary>The text written when the status allows a body.</summary>
    public const string Text = "plain text";

    /// <summary>The status to respond with.</summary>
    public int Status { get; set; }

    /// <summary>Returns the text with the requested status.</summary>
    /// <param name="query">The query.</param>
    /// <returns>A plain text result.</returns>
    public static PlainTextResult Handle(E2EPlainTextStatusQuery query)
    {
        return new PlainTextResult(Text, (HttpStatusCode)query.Status);
    }
}
