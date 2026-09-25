using Blueprint.Marten;
using Blueprint.Persistence;
using Blueprint.Rebus;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;
using Weasel.Core;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that a generated pipeline fails, rather than silently dropping the work, when an enlisted outbox queues work on
/// the Marten session after the pipeline has saved it — the trap a direct port of legacy's
/// <c>UseMartenSessionAsOutbox</c> falls into.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's outbox transport queued each sent message's outbox row on the session from a commit callback of the Rebus
/// transaction, and legacy completed that transaction before it saved the session (Signature365 <c>8b40ecc961</c>,
/// <c>AzureAdOauthReturnCommandExecutorPipeline.cs:133-137</c>), so the rows were saved. A generated pipeline saves the
/// session first and completes the transaction last, so the same callback queues the rows on a session that has already
/// saved and never saves again: the messages were neither sent nor stored, and nothing said so.
/// </para>
/// <para>
/// No database is needed: the session has nothing to save when the pipeline saves it, so Marten never opens a connection,
/// and the rows queued afterwards are never executed.
/// </para>
/// </remarks>
public class MartenSessionQueuedAfterSaveIntegrationTests
{
    [Fact]
    public async Task When_an_enlisted_outbox_queues_on_the_session_after_the_save_then_the_pipeline_fails_instead_of_losing_it()
    {
        using var store = CreateUnreachableStore();
        await using var provider = BuildProvider(store);
        using var scope = provider.CreateScope();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, provider);
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(SendOnlyOutboxCommand)), CancellationToken.None);

        var result = await executor.GetPipeline(typeof(SendOnlyOutboxCommand)).ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>()
            .Which.Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("after it was saved");
    }

    private static DocumentStore CreateUnreachableStore()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
        options.AutoCreateSchemaObjects = AutoCreate.None;

        return new DocumentStore(options);
    }

    private static ServiceProvider BuildProvider(DocumentStore store)
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<IDocumentSession>(_ => store.LightweightSession());
        services.AddScoped<IUnitOfWork, MartenUnitOfWork>();
        services.AddScoped<IRebusTransactionEnlistment, LegacyStyleOutboxEnlistment>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// As legacy's <c>UseMartenSessionAsOutbox</c> did: puts the session into the transaction, for the outbox transport to
    /// queue its rows on.
    /// </summary>
    private sealed class LegacyStyleOutboxEnlistment(IDocumentSession session) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.Items[LegacyStyleOutbox.SessionKey] = session;

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Stands in for legacy's outbox transport: a send registers, once, a commit callback that queues the message's outbox
/// row on the enlisted session.
/// </summary>
public static class LegacyStyleOutbox
{
    /// <summary>The transaction item the enlisted session is kept in, as legacy's <c>current-outbox-connection</c>.</summary>
    public const string SessionKey = "current-outbox-connection";

    /// <summary>Sends <paramref name="messageId"/> through the ambient transaction's outbox.</summary>
    /// <param name="messageId">The id of the message sent.</param>
    public static void Send(string messageId)
    {
        var transaction = AmbientTransactionContext.Current
                          ?? throw new InvalidOperationException("A send needs the ambient Rebus transaction.");
        var session = (IDocumentSession)transaction.Items[SessionKey];

        transaction.OnCommit(_ =>
        {
            session.QueueSqlCommand("insert into outbox (id) values (?)", messageId);

            return Task.CompletedTask;
        });
    }
}

/// <summary>A command whose handler sends a message and writes nothing.</summary>
public partial class SendOnlyOutboxCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(SendOnlyOutboxCommand operation)
    {
        LegacyStyleOutbox.Send("MSG-1");

        return Task.CompletedTask;
    }
}
