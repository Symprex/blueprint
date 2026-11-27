using Blueprint.Marten.Tests.Postgres;
using Blueprint.Persistence;
using FluentAssertions;
using Marten.Exceptions;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Pins, against a real PostgreSQL, which exceptions Marten 6.4.2 raises when another request wrote first, and that
/// <see cref="MartenUnitOfWork"/> raises each as a <see cref="ConcurrencyConflictException"/> (issue #197).
/// </summary>
public class MartenUnitOfWorkConcurrencyConflictPostgresTests
{
    [PostgresFact]
    public async Task An_insert_of_a_document_another_request_inserted_first_is_a_concurrency_conflict()
    {
        await using var database = await PostgresTestStore.CreateAsync();
        await StoreAsync(database, new ConflictClaim { Id = "CLAIM-1" });

        await using var session = database.Store.LightweightSession();
        session.Insert(new ConflictClaim { Id = "CLAIM-1" });

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowExactlyAsync<ConcurrencyConflictException>())
            .Which.InnerException.Should().BeOfType<DocumentAlreadyExistsException>();
    }

    [PostgresFact]
    public async Task A_document_changed_by_another_request_since_it_was_read_is_a_concurrency_conflict()
    {
        await using var database = await PostgresTestStore.CreateAsync(static options => options.Schema.For<ConflictVersion>().UseOptimisticConcurrency(true));
        await StoreAsync(database, new ConflictVersion { Id = "DOC-1", Name = "original" });

        await using var session = database.Store.LightweightSession();
        var read = await session.LoadAsync<ConflictVersion>("DOC-1");

        await using (var other = database.Store.LightweightSession())
        {
            var winner = await other.LoadAsync<ConflictVersion>("DOC-1");
            winner!.Name = "winner";
            other.Store(winner);
            await other.SaveChangesAsync();
        }

        read!.Name = "loser";
        session.Store(read);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowExactlyAsync<ConcurrencyConflictException>())
            .Which.InnerException.Should().BeOfType<ConcurrencyException>();
    }

    [PostgresFact]
    public async Task Two_documents_changed_by_another_request_are_one_concurrency_conflict()
    {
        await using var database = await PostgresTestStore.CreateAsync(static options => options.Schema.For<ConflictVersion>().UseOptimisticConcurrency(true));
        await StoreAsync(database, new ConflictVersion { Id = "DOC-1" }, new ConflictVersion { Id = "DOC-2" });

        await using var session = database.Store.LightweightSession();
        var first = await session.LoadAsync<ConflictVersion>("DOC-1");
        var second = await session.LoadAsync<ConflictVersion>("DOC-2");

        await using (var other = database.Store.LightweightSession())
        {
            other.Store((await other.LoadAsync<ConflictVersion>("DOC-1"))!, (await other.LoadAsync<ConflictVersion>("DOC-2"))!);
            await other.SaveChangesAsync();
        }

        session.Store(first!, second!);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowExactlyAsync<ConcurrencyConflictException>())
            .Which.InnerException.Should().BeOfType<AggregateException>()
            .Which.InnerExceptions.Should().HaveCount(2).And.AllBeOfType<ConcurrencyException>();
    }

    private static async Task StoreAsync<T>(PostgresTestStore database, params T[] documents)
        where T : notnull
    {
        await using var seed = database.Store.LightweightSession();
        seed.Store(documents);
        await seed.SaveChangesAsync();
    }
}

// Top level, with short names: Marten names a nested type's tables after its outer type too, and with this test class's
// name they ran past PostgreSQL's 63-character identifier limit.

/// <summary>A unique claim: a document inserted, never stored over.</summary>
public sealed class ConflictClaim
{
    /// <summary>Gets or sets the id.</summary>
    public string Id { get; set; } = string.Empty;
}

/// <summary>A document saved with optimistic concurrency.</summary>
public sealed class ConflictVersion
{
    /// <summary>Gets or sets the id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; set; }
}
