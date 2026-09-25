using Blueprint.Marten.Tests.Postgres;
using FluentAssertions;
using Marten;
using Weasel.Core;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that a drain listener added with <see cref="MartenServiceCollectionExtensions.AddBlueprintOutboxListener"/>
/// runs whenever <see cref="MartenUnitOfWork"/> saves, including a save with nothing else to write, and that the
/// guarantee costs nothing where no such listener is added.
/// </summary>
/// <remarks>
/// <para>
/// Marten 6.4.2 runs no listener for a session with nothing to save (<c>DocumentSessionBase.SaveChangesAsync</c>
/// <c>:569-572</c> before <c>:591-594</c>, pinned by <see cref="MartenSaveListenerPostgresTests"/>). An operation that sends
/// a message but writes nothing therefore saves a session with nothing to save, and a drain listener would never write
/// its message. Legacy did not have this gap: its Rebus commit queued the outbox rows on the session before the save
/// (Signature365 <c>8b40ecc961</c>, <c>AzureAdOauthReturnCommandExecutorPipeline.cs:133-137</c>), so the session had work.
/// </para>
/// <para>
/// The Postgres facts are skipped where no database is configured, as in CI; the one without a database runs everywhere.
/// </para>
/// </remarks>
public class MartenUnitOfWorkOutboxDrainTests
{
    [PostgresFact]
    public async Task A_drain_added_with_AddBlueprintOutboxListener_writes_when_the_unit_of_work_saves_a_session_with_nothing_else_to_save()
    {
        await using var database = await PostgresTestStore.CreateAsync();
        database.Store.Options.AddBlueprintOutboxListener(new DrainListener(database.OutboxTable, "MSG-1"));

        await using (var session = database.Store.LightweightSession())
        {
            // A send-only operation: nothing stored, so only the drain has anything to write.
            await new MartenUnitOfWork(session).SaveChangesAsync();
        }

        (await database.ReadOutboxIdsAsync()).Should().Equal("MSG-1");
    }

    [PostgresFact]
    public async Task A_drain_that_writes_a_versioned_document_as_well_as_sql_on_an_otherwise_empty_save_commits_both()
    {
        // The save the guarantee forces holds its own statement besides the drain's writes. A versioned document's upsert
        // reads its result back, so this fails if that statement returns a result the batch does not expect.
        await using var database = await PostgresTestStore.CreateAsync(
            static options => options.Schema.For<VersionedDocument>().UseOptimisticConcurrency(true));

        // Marten creates a document's storage before it runs the listeners (DocumentSessionBase.SaveChangesAsync :584-590),
        // so a type first stored by a listener would find none; a deployed app has its schema in place already.
        await database.Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        database.Store.Options.AddBlueprintOutboxListener(
            new DrainListener(database.OutboxTable, "MSG-1", new VersionedDocument { Id = "DOC-1" }));

        await using (var session = database.Store.LightweightSession())
        {
            await new MartenUnitOfWork(session).SaveChangesAsync();
        }

        (await database.ReadOutboxIdsAsync()).Should().Equal("MSG-1");
        await using var query = database.Store.QuerySession();
        (await query.LoadAsync<VersionedDocument>("DOC-1")).Should().NotBeNull();
    }

    [PostgresFact]
    public async Task A_drain_that_says_it_has_messages_writes_when_the_unit_of_work_saves_a_session_with_nothing_else_to_save()
    {
        await using var database = await PostgresTestStore.CreateAsync();
        database.Store.Options.AddBlueprintOutboxListener(new PendingAwareDrainListener(database.OutboxTable, "MSG-1", hasPendingMessages: true));

        await using (var session = database.Store.LightweightSession())
        {
            await new MartenUnitOfWork(session).SaveChangesAsync();
        }

        (await database.ReadOutboxIdsAsync()).Should().Equal("MSG-1");
    }

    [PostgresFact]
    public async Task A_drain_that_says_it_has_no_messages_is_not_run_for_a_session_with_nothing_else_to_save()
    {
        // Legacy's session had work exactly when a message had been sent, because the Rebus commit queued its outbox rows
        // on the session before the save (Signature365 8b40ecc961, AzureAdOauthReturnCommandExecutorPipeline.cs:133-137);
        // otherwise its save had nothing to do and Marten returned before running any listener (DocumentSessionBase
        // .SaveChangesAsync :569-572, pinned by MartenSaveListenerPostgresTests).
        await using var database = await PostgresTestStore.CreateAsync();
        var drain = new PendingAwareDrainListener(database.OutboxTable, "MSG-1", hasPendingMessages: false);
        database.Store.Options.AddBlueprintOutboxListener(drain);

        await using (var session = database.Store.LightweightSession())
        {
            await new MartenUnitOfWork(session).SaveChangesAsync();
        }

        drain.Ran.Should().BeFalse("nothing was sent and nothing stored, so legacy's save did nothing");
        (await database.ReadOutboxIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_session_with_nothing_to_save_whose_drain_has_no_messages_is_saved_without_touching_the_database()
    {
        // A handler that writes nothing and sends nothing needed no database under legacy, so it must not fail, and be
        // retried or dead-lettered, while PostgreSQL is down. Nothing listens on this port, so any statement would fail.
        var options = UnreachableOptions();
        options.AddBlueprintOutboxListener(new PendingAwareDrainListener("unused", "MSG-1", hasPendingMessages: false));
        using var store = new DocumentStore(options);
        await using var session = store.LightweightSession();

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        await save.Should().NotThrowAsync("a drain with nothing to write needs no connection");
    }

    [Fact]
    public async Task A_session_with_nothing_to_save_whose_drain_cannot_say_whether_it_has_messages_still_runs_it()
    {
        // A drain that is not an IOutboxDrainListener cannot say, so its save still opens a connection to run it rather
        // than risk losing what it would write: against an unreachable database, that is a failed save.
        var options = UnreachableOptions();
        options.AddBlueprintOutboxListener(new DrainListener("unused", "MSG-1"));
        using var store = new DocumentStore(options);
        await using var session = store.LightweightSession();

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        await save.Should().ThrowAsync<Exception>("the drain is run, which needs the database");
    }

    [Fact]
    public async Task A_session_with_nothing_to_save_and_no_outbox_listener_is_saved_without_touching_the_database()
    {
        // Nothing listens on this port, so any statement the save sent would fail.
        using var store = new DocumentStore(UnreachableOptions());
        await using var session = store.LightweightSession();

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        await save.Should().NotThrowAsync("a save with nothing to do, and no drain to run, must not open a connection");
    }

    private static StoreOptions UnreachableOptions()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
        options.AutoCreateSchemaObjects = AutoCreate.None;

        return options;
    }

    /// <summary>A document with optimistic concurrency, whose upsert returns the version it wrote.</summary>
    public sealed class VersionedDocument
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>
    /// Stands in for a consumer's drain: during the save, writes one outbox row, and optionally a document, into the session.
    /// </summary>
    private sealed class DrainListener(string outboxTable, string id, VersionedDocument? document = null) : DocumentSessionListenerBase
    {
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            if (document is not null)
            {
                session.Store(document);
            }

            session.QueueSqlCommand($"insert into {outboxTable} (id, body) values (?, ?)", id, "drained");

            return Task.CompletedTask;
        }
    }

    /// <summary>A consumer's drain that says whether it has messages to write, and writes one outbox row when it runs.</summary>
    private sealed class PendingAwareDrainListener(string outboxTable, string id, bool hasPendingMessages) : DocumentSessionListenerBase, IOutboxDrainListener
    {
        public bool Ran { get; private set; }

        public bool HasPendingMessages(IDocumentSession session)
        {
            return hasPendingMessages;
        }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            this.Ran = true;

            if (hasPendingMessages)
            {
                session.QueueSqlCommand($"insert into {outboxTable} (id, body) values (?, ?)", id, "drained");
            }

            return Task.CompletedTask;
        }
    }
}
