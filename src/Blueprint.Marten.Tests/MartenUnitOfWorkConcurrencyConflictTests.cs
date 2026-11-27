using Blueprint.Persistence;
using FluentAssertions;
using Marten;
using Marten.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Issue #197: a concurrency conflict in a Marten save, because another request wrote first, used to escape the unit of
/// work as Marten's own exception, which the pipeline answered with a 500. <see cref="MartenUnitOfWork"/> raises a
/// <see cref="ConcurrencyConflictException"/> in its place, keeping Marten's as the cause.
/// </summary>
public class MartenUnitOfWorkConcurrencyConflictTests
{
    public static TheoryData<Exception> ConcurrencyConflicts => new()
    {
        new ConcurrencyException(typeof(Document), "DOC-1"),
        new DocumentAlreadyExistsException(new InvalidOperationException("duplicate key"), typeof(Document), "DOC-1"),

        // An event stream appended to with an expected version another request has moved past.
        new EventStreamUnexpectedMaxEventIdException("DOC-1", typeof(Document), 1, 2),
    };

    [Theory]
    [MemberData(nameof(ConcurrencyConflicts))]
    public async Task A_Marten_concurrency_failure_is_raised_as_a_ConcurrencyConflictException(Exception conflict)
    {
        var session = Substitute.For<IDocumentSession>();
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflict);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        var raised = (await save.Should().ThrowExactlyAsync<ConcurrencyConflictException>()).Which;
        raised.InnerException.Should().BeSameAs(conflict);

        // The problem's detail: what the caller can do, as PR #198's review asked, not what the store saw.
        raised.Message.Should().Be("The resource has been modified since it was retrieved. Refresh the resource and retry the operation.");
    }

    [Fact]
    public async Task Several_conflicts_in_one_batch_are_raised_as_one_ConcurrencyConflictException()
    {
        // Marten throws an AggregateException when more than one operation of the batch fails with a concurrency conflict.
        var conflicts = new AggregateException(
            new ConcurrencyException(typeof(Document), "DOC-1"),
            new ConcurrencyException(typeof(Document), "DOC-2"));
        var session = Substitute.For<IDocumentSession>();
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(conflicts);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowExactlyAsync<ConcurrencyConflictException>())
            .Which.InnerException.Should().BeSameAs(conflicts);
    }

    [Fact]
    public async Task Any_other_failure_of_the_save_escapes_unchanged()
    {
        var failure = new InvalidOperationException("The database is down.");
        var session = Substitute.For<IDocumentSession>();
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task A_batch_holding_a_conflict_and_another_failure_escapes_unchanged()
    {
        // Not every failure in it is a clash another request caused, so it is not answered as one.
        var failures = new AggregateException(
            new ConcurrencyException(typeof(Document), "DOC-1"),
            new InvalidOperationException("The database is down."));
        var session = Substitute.For<IDocumentSession>();
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(failures);

        var save = () => new MartenUnitOfWork(session).SaveChangesAsync();

        (await save.Should().ThrowAsync<AggregateException>()).Which.Should().BeSameAs(failures);
    }

    /// <summary>A document type the conflicts name.</summary>
    public sealed class Document
    {
        /// <summary>Gets or sets the id.</summary>
        public string Id { get; set; } = string.Empty;
    }
}
