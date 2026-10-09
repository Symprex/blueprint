using Blueprint.Model;
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
/// Proves that a query's generated pipeline opens a Rebus transaction scope, enlists it and saves the unit of work, as a
/// command's does, unless the query is marked <c>[DoNotSaveChanges]</c> — and that a query run nested, as a resource
/// event's self query is, does none of that.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>SaveMartenChangesMiddleware</c> and <c>UseMartenAsOutboxMiddleware</c> matched any operation without
/// <c>[DoNotSaveChanges]</c> (Signature365 <c>8b40ecc961</c>, <c>Common/Data/Marten/SaveMartenChangesMiddleware.cs:24-29</c>,
/// <c>UseMartenAsOutboxMiddleware.cs:26-31</c>), and <c>StartRebusTransactionScopeMiddleware</c> any without
/// <c>[NoRebusTransaction]</c> (<c>BlueprintRebus/StartRebusTransactionScopeMiddleware.cs:25-30</c>). Portal's
/// <c>Microsoft365AddOnConnectQueryExecutorPipeline.cs:114-134</c> opens the scope, enlists the session, runs the handler,
/// completes the scope and saves the session.
/// </para>
/// <para>
/// All four middlewares declared <c>SupportsNestedExecution =&gt; false</c>, and legacy ran a nested context through the
/// pipeline built without them (<c>Blueprint/CodeGennedExecutor.cs:99-100</c>): the same query's
/// <c>ExecuteNestedAsync</c> (<c>:180-253</c>) has no scope, enlistment or save. A resource event's self query is the one
/// nested execution this framework makes (<c>ResourceEventHandler.cs:116</c> in legacy), and it shares its command's scope
/// and session.
/// </para>
/// </remarks>
public class QueryUnitOfWorkIntegrationTests
{
    [Fact]
    public async Task When_a_query_saves_a_unit_of_work_then_its_transaction_is_enlisted_before_the_handler_and_saved_after()
    {
        await using var provider = BuildProvider();

        var (result, log) = await ExecuteAsync<EnlistedLookupQuery>(provider);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("enlist", "handler", "save", "commit");
        log.EnlistedTransaction.Should().NotBeNull()
            .And.BeSameAs(log.HandlerAmbientTransaction, "the transaction enlisted is the one the handler's messages go into");
    }

    [Fact]
    public async Task When_a_query_has_DoNotSaveChanges_then_its_scope_opens_but_it_is_neither_enlisted_nor_saved()
    {
        // Legacy's StartRebusTransactionScopeMiddleware looked only for [NoRebusTransaction], so the scope opened; its save
        // and outbox middlewares both stood down for [DoNotSaveChanges] (Outlook GetAddInSignaturesQuery.cs:35 carried it).
        await using var provider = BuildProvider();

        var (result, log) = await ExecuteAsync<NotSavingEnlistedLookupQuery>(provider);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("handler");
        log.HandlerAmbientTransaction.Should().NotBeNull("the query still runs in a Rebus transaction scope of its own");
    }

    [Fact]
    public async Task When_a_commands_self_query_runs_nested_then_it_neither_opens_a_scope_nor_enlists_nor_saves()
    {
        // The command enlists its own transaction, saves before its self query runs (so the query can read what it wrote)
        // and saves again after its handler, then commits. The self query, GetSelfQueryTestResourceQuery, adds nothing: a
        // scope of its own would add an "enlist" and a "commit", and a save of its own a "save".
        await using var provider = BuildProvider();

        var (result, log) = await ExecuteAsync<DeferredSelfQueryCommand>(provider);

        result.WasSuccessful.Should().BeTrue();
        ((IDeferredResourceEventResult)result).ResolvedContent.Should().NotBeNull("the self query ran");
        log.Entries.Should().Equal("enlist", "save", "save", "commit");
    }

    private static async Task<(OperationResult Result, EnlistmentLog Log)> ExecuteAsync<TOperation>(ServiceProvider provider)
    {
        var app = provider.GetRequiredService<IntegrationTestBlueprintApp>();
        var executor = provider.GetRequiredService<BlueprintAppExecutor>();
        using var scope = provider.CreateScope();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await executor.GetPipeline(typeof(TOperation)).ExecuteAsync(context);

        return (result, scope.ServiceProvider.GetRequiredService<EnlistmentLog>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered, and runs a self query through the executor
        // it resolves from the root provider.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddSingleton<IntegrationTestBlueprintApp>();
        services.AddSingleton<BlueprintAppExecutor>(static sp =>
            new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, sp.GetRequiredService<IntegrationTestBlueprintApp>(), sp));
        services.AddScoped<EnlistmentLog>();
        services.AddScoped<IRebusTransactionEnlistment, LoggingEnlistment>();
        services.AddScoped<IUnitOfWork, LoggingUnitOfWork>();

        return services.BuildServiceProvider();
    }

    private sealed class LoggingEnlistment(EnlistmentLog log) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Entries.Add("enlist");
            log.EnlistedTransaction = transactionContext;

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

/// <summary>A query whose pipeline saves the unit of work (R138).</summary>
public partial class EnlistedLookupQuery : IQuery<string>
{
    public static string Handle(EnlistedLookupQuery operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");
        log.HandlerAmbientTransaction = AmbientTransactionContext.Current;

        return "looked-up";
    }
}

/// <summary>A query whose pipeline does not save the unit of work (R138).</summary>
[DoNotSaveChanges]
public partial class NotSavingEnlistedLookupQuery : IQuery<string>
{
    public static string Handle(NotSavingEnlistedLookupQuery operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");
        log.HandlerAmbientTransaction = AmbientTransactionContext.Current;

        return "looked-up";
    }
}
