using System.Collections.Concurrent;
using System.Net;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that <c>UseBlueprintTaskRunner</c> releases, after every request, the background tasks code outside a Blueprint
/// operation enqueued through the request's <see cref="IBackgroundTaskScheduler"/>, as legacy's did, and never the tasks
/// of a Blueprint operation, whose own pipeline releases them on success only.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Tasks.AspNetCore/TaskRunnerMiddleware.cs:32-36</c>) awaited the
/// rest of the pipeline and then called <c>RunNowAsync()</c> on the request's scheduler, whatever the status, and not at all
/// when the rest of the pipeline threw. Signature365's IdentityServer registered it (<c>Startup.cs:374</c>) for its Razor
/// pages and email verification services.
/// </para>
/// <para>
/// Legacy's HTTP host ran each operation in a scope of its own (<c>Blueprint.Http/ApplicationBuilderExtensions.cs:213</c>),
/// so the scheduler the middleware flushed never held an operation's tasks. This host runs an operation in the request's
/// scope, so the tasks of an operation that failed after enqueuing them are still in the request's scheduler afterwards,
/// and must not be released from it.
/// </para>
/// </remarks>
public sealed class TaskRunnerWireTests
{
    [Fact]
    public async Task A_task_a_plain_endpoint_enqueues_is_dispatched_after_the_endpoint_returns()
    {
        await using var host = await TaskRunnerHost.StartAsync();

        using var response = await host.Client.PostAsync("plain/no-content", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Log.Should().Equal("endpoint no-content", "dispatch plain no-content");
    }

    [Fact]
    public async Task A_task_a_plain_endpoint_enqueues_is_dispatched_whatever_status_the_endpoint_answers()
    {
        await using var host = await TaskRunnerHost.StartAsync();

        using var response = await host.Client.PostAsync("plain/bad-request", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Log.Should().Equal("endpoint bad-request", "dispatch plain bad-request");
    }

    [Fact]
    public async Task A_task_a_plain_endpoint_enqueues_before_it_throws_is_not_dispatched()
    {
        await using var host = await TaskRunnerHost.StartAsync();

        using var response = await host.Client.PostAsync("plain/throw", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        host.Log.Should().Equal("endpoint throw");
    }

    [Fact]
    public async Task Without_the_task_runner_a_task_a_plain_endpoint_enqueues_is_not_dispatched()
    {
        await using var host = await TaskRunnerHost.StartAsync(useTaskRunner: false);

        using var response = await host.Client.PostAsync("plain/no-content", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        host.Log.Should().Equal("endpoint no-content");
    }

    [Fact]
    public async Task A_Blueprint_operations_task_is_dispatched_once_by_its_own_pipeline()
    {
        await using var host = await TaskRunnerHost.StartAsync();

        using var response = await host.Client.PostAsync("api/task-runner/succeed", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Log.Should().Equal("dispatch operation succeed");
    }

    [Fact]
    public async Task A_Blueprint_operation_that_fails_after_enqueuing_a_task_dispatches_nothing()
    {
        await using var host = await TaskRunnerHost.StartAsync();

        using var response = await host.Client.PostAsync("api/task-runner/fail", content: null);
        await host.WaitForRequestAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        host.Log.Should().BeEmpty("legacy ran the operation in a scope of its own, whose tasks nothing released once it failed");
    }

    /// <summary>
    /// The integration app and a plain endpoint, served by Kestrel, with the production scheduler over a recording provider.
    /// </summary>
    private sealed class TaskRunnerHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly SemaphoreSlim _completedRequests;
        private readonly TaskRunnerLog _log;

        private TaskRunnerHost(WebApplication app, System.Net.Http.HttpClient client, SemaphoreSlim completedRequests, TaskRunnerLog log)
        {
            this._app = app;
            this.Client = client;
            this._completedRequests = completedRequests;
            this._log = log;
        }

        public System.Net.Http.HttpClient Client { get; }

        public IReadOnlyList<string> Log => [.. this._log.Entries];

        public static async Task<TaskRunnerHost> StartAsync(bool useTaskRunner = true)
        {
            var completedRequests = new SemaphoreSlim(0);
            var log = new TaskRunnerLog();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(log);
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint
                    .AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>()
                    .AddBackgroundTaskScheduleProvider<RecordingScheduleProvider>());

            var app = builder.Build();

            // Outside the task runner, so a test can wait until the server has finished with a request, dispatch included,
            // however early the client was answered.
            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                finally
                {
                    completedRequests.Release();
                }
            });

            if (useTaskRunner)
            {
                app.UseBlueprintTaskRunner();
            }

            // As a Razor page or a service outside any operation would: the request's own scheduler.
            app.MapPost("/plain/{outcome}", static context =>
            {
                var outcome = (string)context.GetRouteValue("outcome")!;

                context.RequestServices.GetRequiredService<TaskRunnerLog>().Entries.Enqueue($"endpoint {outcome}");
                context.RequestServices.GetRequiredService<IBackgroundTaskScheduler>().Enqueue(new FollowOnTask { Source = $"plain {outcome}" });

                context.Response.StatusCode = outcome switch
                {
                    "no-content" => StatusCodes.Status204NoContent,
                    "bad-request" => StatusCodes.Status400BadRequest,
                    _ => throw new InvalidOperationException("The endpoint failed after enqueuing."),
                };

                return Task.CompletedTask;
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new TaskRunnerHost(app, client, completedRequests, log);
        }

        public async Task WaitForRequestAsync()
        {
            (await this._completedRequests.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the server should finish the request");
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
            this._completedRequests.Dispose();
        }
    }

    /// <summary>What the endpoints and the schedule provider did, in order.</summary>
    private sealed class TaskRunnerLog
    {
        public ConcurrentQueue<string> Entries { get; } = new();
    }

    private sealed class RecordingScheduleProvider(TaskRunnerLog log) : IBackgroundTaskScheduleProvider
    {
        public Task<string> EnqueueAsync(BackgroundTaskEnvelope task)
        {
            log.Entries.Enqueue($"dispatch {((FollowOnTask)task.Task).Source}");

            return Task.FromResult("job");
        }

        public Task<string> ScheduleAsync(BackgroundTaskEnvelope task, TimeSpan delay)
        {
            throw new NotSupportedException();
        }

        public Task<string> EnqueueChildAsync(BackgroundTaskEnvelope task, string parentId, BackgroundTaskContinuationOptions continuationOptions)
        {
            throw new NotSupportedException();
        }
    }
}

/// <summary>A command that enqueues a background task and then succeeds, or fails, as its <see cref="Outcome"/> names.</summary>
[AllowAnonymous]
[RootLink("/task-runner/{Outcome}")]
[HttpPost]
public partial class EnqueueTaskRunnerProbeCommand : ICommand<string>
{
    public string Outcome { get; set; } = null!;

    public static string Handle(EnqueueTaskRunnerProbeCommand command, IBackgroundTaskScheduler scheduler)
    {
        scheduler.Enqueue(new FollowOnTask { Source = $"operation {command.Outcome}" });

        return command.Outcome == "succeed"
            ? "enqueued"
            : throw new InvalidOperationException("The operation failed after enqueuing.");
    }
}
