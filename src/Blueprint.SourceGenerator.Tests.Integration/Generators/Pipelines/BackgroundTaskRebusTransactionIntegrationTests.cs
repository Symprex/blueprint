using Blueprint.Rebus;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that a generated background-task pipeline runs its handler inside a Rebus transaction scope, as a command's
/// does, so that messages the task sends are held until it succeeds and dropped when it fails; and that
/// <see cref="NoRebusTransactionAttribute"/> opts a task out.
/// </summary>
/// <remarks>
/// Rebus attaches outgoing messages to the ambient transaction context and sends them from its commit callbacks, so a
/// commit callback registered by the handler stands in for a message it sent.
/// </remarks>
public class BackgroundTaskRebusTransactionIntegrationTests
{
    [Fact]
    public async Task When_background_task_succeeds_then_its_messages_are_committed_after_the_handler()
    {
        var (context, pipeline) = CreatePipeline<RebusScopedBackgroundTask>();
        var probe = new RebusScopeProbe();
        ((RebusScopedBackgroundTask)context.Operation).Probe = probe;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        probe.HandlerSawAmbientTransaction.Should().BeTrue("the handler must run inside the task's Rebus transaction scope");
        probe.Committed.Should().BeTrue("the scope is completed once the task has succeeded");
        AmbientTransactionContext.Current.Should().BeNull("the task's scope must not outlive its pipeline");
    }

    [Fact]
    public async Task When_background_task_fails_then_its_messages_are_not_committed()
    {
        var (context, pipeline) = CreatePipeline<RebusScopedBackgroundTask>();
        var probe = new RebusScopeProbe { Fail = true };
        ((RebusScopedBackgroundTask)context.Operation).Probe = probe;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        probe.HandlerSawAmbientTransaction.Should().BeTrue();
        probe.Committed.Should().BeFalse("a failed task's messages must be discarded with its scope");
    }

    [Fact]
    public async Task When_background_task_has_NoRebusTransaction_then_the_handler_runs_without_a_scope()
    {
        var (context, pipeline) = CreatePipeline<UnscopedBackgroundTask>();
        var probe = new RebusScopeProbe();
        ((UnscopedBackgroundTask)context.Operation).Probe = probe;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        probe.HandlerSawAmbientTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task When_background_task_runs_inside_an_outer_rebus_scope_then_it_commits_its_own_and_restores_the_outer()
    {
        // The case where a task is executed while another transaction is ambient — a Rebus handler, or a command whose
        // flush runs the task inline. Rebus 8 nests scopes rather than rejecting the inner one.
        var (context, pipeline) = CreatePipeline<RebusScopedBackgroundTask>();
        var probe = new RebusScopeProbe();
        ((RebusScopedBackgroundTask)context.Operation).Probe = probe;

        using var outer = new RebusTransactionScope();
        var outerContext = AmbientTransactionContext.Current;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        probe.HandlerTransaction.Should().NotBeSameAs(outerContext, "the task gets a scope of its own");
        probe.Committed.Should().BeTrue("the task's scope commits when the task succeeds, not when the outer one does");
        AmbientTransactionContext.Current.Should().BeSameAs(outerContext, "the outer scope must be ambient again afterwards");
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline) CreatePipeline<TTask>()
        where TTask : IBackgroundTask
    {
        // The pipeline decides at construction whether Rebus is available, from whether an IBus is registered.
        var serviceProvider = new BusServiceProvider(Substitute.For<IBus>());
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var descriptor = app.GetOperationsForHost("tasks").First(static op => op.OperationType == typeof(TTask));
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None);

        return (context, executor.GetPipeline(typeof(TTask)));
    }

    private sealed class BusServiceProvider(IBus bus) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IBus) ? bus : null;
        }
    }
}

/// <summary>Records what a background-task handler observed of the Rebus transaction it ran in.</summary>
public sealed class RebusScopeProbe
{
    public bool Fail { get; init; }

    public bool HandlerSawAmbientTransaction { get; private set; }

    public ITransactionContext? HandlerTransaction { get; private set; }

    public bool Committed { get; private set; }

    public Task RunAsync()
    {
        this.HandlerTransaction = AmbientTransactionContext.Current;
        this.HandlerSawAmbientTransaction = this.HandlerTransaction is not null;

        this.HandlerTransaction?.OnCommit(_ =>
        {
            this.Committed = true;

            return Task.CompletedTask;
        });

        return this.Fail
            ? throw new InvalidOperationException("The background task failed.")
            : Task.CompletedTask;
    }
}

public partial class RebusScopedBackgroundTask : IBackgroundTask
{
    public RebusScopeProbe Probe { get; set; } = null!;

    public static Task HandleAsync(RebusScopedBackgroundTask operation)
    {
        return operation.Probe.RunAsync();
    }
}

[NoRebusTransaction]
public partial class UnscopedBackgroundTask : IBackgroundTask
{
    public RebusScopeProbe Probe { get; set; } = null!;

    public static Task HandleAsync(UnscopedBackgroundTask operation)
    {
        return operation.Probe.RunAsync();
    }
}
