using System.Collections.Concurrent;
using System.Text.Json;
using Blueprint.Authorization;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an app's <see cref="IHttpResultWriter"/> is given every result a served operation ends with before
/// Blueprint writes it, can write it in a shape of its own, and leaves Blueprint's own response untouched when it declines
/// (Symprex/blueprint#90).
/// </summary>
/// <remarks>
/// The shape written here is the legacy Blueprint error envelope a strangled legacy API's clients read
/// (<c>{"error":{"code","message"}}</c>, with a 422's <c>errors</c> beside it, as <c>application/json</c>), and a CSV
/// rendering of a successful result asked for with <c>?format=csv</c>.
/// </remarks>
public sealed class HttpResultWriterWireTests
{
    [Fact]
    public async Task A_writer_replaces_the_body_of_a_validation_failure()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("error-casing/thrown");

        response.Status.Should().Be(422);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be(
            """{"error":{"code":"validation_failed","message":"One or more validation errors occurred."},"errors":{"name":["Name is required"],"maxResults":["MaxResults is out of range"]}}""");
    }

    [Fact]
    public async Task A_writer_replaces_the_body_of_an_anonymous_callers_401()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("authorisation-denials/allow", user: null);

        response.Status.Should().Be(401);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be("""{"error":{"code":"security_failure","message":"Your session has expired"}}""");
    }

    [Fact]
    public async Task A_writer_replaces_the_body_of_an_authentication_denials_401()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("authorisation-denials/authentication");

        response.Status.Should().Be(401);
        response.Body.Should().Be("""{"error":{"code":"security_failure","message":"Your account has been deactivated"}}""");
    }

    [Fact]
    public async Task A_writer_replaces_the_body_of_a_403()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("authorisation-denials/authorisation");

        response.Status.Should().Be(403);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be("""{"error":{"code":"feature_not_available","message":"You may not change this widget"}}""");
    }

    [Fact]
    public async Task A_writer_is_given_the_original_exception_an_operation_threw_and_chooses_its_status_and_body()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("exception-problems/store-unavailable");

        response.Status.Should().Be(400);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"The widget store is unavailable"}}""");
    }

    [Fact]
    public async Task A_writer_that_declines_one_exception_leaves_that_one_to_blueprints_problem_details()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("exception-problems/not-found");

        response.Status.Should().Be(404);
        response.ContentType.Should().Be("application/problem+json; charset=utf-8");
        response.Body.Should().Be("""{"type":"not_found","title":"The requested resource could not be found","status":404,"detail":"No such widget"}""");
    }

    [Fact]
    public async Task A_writer_is_given_an_exception_blueprints_own_write_threw_with_the_original_exception()
    {
        // The writer declines the OkResult; Blueprint's own write of it faults before the response starts, and the router's
        // handling of that exception gives it to the writer as an UnhandledExceptionResult.
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("sync-path-write-fault");

        response.Status.Should().Be(400, response.Body);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"boom-during-write"}}""");
    }

    [Fact]
    public async Task A_writer_is_given_an_exception_from_a_write_that_had_already_faulted()
    {
        // The body stream returns an already-faulted write, without yielding: HandleOkResult used to take that write as
        // complete, so the exception reached no one and the request ended as a 200 with no body.
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("sync-path-write-fault?at-once");

        response.Status.Should().Be(400, response.Body);
        response.ContentType.Should().Be("application/json");
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"boom-at-once"}}""");
    }

    [Fact]
    public async Task Without_a_writer_a_write_that_had_already_faulted_is_answered_with_the_exception_body()
    {
        await using var host = await ResultWriterHost.StartAsync(static _ => { });

        var response = await host.GetAsync("sync-path-write-fault?at-once");

        response.Status.Should().Be(500, response.Body);
        response.Body.Should().Be("""{"type":"unknown_error","title":"Something has gone wrong, please try again","status":500}""");
    }

    [Fact]
    public async Task A_writer_that_throws_is_given_that_exception_once_and_may_write_it()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("e2e/maybe-items/writer-throws");

        response.Status.Should().Be(400, response.Body);
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"The writer failed"}}""");
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("OkResult`1", "UnhandledExceptionResult:The writer failed");
    }

    [Fact]
    public async Task A_writer_that_throws_again_for_its_own_exception_lets_the_second_escape_to_aspnet_core()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("e2e/maybe-items/writer-throws-twice");

        // ASP.NET Core's own answer to an exception that leaves the request delegate before the response starts.
        response.Status.Should().Be(500);
        response.Body.Should().BeEmpty();
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("OkResult`1", "UnhandledExceptionResult:The writer failed twice");
    }

    [Fact]
    public async Task A_writer_that_writes_a_proxy_result_itself_still_has_its_upstream_response_disposed()
    {
        // The writer answers in place of ProxyResult.ExecuteAsync, which used to be the only thing that disposed the
        // upstream response a forward hands over.
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());
        var disposed = ResultWriterProxyQuery.Track("writer-writes");

        var response = await host.GetAsync("result-writer/proxy?key=writer-writes&writer=writes");

        response.Status.Should().Be(200, response.Body);
        response.Body.Should().Be("written by the writer");
        (await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(disposed, "the upstream response is disposed with the request");
    }

    [Fact]
    public async Task A_writer_that_throws_on_a_proxy_result_still_has_its_upstream_response_disposed()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());
        var disposed = ResultWriterProxyQuery.Track("writer-throws");

        var response = await host.GetAsync("result-writer/proxy?key=writer-throws&writer=throws");

        response.Status.Should().Be(400, response.Body);
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"The writer failed on a proxy"}}""");
        (await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(disposed, "the upstream response is disposed with the request");
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("faulted")]
    public async Task A_writer_that_throws_for_a_pipeline_failure_is_given_its_own_exception_once(string failure)
    {
        // A pipeline that throws, or returns a faulted task, gives the writer its first result as the exception. Its throw
        // there is a writer failure like any other, so it is given that one recovery call too.
        await using var host = await ResultWriterHost.StartAsync<FaultingPipelineExecutor>(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync($"sync-path-write-fault?failure={failure}&exception=timeout");

        response.Status.Should().Be(400, response.Body);
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"The writer failed"}}""");
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("UnhandledExceptionResult:pipeline-failed", "UnhandledExceptionResult:The writer failed");
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("faulted")]
    public async Task A_writer_that_throws_again_for_a_pipeline_failure_lets_the_second_escape_to_aspnet_core(string failure)
    {
        await using var host = await ResultWriterHost.StartAsync<FaultingPipelineExecutor>(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync($"sync-path-write-fault?failure={failure}&exception=argument");

        response.Status.Should().Be(500);
        response.Body.Should().BeEmpty();
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("UnhandledExceptionResult:pipeline-failed-twice", "UnhandledExceptionResult:The writer failed twice");
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("faulted")]
    public async Task A_writer_that_declines_a_pipeline_failure_is_given_the_fault_of_blueprints_own_problem_write(string failure)
    {
        // The writer declines the pipeline's exception; Blueprint's write of its problem then faults before the response
        // starts, and that fault is offered to the writer once.
        await using var host = await ResultWriterHost.StartAsync<FaultingPipelineExecutor>(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync($"sync-path-write-fault?failure={failure}&exception=declined&write-fault");

        response.Status.Should().Be(400, response.Body);
        response.Body.Should().Be("""{"error":{"code":"invalid_operation","message":"boom-during-write"}}""");
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("UnhandledExceptionResult:pipeline-failed-declined", "UnhandledExceptionResult:boom-during-write");
    }

    [Fact]
    public async Task A_writer_that_declines_an_exception_result_is_given_the_fault_of_its_problem_write_as_its_only_recovery()
    {
        // The operation's result is an exception the writer declines; Blueprint's write of its problem faults, and that
        // fault is the writer's one recovery call, so its throw there escapes rather than earning a third call.
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var response = await host.GetAsync("exception-problems/not-found?write-fault=unsupported");

        response.Status.Should().Be(500);
        response.Body.Should().BeEmpty();
        host.Services.GetRequiredService<IHttpResultWriter>().As<LegacyEnvelopeResultWriter>().Given
            .Should().Equal("UnhandledExceptionResult:No such widget", "UnhandledExceptionResult:boom-during-write");
    }

    [Fact]
    public async Task A_writer_writes_a_successful_result_in_a_format_of_its_own()
    {
        await using var host = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<LegacyEnvelopeResultWriter>());

        var csv = await host.GetAsync("e2e/maybe-items/alpha?format=csv");
        var json = await host.GetAsync("e2e/maybe-items/alpha");

        csv.Status.Should().Be(200);
        csv.ContentType.Should().Be("text/csv");
        csv.Body.Should().Be("name\r\nalpha\r\n");

        json.Status.Should().Be(200);
        json.ContentType.Should().Be("application/json; charset=utf-8");
        json.Body.Should().Be("""{"name":"alpha"}""");
    }

    [Fact]
    public async Task A_writer_that_declines_everything_leaves_every_response_exactly_as_blueprint_writes_it_and_sees_every_result()
    {
        string[] paths =
        [
            "e2e/maybe-items/alpha",
            "error-casing/thrown",
            "authorisation-denials/allow?anonymous",
            "authorisation-denials/authentication",
            "authorisation-denials/authorisation",
            "exception-problems/store-unavailable",
            "exception-problems/not-found",
            "e2e/typed-items/not-a-guid",
            "e2e/maybe-items/missing",
            "sync-path-write-fault",
        ];

        await using var plain = await ResultWriterHost.StartAsync(static _ => { });
        await using var declining = await ResultWriterHost.StartAsync(static b => b.AddHttpResultWriter<DecliningResultWriter>());

        foreach (var path in paths)
        {
            var expected = await plain.GetAsync(path);
            var actual = await declining.GetAsync(path);

            actual.Should().Be(expected, path);
        }

        var seen = declining.Services.GetRequiredService<IHttpResultWriter>().As<DecliningResultWriter>().Seen;

        seen.Should().Equal(
            "OkResult`1",
            "ValidationFailedResult",
            "UnauthenticatedResult",
            "UnauthenticatedResult",
            "ForbiddenResult",
            "UnhandledExceptionResult",
            "UnhandledExceptionResult",
            "BindingFailureResult",
            "NotFoundResult`1",
            "OkResult`1",
            "UnhandledExceptionResult");
    }

    /// <summary>A response's status, content type and body.</summary>
    /// <param name="Status">The status code.</param>
    /// <param name="ContentType">The <c>Content-Type</c> header, or <see langword="null"/>.</param>
    /// <param name="Body">The body.</param>
    private sealed record WireResponse(int Status, string? ContentType, string Body);

    /// <summary>
    /// Writes the legacy error envelope for the failures a legacy client reads it from, CSV for a successful result asked
    /// for as <c>?format=csv</c>, and declines everything else.
    /// </summary>
    private sealed class LegacyEnvelopeResultWriter : IHttpResultWriter
    {
        private readonly ConcurrentQueue<string> _given = new();

        /// <summary>Gets each result it was given: its type, and an exception's message.</summary>
        public IReadOnlyCollection<string> Given => this._given;

        public ValueTask<bool> TryWriteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result)
        {
            this._given.Enqueue(result is UnhandledExceptionResult unhandled
                ? $"{nameof(UnhandledExceptionResult)}:{unhandled.Exception.Message}"
                : result.GetType().Name);

            return result switch
            {
                // A writer that fails, and one that fails again when given its own failure.
                OkResult { Content: E2EWidgetResponse { Name: "writer-throws" } } => throw new InvalidOperationException("The writer failed"),
                OkResult { Content: E2EWidgetResponse { Name: "writer-throws-twice" } } => throw new NotSupportedException("The writer failed twice"),
                UnhandledExceptionResult { Exception: NotSupportedException } => throw new NotSupportedException("The writer failed again"),

                // The same, for an exception a pipeline failed with.
                UnhandledExceptionResult { Exception: TimeoutException } => throw new InvalidOperationException("The writer failed"),
                UnhandledExceptionResult { Exception: ArgumentException } => throw new NotSupportedException("The writer failed twice"),
                OkResult { Content: E2EWidgetResponse widget } when httpContext.Request.Query["format"] == "csv" => WriteCsvAsync(httpContext, widget),
                ValidationFailedResult failed => WriteEnvelopeAsync(
                    httpContext,
                    422,
                    "validation_failed",
                    "One or more validation errors occurred.",
                    HttpOperationResultExecutor.GetValidationErrors(operationContext, failed)),
                UnauthenticatedResult denied => WriteEnvelopeAsync(httpContext, 401, "security_failure", denied.Failure?.Message ?? "Your session has expired"),
                ForbiddenResult denied => WriteEnvelopeAsync(httpContext, 403, "feature_not_available", denied.Failure?.Message ?? "Forbidden"),
                UnhandledExceptionResult { Exception: InvalidOperationException exception } => WriteEnvelopeAsync(httpContext, 400, "invalid_operation", exception.Message),

                // A proxy result answered, or failed on, without ever being executed.
                Blueprint.Http.ProxyResult when httpContext.Request.Query["writer"] == "throws" => throw new InvalidOperationException("The writer failed on a proxy"),
                Blueprint.Http.ProxyResult when httpContext.Request.Query["writer"] == "writes" => WritePlainAsync(httpContext, "written by the writer"),
                _ => ValueTask.FromResult(false),
            };
        }

        private static async ValueTask<bool> WritePlainAsync(HttpContext httpContext, string body)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentType = "text/plain";
            await httpContext.Response.WriteAsync(body);

            return true;
        }

        private static async ValueTask<bool> WriteCsvAsync(HttpContext httpContext, E2EWidgetResponse widget)
        {
            httpContext.Response.StatusCode = 200;
            httpContext.Response.ContentType = "text/csv";
            await httpContext.Response.WriteAsync($"name\r\n{widget.Name}\r\n");

            return true;
        }

        private static async ValueTask<bool> WriteEnvelopeAsync(
            HttpContext httpContext,
            int status,
            string code,
            string message,
            IReadOnlyDictionary<string, string[]>? errors = null)
        {
            using var body = new MemoryStream();

            using (var writer = new Utf8JsonWriter(body))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("error");
                writer.WriteString("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();

                if (errors is not null)
                {
                    writer.WriteStartObject("errors");

                    foreach (var (key, messages) in errors)
                    {
                        writer.WriteStartArray(key);

                        foreach (var error in messages)
                        {
                            writer.WriteStringValue(error);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            httpContext.Response.StatusCode = status;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.Body.WriteAsync(body.ToArray());

            return true;
        }
    }

    /// <summary>An executor whose one pipeline, for <see cref="GetSyncPathWriteFaultQuery"/>, is <see cref="FaultingPipeline"/>.</summary>
    private sealed class FaultingPipelineExecutor : BlueprintAppExecutor
    {
        public FaultingPipelineExecutor(IntegrationTestBlueprintApp app)
            : base(app)
        {
            this.Register(typeof(GetSyncPathWriteFaultQuery), new FaultingPipeline());
        }
    }

    /// <summary>
    /// A hand-rolled pipeline that fails as the request's <c>failure</c> says — by throwing, or by returning a task that
    /// has already faulted — with the exception its <c>exception</c> names.
    /// </summary>
    private sealed class FaultingPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            var query = context.GetHttpContext().Request.Query;
            Exception exception = query["exception"].ToString() switch
            {
                "argument" => new ArgumentException("pipeline-failed-twice"),
                "declined" => new KeyNotFoundException("pipeline-failed-declined"),
                _ => new TimeoutException("pipeline-failed"),
            };

            return query["failure"] == "throws"
                ? throw exception
                : ValueTask.FromException<OperationResult>(exception);
        }
    }

    /// <summary>Records the type of every result it is given and declines each.</summary>
    private sealed class DecliningResultWriter : IHttpResultWriter
    {
        private readonly ConcurrentQueue<string> _seen = new();

        public IReadOnlyCollection<string> Seen => this._seen;

        public ValueTask<bool> TryWriteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result)
        {
            this._seen.Enqueue(result.GetType().Name);

            return ValueTask.FromResult(false);
        }
    }

    /// <summary>
    /// The integration app served by Kestrel, with the denial tests' authoriser and their test-user header, and no
    /// activity, so no body carries a trace id.
    /// </summary>
    private sealed class ResultWriterHost : IAsyncDisposable
    {
        private const string TestUserHeader = "X-Test-User";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private ResultWriterHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public IServiceProvider Services => this._app.Services;

        public static Task<ResultWriterHost> StartAsync(Action<BlueprintServiceBuilder> configure)
        {
            return StartAsync<IntegrationTestBlueprintAppExecutor>(configure);
        }

        public static async Task<ResultWriterHost> StartAsync<TExec>(Action<BlueprintServiceBuilder> configure)
            where TExec : BlueprintAppExecutor
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IApiAuthoriser, AuthorisationDenialWireTests.OutcomeApiAuthoriser>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, TExec>(blueprint =>
            {
                blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>();
                configure(blueprint);
            });

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.TryGetValue(TestUserHeader, out var userName))
                {
                    httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName.ToString())],
                        authenticationType: "Test"));
                }

                // As SyncPathWriteFaultWireTests: Blueprint's own write of this operation's result faults before the
                // response starts, reaching the router's exception handling.
                // A request whose pipeline fails (FaultingPipeline) writes through the real body unless it asks for a
                // write fault too, as any other request may.
                var query = httpContext.Request.Query;

                if ((httpContext.Request.Path.Value?.EndsWith("/sync-path-write-fault", StringComparison.Ordinal) == true && !query.ContainsKey("failure"))
                    || query.ContainsKey("write-fault"))
                {
                    httpContext.Response.Body = new FaultsOnFirstWriteStream(
                        httpContext.Response.Body,
                        alreadyFaulted: httpContext.Request.QueryString.Value == "?at-once",
                        unsupported: query["write-fault"] == "unsupported");
                }

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, TExec>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ResultWriterHost(app, client);
        }

        /// <summary>Sends a GET as a signed-in user, or anonymously when <paramref name="user"/> is <see langword="null"/>
        /// or the path's query is <c>?anonymous</c>.</summary>
        public async Task<WireResponse> GetAsync(string path, string? user = "user-1")
        {
            if (path.EndsWith("?anonymous", StringComparison.Ordinal))
            {
                path = path[..^"?anonymous".Length];
                user = null;
            }

            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, path);

            if (user is not null)
            {
                request.Headers.Add(TestUserHeader, user);
            }

            using var response = await this._client.SendAsync(request);

            return new WireResponse(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>
    /// A response body whose first write fails without forwarding a byte — after a yield, or, when
    /// <paramref name="alreadyFaulted"/>, as a write that has already faulted — and whose later writes go through. The
    /// failure after a yield is an <see cref="InvalidOperationException"/>, or, when <paramref name="unsupported"/>, a
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    private sealed class FaultsOnFirstWriteStream(Stream inner, bool alreadyFaulted, bool unsupported) : Stream
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

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (this._hasFaulted)
            {
                return inner.WriteAsync(buffer, cancellationToken);
            }

            this._hasFaulted = true;

            return alreadyFaulted
                ? ValueTask.FromException(new InvalidOperationException("boom-at-once"))
                : FaultAfterYieldAsync(unsupported);

            static async ValueTask FaultAfterYieldAsync(bool unsupported)
            {
                await Task.Yield();
                throw unsupported
                    ? new NotSupportedException("boom-during-write")
                    : new InvalidOperationException("boom-during-write");
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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

/// <summary>
/// Anonymous GET returning a <see cref="Blueprint.Http.ProxyResult"/> whose upstream content reports, under the request's
/// <c>key</c>, when it is disposed: the result writer tests answer it without executing it.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.RootLink("/result-writer/proxy")]
[Blueprint.Model.Attributes.HttpGet]
[Blueprint.Auditing.DoNotAudit]
public partial class ResultWriterProxyQuery : Blueprint.Model.IQuery<Blueprint.Http.ProxyResult>
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Disposals = new();

    /// <summary>Gets or sets the key the upstream content's disposal is reported under.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Starts tracking the disposal of the upstream content a request with <paramref name="key"/> creates.</summary>
    /// <param name="key">The request's key.</param>
    /// <returns>A task that completes when that content is disposed.</returns>
    public static Task Track(string key)
    {
        return Disposals.GetOrAdd(key, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    /// <summary>Simulates an upstream response handed over unread, as a forward hands one over.</summary>
    /// <param name="query">The query, naming the key to report disposal under.</param>
    /// <returns>A proxy result owning the simulated upstream response.</returns>
    public static Task<Blueprint.Http.ProxyResult> HandleAsync(ResultWriterProxyQuery query)
    {
        var upstream = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new DisposalReportingContent(query.Key),
        };

        return Task.FromResult(new Blueprint.Http.ProxyResult(upstream));
    }

    private sealed class DisposalReportingContent(string key) : System.Net.Http.StringContent("upstream")
    {
        protected override void Dispose(bool disposing)
        {
            if (Disposals.TryGetValue(key, out var disposed))
            {
                disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }
}
