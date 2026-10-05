using Blueprint.Marten.Tests.Postgres;
using FluentAssertions;
using Marten;
using Marten.Exceptions;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Establishes, against a real PostgreSQL, the two facts about Marten 6.4.2's save that an outbox drain listener depends
/// on: whether the listeners run for a session with nothing to save, and whether SQL a listener queues during the save
/// is executed in the same transaction as the session's documents.
/// </summary>
/// <remarks>
/// <para>
/// Both follow from <c>DocumentSessionBase.SaveChangesAsync</c> in Marten 6.4.2 (as decompiled): it returns when
/// <c>_workTracker.HasOutstandingWork()</c> is false (<c>:569-572</c>) before it calls any listener's
/// <c>BeforeSaveChangesAsync</c> (<c>:591-594</c>); and it builds the batch from <c>_workTracker.AllOperations</c> only after
/// those calls (<c>:595</c>), inside the transaction it began first (<c>:573</c>), so an operation
/// <see cref="IDocumentOperations.QueueSqlCommand"/> adds to the work tracker from a listener (<c>:207-227</c>) is part of
/// that batch. These tests pin the behaviour rather than the reading of it.
/// </para>
/// <para>
/// They need a database, so they are <see cref="PostgresFactAttribute"/>s: skipped where none is configured, as in CI.
/// </para>
/// </remarks>
public class MartenSaveListenerPostgresTests
{
    [PostgresFact]
    public async Task Marten_runs_no_listener_when_the_session_has_nothing_to_save()
    {
        var listener = new CountingListener();
        await using var database = await PostgresTestStore.CreateAsync(options => options.Listeners.Add(listener));
        await using var session = database.Store.LightweightSession();

        await session.SaveChangesAsync();

        listener.BeforeSaveCalls.Should().Be(0, "Marten returns from a save with no outstanding work before any listener");

        // The control: the same listener on the same session does run once there is work, so the zero above is not a
        // listener that was never attached.
        session.Store(new OrderDocument { Id = "ORD-1" });
        await session.SaveChangesAsync();

        listener.BeforeSaveCalls.Should().Be(1);
    }

    [PostgresFact]
    public async Task Sql_a_listener_queues_during_the_save_is_committed_with_the_sessions_documents()
    {
        await using var database = await PostgresTestStore.CreateAsync();
        database.Store.Options.Listeners.Add(new QueueingListener(database.OutboxTable, "MSG-1"));

        await using (var session = database.Store.LightweightSession())
        {
            session.Store(new OrderDocument { Id = "ORD-1" });
            await session.SaveChangesAsync();
        }

        (await database.ReadOutboxIdsAsync()).Should().Equal("MSG-1");
        await using var query = database.Store.QuerySession();
        (await query.LoadAsync<OrderDocument>("ORD-1")).Should().NotBeNull();
    }

    [PostgresFact]
    public async Task When_the_sql_a_listener_queued_fails_then_the_sessions_documents_are_rolled_back_with_it()
    {
        await using var database = await PostgresTestStore.CreateAsync();
        database.Store.Options.Listeners.Add(new QueueingListener(database.OutboxTable, "MSG-1"));

        // Already there, so the listener's insert violates the primary key.
        await database.InsertOutboxRowAsync("MSG-1");

        await using (var session = database.Store.LightweightSession())
        {
            session.Store(new OrderDocument { Id = "ORD-1" });

            var save = () => session.SaveChangesAsync();

            await save.Should().ThrowAsync<Exception>();
        }

        await using var query = database.Store.QuerySession();
        (await query.LoadAsync<OrderDocument>("ORD-1")).Should().BeNull("the document was in the batch whose transaction failed");
    }

    [PostgresFact]
    public async Task When_a_document_operation_fails_then_the_sql_a_listener_queued_is_rolled_back_with_it()
    {
        await using var database = await PostgresTestStore.CreateAsync();

        await using (var seed = database.Store.LightweightSession())
        {
            seed.Store(new OrderDocument { Id = "ORD-1" });
            await seed.SaveChangesAsync();
        }

        database.Store.Options.Listeners.Add(new QueueingListener(database.OutboxTable, "MSG-1"));

        await using (var session = database.Store.LightweightSession())
        {
            // An insert of a document that exists fails inside the batch.
            session.Insert(new OrderDocument { Id = "ORD-1" });

            var save = () => session.SaveChangesAsync();

            await save.Should().ThrowAsync<DocumentAlreadyExistsException>();
        }

        (await database.ReadOutboxIdsAsync()).Should().BeEmpty("the listener's row was in the batch whose transaction failed");
    }

    /// <summary>A document the tests store.</summary>
    public sealed class OrderDocument
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;
    }

    private sealed class CountingListener : DocumentSessionListenerBase
    {
        public int BeforeSaveCalls { get; private set; }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            this.BeforeSaveCalls++;

            return Task.CompletedTask;
        }
    }

    /// <summary>Writes one outbox row during the save, as a consumer's drain listener would.</summary>
    private sealed class QueueingListener(string outboxTable, string id) : DocumentSessionListenerBase
    {
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            session.QueueSqlCommand($"insert into {outboxTable} (id, body) values (?, ?)", id, "drained");

            return Task.CompletedTask;
        }
    }
}
