using System.Text.Json;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an exception an operation throws is answered with the problem body legacy Blueprint wrote for it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) made every exception an <c>UnhandledExceptionOperationResult</c>, written by
/// <c>Blueprint.Http/UnhandledExceptionOperationResultExecutor.cs</c>:
/// </para>
/// <list type="bullet">
/// <item><description>an <c>ApiException</c> as its own status, title, type, detail and extensions, whatever the status
/// and whether details were exposed (:50-58);</description></item>
/// <item><description>any other exception as a 500 of type <c>unknown_error</c>, titled "Something has gone wrong, please
/// try again" with no detail, or, with details exposed, titled with its message and detailed with its stack trace
/// (:60-67);</description></item>
/// <item><description>with a <c>traceId</c> extension, after the others, when the operation had an activity
/// (:35-38).</description></item>
/// </list>
/// <para>
/// The body was type, title, status, detail, then the extensions, each left out when null
/// (<c>ProblemDetailsJsonConverter.cs:107-139</c>). The exceptions: <c>NotFoundException(message)</c> was
/// <c>("The requested resource could not be found", "not_found", message, 404)</c>
/// (<c>Blueprint/Errors/NotFoundException.cs</c>). <c>UnauthorizedException(message)</c> was
/// <c>(message, "security_failure", null, 401)</c> (<c>Errors/UnauthorizedException.cs:15-18</c>).
/// <c>ForbiddenException(failure)</c> was <c>(failure.Message, "unauthorized", failure.Reason, 403)</c>
/// (<c>Errors/ForbiddenException.cs:8-12</c>).
/// </para>
/// </remarks>
public sealed class ExceptionProblemWireTests
{
    [Theory]
    [InlineData("not-found", 404, """{"type":"not_found","title":"The requested resource could not be found","status":404,"detail":"No such widget"}""")]
    [InlineData("no-detail", 423, """{"type":"widget_locked","title":"Widget locked","status":423}""")]
    [InlineData("detail-only", 400, """{"status":400,"detail":"Only a detail"}""")]
    [InlineData("server-api-exception", 502, """{"type":"upstream_failed","title":"Upstream failed","status":502,"detail":"The remote server returned an internal error response"}""")]
    [InlineData("unauthorized", 401, """{"type":"security_failure","title":"User not a valid claims identity","status":401}""")]
    [InlineData("forbidden", 403, """{"type":"unauthorized","title":"Only the owner may do this","status":403,"detail":"NotOwner"}""")]
    [InlineData("unhandled", 500, """{"type":"unknown_error","title":"Something has gone wrong, please try again","status":500}""")]
    public async Task An_exception_is_written_with_legacys_body(string outcome, int status, string expected)
    {
        await using var host = await ExceptionHost.StartAsync(exposeDetails: false, withActivity: false);

        var (actualStatus, body) = await host.GetAsync(outcome);

        actualStatus.Should().Be(status);
        body.Should().Be(expected);
    }

    [Fact]
    public async Task An_ApiException_keeps_its_own_detail_when_details_are_exposed()
    {
        // The ApiException branch ignored the exposure setting (:50-58).
        await using var host = await ExceptionHost.StartAsync(exposeDetails: true, withActivity: false);

        var (_, body) = await host.GetAsync("server-api-exception");

        body.Should().Be("""{"type":"upstream_failed","title":"Upstream failed","status":502,"detail":"The remote server returned an internal error response"}""");
    }

    [Fact]
    public async Task Another_exception_is_titled_with_its_message_and_detailed_with_its_stack_trace_when_details_are_exposed()
    {
        await using var host = await ExceptionHost.StartAsync(exposeDetails: true, withActivity: false);

        var (_, body) = await host.GetAsync("unhandled");

        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(static p => p.Name).Should().Equal("type", "title", "status", "detail");
        document.RootElement.GetProperty("type").GetString().Should().Be("unknown_error");
        document.RootElement.GetProperty("title").GetString().Should().Be("The widget store is unavailable");
        document.RootElement.GetProperty("detail").GetString().Should().StartWith("   at ", "legacy's detail was the exception's StackTrace alone");
    }

    [Theory]
    [InlineData("not-found", "type,title,status,detail,traceId")]
    [InlineData("with-extensions", "type,title,status,detail,currentVersion,traceId")]
    [InlineData("unhandled", "type,title,status,traceId")]
    public async Task An_operation_with_an_activity_carries_its_trace_id_after_every_other_member(string outcome, string members)
    {
        await using var host = await ExceptionHost.StartAsync(exposeDetails: false, withActivity: true);

        var (_, body) = await host.GetAsync(outcome);

        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(static p => p.Name).Should().Equal(members.Split(','));
        document.RootElement.GetProperty("traceId").GetString().Should().Be(ExceptionHost.TraceId);
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class ExceptionHost : IAsyncDisposable
    {
        public const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private ExceptionHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<ExceptionHost> StartAsync(bool exposeDetails, bool withActivity)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            // Logging enabled is what makes ASP.NET Core start the request's activity, which the operation's is made under.
            if (withActivity)
            {
                builder.Logging.AddProvider(new NullLoggerProvider());
            }

            builder.Services.Configure<BlueprintHttpOptions>(options => options.ExposeExceptionDetailsInErrorResponses = exposeDetails);
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ExceptionHost(app, client);
        }

        public async Task<(int Status, string Body)> GetAsync(string outcome)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "exception-problems/" + outcome);
            request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");

            using var response = await this._client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>A logger provider whose loggers are enabled and record nothing.</summary>
    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new EnabledLogger();
        }

        public void Dispose()
        {
        }
    }

    private sealed class EnabledLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}

/// <summary>A query that throws the exception its <see cref="Outcome"/> names.</summary>
[AllowAnonymous]
[RootLink("/exception-problems/{Outcome}")]
public partial class GetExceptionProblemQuery : IQuery<string>
{
    public string Outcome { get; set; } = null!;

    public static string Handle(GetExceptionProblemQuery query)
    {
        throw query.Outcome switch
        {
            "not-found" => new NotFoundException("No such widget"),
            "no-detail" => new ApiException("Widget locked", "widget_locked", null, 423),
            "detail-only" => new ApiException(null!, null!, "Only a detail", 400),
            "server-api-exception" => new ApiException("Upstream failed", "upstream_failed", "The remote server returned an internal error response", 502),
            "unauthorized" => new UnauthorizedException("User not a valid claims identity"),
            "forbidden" => new ForbiddenException(ExecutionAllowed.No("NotOwner", "Only the owner may do this", ExecutionAllowedFailureType.Authorisation)),
            "with-extensions" => new ApiException(
                "Template version conflict",
                "template_conflict",
                "Changed by someone else",
                409,
                new Dictionary<string, object> { ["currentVersion"] = 7 }),
            _ => new InvalidOperationException("The widget store is unavailable"),
        };
    }
}
