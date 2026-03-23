using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class ConcurrentExecutorDispatchIntegrationTests
{
    [Fact]
    public async Task When_dispatching_same_operation_concurrently_then_stress_harness_reports_all_dispatches_completed()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var descriptor = ConcurrentDispatchOperation.Configure();

        ConcurrentDispatchOperation.Reset();

        var summary = await ConcurrentExecutorDispatchStressHarness.ExecuteAsync(
            app,
            executor,
            descriptor,
            dispatchCount: 64,
            configureContext: static context => ((ConcurrentDispatchOperation)context.Operation).Name = "Blueprint");

        summary.RequestedDispatchCount.Should().Be(64);
        summary.CompletedDispatchCount.Should().Be(64);
        summary.FaultedDispatchCount.Should().Be(0);
        summary.Payloads.Should().OnlyContain(static payload => payload == "Blueprint");
        ConcurrentDispatchOperation.HandlerCallCount.Should().Be(64);
    }
}

public partial class ConcurrentDispatchOperation : ICommand<string>
{
    private static int _handlerCallCount;

    public static int HandlerCallCount => _handlerCallCount;

    public string? Name { get; set; }

    public static void Reset()
    {
        Interlocked.Exchange(ref _handlerCallCount, 0);
    }

    public static Task<string> HandleAsync(ConcurrentDispatchOperation operation)
    {
        Interlocked.Increment(ref _handlerCallCount);
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

internal static class ConcurrentExecutorDispatchStressHarness
{
    public static async Task<ConcurrentExecutorDispatchSummary> ExecuteAsync(
        BlueprintApp app,
        BlueprintAppExecutor executor,
        ApiOperationDescriptor descriptor,
        int dispatchCount,
        Action<ApiOperationContext> configureContext)
    {
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new Task<ConcurrentExecutorDispatchOutcome>[dispatchCount];

        for (var index = 0; index < dispatchCount; index++)
        {
            tasks[index] = ExecuteDispatchAsync(app, executor, descriptor, configureContext, startGate.Task);
        }

        startGate.SetResult(true);

        var outcomes = await Task.WhenAll(tasks);
        var payloads = new List<string?>(dispatchCount);
        var completedDispatchCount = 0;
        var faultedDispatchCount = 0;

        foreach (var outcome in outcomes)
        {
            if (outcome.Exception is not null)
            {
                faultedDispatchCount++;
                continue;
            }

            completedDispatchCount++;
            payloads.Add(outcome.Payload);
        }

        return new ConcurrentExecutorDispatchSummary(dispatchCount, completedDispatchCount, faultedDispatchCount, payloads);
    }

    private static async Task<ConcurrentExecutorDispatchOutcome> ExecuteDispatchAsync(
        BlueprintApp app,
        BlueprintAppExecutor executor,
        ApiOperationDescriptor descriptor,
        Action<ApiOperationContext> configureContext,
        Task startSignal)
    {
        await startSignal;

        try
        {
            var context = CreateContext(app, descriptor);
            configureContext(context);

            var result = await executor.ExecuteAsync(context);
            var payload = ((OkResult)result).Unwrap<string>();
            return ConcurrentExecutorDispatchOutcome.Success(payload);
        }
        catch (Exception exception)
        {
            return ConcurrentExecutorDispatchOutcome.Failure(exception);
        }
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(EmptyServiceProvider.Instance, app, descriptor, CancellationToken.None);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

internal sealed record ConcurrentExecutorDispatchSummary(
    int RequestedDispatchCount,
    int CompletedDispatchCount,
    int FaultedDispatchCount,
    IReadOnlyList<string?> Payloads);

internal sealed record ConcurrentExecutorDispatchOutcome(string? Payload, Exception? Exception)
{
    public static ConcurrentExecutorDispatchOutcome Success(string payload)
    {
        return new ConcurrentExecutorDispatchOutcome(payload, null);
    }

    public static ConcurrentExecutorDispatchOutcome Failure(Exception exception)
    {
        return new ConcurrentExecutorDispatchOutcome(null, exception);
    }
}
