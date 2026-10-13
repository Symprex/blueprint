using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that, on the router's synchronous-execution fast path, a result that writes itself (one the result executor
/// does not translate, so the router runs its own <see cref="OperationResult.ExecuteAsync"/>) and faults is not swallowed
/// into a clean, completed response: it is answered with the exception body while the response has not started, and
/// aborts the connection once it has — as the router's slow path already answers the same fault.
/// </summary>
/// <remarks>
/// <para>
/// <c>HandleResultSyncPath</c> returned <c>legacyTask.IsCompleted ? Task.CompletedTask : legacyTask</c>: a task that had
/// already faulted is completed too, so its exception was dropped and the request ended as a 200 with no body; and a
/// synchronous throw from <c>ExecuteAsync</c> escaped the request delegate, which ASP.NET Core answers with an empty 500.
/// </para>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) awaited a result's <c>ExecuteAsync</c> in its router with no catch
/// (<c>Blueprint.Http/ApplicationBuilderExtensions.cs:244-247</c>); the exception body came from its result executor,
/// which caught a write fault only while the response had not started (<c>OkResultOperationExecutor.cs:48-65</c>). This
/// framework's router already answers a self-executing result's fault that way on its slow path
/// (<c>RouteOperationSlowPathAsync</c>) and a write fault that way on this one (R105, R118); the fast path's
/// self-executing branch now does the same.
/// </para>
/// <para>
/// A generated pipeline cannot return such a result — BP108 refuses a result type the HTTP executor does not translate —
/// so the result comes from a hand-rolled pipeline, the case the router's own defensive handling names, registered for
/// <see cref="GetSyncPathWriteFaultQuery"/>'s route. It completes synchronously, so the router takes the fast path.
/// </para>
/// </remarks>
public sealed class SelfExecutingResultFaultWireTests
{
    [Theory]
    [InlineData(SelfExecutingFaultResult.CompletedFaulted)]
    [InlineData(SelfExecutingFaultResult.SynchronousThrow)]
    [InlineData(SelfExecutingFaultResult.FaultAfterYield)]
    public async Task A_self_executing_result_that_faults_before_the_response_starts_is_answered_with_the_exception_body(string mode)
    {
        await using var host = await SelfExecutingResultFaultHost.StartAsync();

        using var response = await host.GetAsync(mode);
        var contentType = response.Content.Headers.ContentType?.ToString();
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().Be(500);
        contentType.Should().StartWith("application/json");
        body.Should().Contain("boom-self-executing-" + mode);
    }

    [Fact]
    public async Task A_self_executing_result_that_faults_after_starting_the_response_aborts_the_connection()
    {
        await using var host = await SelfExecutingResultFaultHost.StartAsync();

        var act = () => host.GetAsync(SelfExecutingFaultResult.FaultAfterStarting);

        await act.Should().ThrowAsync<Exception>("the connection was aborted mid-body, not completed cleanly");
    }

    /// <summary>The integration app, served by Kestrel through <see cref="SelfExecutingFaultExecutor"/>.</summary>
    private sealed class SelfExecutingResultFaultHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private SelfExecutingResultFaultHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<SelfExecutingResultFaultHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.Configure<Blueprint.Runtime.Http.BlueprintHttpOptions>(options =>
            {
                options.NegotiateErrorContentType = true;
                options.ExposeExceptionDetailsInErrorResponses = true;
            });
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, SelfExecutingFaultExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, SelfExecutingFaultExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new SelfExecutingResultFaultHost(app, client);
        }

        public Task<System.Net.Http.HttpResponseMessage> GetAsync(string mode)
        {
            return this._client.GetAsync("sync-path-write-fault?mode=" + mode);
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>An executor whose one pipeline, for <see cref="GetSyncPathWriteFaultQuery"/>, is <see cref="SelfExecutingFaultPipeline"/>.</summary>
    private sealed class SelfExecutingFaultExecutor : BlueprintAppExecutor
    {
        public SelfExecutingFaultExecutor(IntegrationTestBlueprintApp app)
            : base(app)
        {
            this.Register(typeof(GetSyncPathWriteFaultQuery), new SelfExecutingFaultPipeline());
        }
    }

    /// <summary>
    /// A hand-rolled pipeline that completes synchronously with a <see cref="SelfExecutingFaultResult"/> of the request's
    /// <c>mode</c>.
    /// </summary>
    private sealed class SelfExecutingFaultPipeline : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return new ValueTask<OperationResult>(new SelfExecutingFaultResult(context.GetHttpContext().Request.Query["mode"].ToString()));
        }
    }

    /// <summary>A result the HTTP result executor does not translate, whose own <see cref="ExecuteAsync"/> faults as its mode says.</summary>
    private sealed class SelfExecutingFaultResult(string mode) : OperationResult
    {
        /// <summary>Returns a task that has already faulted.</summary>
        public const string CompletedFaulted = "completed-faulted";

        /// <summary>Throws from <see cref="ExecuteAsync"/> itself, returning no task.</summary>
        public const string SynchronousThrow = "synchronous-throw";

        /// <summary>Returns a task that faults once it has yielded, before writing anything.</summary>
        public const string FaultAfterYield = "fault-after-yield";

        /// <summary>Writes and flushes part of a body, starting the response, then faults.</summary>
        public const string FaultAfterStarting = "fault-after-starting";

        public override Task ExecuteAsync(ApiOperationContext context)
        {
            return mode switch
            {
                CompletedFaulted => Task.FromException(new InvalidOperationException("boom-self-executing-" + mode)),
                SynchronousThrow => throw new InvalidOperationException("boom-self-executing-" + mode),
                FaultAfterYield => FaultAfterYieldAsync(),
                FaultAfterStarting => FaultAfterStartingAsync(context.GetHttpContext()),
                _ => Task.CompletedTask,
            };
        }

        private static async Task FaultAfterYieldAsync()
        {
            await Task.Yield();

            throw new InvalidOperationException("boom-self-executing-" + FaultAfterYield);
        }

        private static async Task FaultAfterStartingAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "text/plain";
            await httpContext.Response.WriteAsync("a partial body");
            await httpContext.Response.Body.FlushAsync();
            await Task.Yield();

            throw new InvalidOperationException("boom-self-executing-" + FaultAfterStarting);
        }
    }
}
