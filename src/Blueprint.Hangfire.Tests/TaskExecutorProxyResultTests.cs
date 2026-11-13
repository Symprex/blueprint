using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Tasks;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// <see cref="TaskExecutor"/> discards the result of the task it runs, so it is the last to see a
/// <see cref="ProxyResult"/>'s upstream response, which it used to leave undisposed.
/// </summary>
public class TaskExecutorProxyResultTests
{
    [Fact]
    public async Task When_a_task_returns_a_proxy_result_then_its_upstream_response_is_disposed()
    {
        var content = new TrackedContent();
        var app = new BlueprintApp();
        app.Register(new ApiOperationDescriptor(typeof(ProxyTask), nameof(ProxyTask), "test"));
        var executor = new BlueprintAppExecutor(app);
        executor.Register(typeof(ProxyTask), new FixedResultPipeline(new ProxyResult(new HttpResponseMessage { Content = content })));
        using var services = new ServiceCollection().BuildServiceProvider();

        await new TaskExecutor(executor, services).ExecuteAsync(new BackgroundTaskEnvelope(new ProxyTask()));

        content.IsDisposed.Should().BeTrue();
    }

    public sealed class ProxyTask : IBackgroundTask
    {
    }

    private sealed class FixedResultPipeline(OperationResult result) : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TrackedContent() : StringContent("upstream")
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
