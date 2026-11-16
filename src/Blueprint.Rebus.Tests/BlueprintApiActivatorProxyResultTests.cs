using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Config;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// <see cref="BlueprintApiActivator"/> discards the result of the operation a message runs, so it is the last to see a
/// <see cref="ProxyResult"/>'s upstream response, which it used to leave undisposed.
/// </summary>
public class BlueprintApiActivatorProxyResultTests
{
    [Theory]
    [InlineData(typeof(ProxyMessage))]
    [InlineData(typeof(UntransactedProxyMessage))]
    public async Task Rebus_bus_disposes_the_upstream_response_of_a_proxy_result_an_operation_message_returns(Type messageType)
    {
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var network = new InMemNetwork();

        var app = new BlueprintApp();
        app.Register(new ApiOperationDescriptor(messageType, messageType.Name, "test"));
        var executor = new BlueprintAppExecutor(app);
        executor.Register(messageType, new ProxyReturningPipeline(handled, disposed));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(app);
        services.AddSingleton(executor);
        services.AddSingleton(new BlueprintRebusConfiguration());
        using var provider = services.BuildServiceProvider();

        using var bus = Configure.With(new BlueprintApiActivator(provider))
            .Transport(t => t.UseInMemoryTransport(network, "input"))
            .Start();

        await bus.SendLocal(Activator.CreateInstance(messageType)!);

        (await Task.WhenAny(handled.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(handled.Task, "the message runs its operation");
        (await Task.WhenAny(disposed.Task, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(disposed.Task, "the upstream response is disposed once the message is handled");
        network.GetCount("error").Should().Be(0);
    }

    public sealed class ProxyMessage;

    [NoRebusTransaction]
    public sealed class UntransactedProxyMessage;

    private sealed class ProxyReturningPipeline(TaskCompletionSource handled, TaskCompletionSource disposed) : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            handled.TrySetResult();

            return ValueTask.FromResult<OperationResult>(new ProxyResult(new HttpResponseMessage { Content = new ReportingContent(disposed) }));
        }
    }

    private sealed class ReportingContent(TaskCompletionSource disposed) : StringContent("upstream")
    {
        protected override void Dispose(bool disposing)
        {
            disposed.TrySetResult();
            base.Dispose(disposing);
        }
    }
}
