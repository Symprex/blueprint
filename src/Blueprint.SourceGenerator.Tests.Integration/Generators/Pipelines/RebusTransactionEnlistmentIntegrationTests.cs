using Blueprint.Persistence;
using Blueprint.Rebus;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that a generated pipeline which saves a unit of work calls the consumer's
/// <see cref="IRebusTransactionEnlistment"/>s with its Rebus transaction, before the handler runs, and saves the unit of
/// work before any commit callback of that transaction — the order an outbox enlisted in the unit of work needs.
/// </summary>
public class RebusTransactionEnlistmentIntegrationTests
{
    [Fact]
    public async Task When_command_saves_a_unit_of_work_then_its_rebus_transaction_is_enlisted_before_the_handler_runs()
    {
        await using var provider = BuildProvider(registerUnitOfWork: true);
        var (context, pipeline, log) = Create<EnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("enlist", "handler", "save", "commit");
        log.EnlistedTransaction.Should().NotBeNull()
            .And.BeSameAs(log.HandlerAmbientTransaction, "the transaction enlisted is the one the handler's messages go into");
    }

    [Fact]
    public async Task When_command_does_not_save_a_unit_of_work_then_its_transaction_is_not_enlisted()
    {
        await using var provider = BuildProvider(registerUnitOfWork: true);
        var (context, pipeline, log) = Create<NotSavingEnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("handler");
    }

    [Fact]
    public async Task When_no_unit_of_work_is_registered_then_the_transaction_is_not_enlisted()
    {
        // Nothing would save the outbox, so its messages would never be written.
        await using var provider = BuildProvider(registerUnitOfWork: false);
        var (context, pipeline, log) = Create<EnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("handler");
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline, EnlistmentLog Log) Create<TCommand>(ServiceProvider provider)
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, provider);
        var scope = provider.CreateScope();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(TCommand)), CancellationToken.None);

        return (context, executor.GetPipeline(typeof(TCommand)), scope.ServiceProvider.GetRequiredService<EnlistmentLog>());
    }

    private static ServiceProvider BuildProvider(bool registerUnitOfWork)
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<EnlistmentLog>();
        services.AddScoped<IRebusTransactionEnlistment, LoggingEnlistment>();

        if (registerUnitOfWork)
        {
            services.AddScoped<IUnitOfWork, LoggingUnitOfWork>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class LoggingEnlistment(EnlistmentLog log) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Entries.Add("enlist");
            log.EnlistedTransaction = transactionContext;

            // Stands in for the transport's send callback and a consumer's outbox check alike.
            transactionContext.OnCommit(_ =>
            {
                log.Entries.Add("commit");

                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingUnitOfWork(EnlistmentLog log) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            log.Entries.Add("save");

            return Task.CompletedTask;
        }
    }
}

/// <summary>Records what the enlistment, the handler and the unit of work saw, in order.</summary>
public sealed class EnlistmentLog
{
    public List<string> Entries { get; } = [];

    public ITransactionContext? EnlistedTransaction { get; set; }

    public ITransactionContext? HandlerAmbientTransaction { get; set; }
}

/// <summary>A command whose pipeline saves the unit of work.</summary>
public partial class EnlistedCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(EnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");
        log.HandlerAmbientTransaction = AmbientTransactionContext.Current;

        return Task.CompletedTask;
    }
}

/// <summary>A command whose pipeline does not save the unit of work.</summary>
[DoNotSaveChanges]
public partial class NotSavingEnlistedCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(NotSavingEnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");

        return Task.CompletedTask;
    }
}
