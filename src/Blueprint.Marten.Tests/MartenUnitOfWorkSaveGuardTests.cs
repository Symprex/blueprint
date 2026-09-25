using FluentAssertions;
using Marten;
using Weasel.Core;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that <see cref="MartenUnitOfWork.ThrowIfChangedSinceSave"/> detects work queued on a real Marten session after
/// the unit of work saved it, including SQL queued with <see cref="IDocumentOperations.QueueSqlCommand"/>, which no Marten
/// hook sees when it is called.
/// </summary>
/// <remarks>
/// <para>
/// Detectable because Marten 6.4.2's <c>QueueSqlCommand</c> adds its operation to the session's work tracker
/// (<c>DocumentSessionBase.cs:207-227</c>, as decompiled), whose operations <see cref="IDocumentSession.PendingChanges"/>
/// exposes (<c>UnitOfWork.Operations()</c>, <c>:241-244</c>), and a successful save resets them (<c>:604</c>).
/// </para>
/// <para>
/// No database is needed: the sessions have nothing to save when they are saved, so Marten never opens a connection.
/// </para>
/// </remarks>
public class MartenUnitOfWorkSaveGuardTests
{
    [Fact]
    public async Task Sql_queued_on_the_session_after_the_save_throws()
    {
        using var store = CreateUnreachableStore();
        await using var session = store.LightweightSession();
        var sut = new MartenUnitOfWork(session);
        await sut.SaveChangesAsync();

        session.QueueSqlCommand("insert into outbox (id) values (?)", "MSG-1");

        sut.Invoking(static s => s.ThrowIfChangedSinceSave())
            .Should().Throw<InvalidOperationException>().WithMessage("*after it was saved*");
    }

    [Fact]
    public async Task A_document_stored_on_the_session_after_the_save_throws()
    {
        using var store = CreateUnreachableStore();
        await using var session = store.LightweightSession();
        var sut = new MartenUnitOfWork(session);
        await sut.SaveChangesAsync();

        session.Store(new GuardDocument { Id = "DOC-1" });

        sut.Invoking(static s => s.ThrowIfChangedSinceSave()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Nothing_queued_after_the_save_does_not_throw()
    {
        using var store = CreateUnreachableStore();
        await using var session = store.LightweightSession();
        var sut = new MartenUnitOfWork(session);
        await sut.SaveChangesAsync();

        sut.Invoking(static s => s.ThrowIfChangedSinceSave()).Should().NotThrow();
    }

    [Fact]
    public async Task Work_on_a_session_that_was_never_saved_does_not_throw()
    {
        // A pipeline that failed before its save never completes its Rebus transaction; nothing was lost to a save.
        using var store = CreateUnreachableStore();
        await using var session = store.LightweightSession();
        var sut = new MartenUnitOfWork(session);

        session.QueueSqlCommand("insert into outbox (id) values (?)", "MSG-1");

        sut.Invoking(static s => s.ThrowIfChangedSinceSave()).Should().NotThrow();
    }

    private static DocumentStore CreateUnreachableStore()
    {
        var options = new StoreOptions();
        options.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1");
        options.AutoCreateSchemaObjects = AutoCreate.None;

        return new DocumentStore(options);
    }

    /// <summary>A document stored after the save.</summary>
    public sealed class GuardDocument
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;
    }
}
