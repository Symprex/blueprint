using FluentAssertions;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves the lifetime of the transaction <see cref="IncomingMessageCopyTransaction"/> sends a plain handler's copy of its message
/// in: one of its own, carrying the message, committed after what the received message's transaction committed before it, never
/// committed when that transaction is not, and not handed out once that transaction has started committing.
/// </summary>
public sealed class IncomingMessageCopyTransactionTests : IDisposable
{
    private readonly RebusTransactionScope _received = new();
    private readonly IncomingStepContext _stepContext;

    public IncomingMessageCopyTransactionTests()
    {
        this._stepContext = new IncomingStepContext(new TransportMessage(new Dictionary<string, string>(), []), this._received.TransactionContext);
        this._received.TransactionContext.Items[StepContext.StepContextKey] = this._stepContext;
    }

    public void Dispose()
    {
        this._received.Dispose();
        AmbientTransactionContext.SetCurrent(null);
    }

    [Fact]
    public void When_none_is_attached_to_the_transaction_then_none_is_found()
    {
        // Act / Assert: a plain handler whose transaction no outbox is enlisted in, an HTTP request, a background job
        IncomingMessageCopyTransaction.Find(this._received.TransactionContext).Should().BeNull();
        IncomingMessageCopyTransaction.Find(null).Should().BeNull();
    }

    [Fact]
    public void When_attached_then_the_copy_is_made_in_a_transaction_of_its_own_that_carries_the_message()
    {
        // Arrange
        IncomingMessageCopyTransaction.Attach(this._received.TransactionContext);
        var ambient = AmbientTransactionContext.Current;

        // Act
        var copy = IncomingMessageCopyTransaction.Find(this._received.TransactionContext);

        // Assert
        copy.Should().NotBeNull().And.NotBeSameAs(this._received.TransactionContext, "the received message's transaction is the one an outbox is enlisted in");
        copy!.Items[StepContext.StepContextKey].Should().BeSameAs(this._stepContext, "it is where Rebus finds the message to copy");
        IncomingMessageCopyTransaction.Find(this._received.TransactionContext).Should().BeSameAs(copy, "every copy of the message goes in the one transaction");
        AmbientTransactionContext.Current.Should().BeSameAs(ambient, "beginning it leaves the ambient transaction alone");
    }

    [Fact]
    public async Task When_the_received_transaction_commits_then_the_copy_is_committed_after_what_was_registered_before_it_was_attached()
    {
        // Arrange: as the activator registers the save of the unit of work, then attaches
        var committed = new List<string>();
        this._received.TransactionContext.OnCommit(_ => Record(committed, "save"));
        IncomingMessageCopyTransaction.Attach(this._received.TransactionContext);

        // As the transport does when a copy is sent into it
        IncomingMessageCopyTransaction.Find(this._received.TransactionContext)!.OnCommit(_ => Record(committed, "copy"));

        // Act
        await this._received.CompleteAsync();

        // Assert
        committed.Should().Equal(["save", "copy"]);
    }

    [Fact]
    public async Task When_the_save_before_it_fails_then_the_copy_is_not_committed()
    {
        // Arrange
        var committed = new List<string>();
        this._received.TransactionContext.OnCommit(static _ => throw new InvalidOperationException("The database is down."));
        IncomingMessageCopyTransaction.Attach(this._received.TransactionContext);
        IncomingMessageCopyTransaction.Find(this._received.TransactionContext)!.OnCommit(_ => Record(committed, "copy"));

        // Act
        var complete = () => this._received.CompleteAsync();

        // Assert
        await complete.Should().ThrowAsync<InvalidOperationException>();
        committed.Should().BeEmpty();
    }

    [Fact]
    public void When_the_received_transaction_is_disposed_without_committing_then_the_copy_is_disposed_unsent()
    {
        // Arrange: as when a handler throws
        var committed = new List<string>();
        var disposed = false;
        IncomingMessageCopyTransaction.Attach(this._received.TransactionContext);

        var copy = IncomingMessageCopyTransaction.Find(this._received.TransactionContext)!;
        copy.OnCommit(_ => Record(committed, "copy"));
        copy.OnDisposed(_ => disposed = true);

        var ambient = new RebusTransactionScope().TransactionContext;

        // Act
        this._received.TransactionContext.Dispose();

        // Assert
        committed.Should().BeEmpty();
        disposed.Should().BeTrue();
        AmbientTransactionContext.Current.Should().BeSameAs(ambient, "disposing it leaves the ambient transaction of whoever disposes alone");
    }

    [Fact]
    public async Task When_the_received_transaction_has_committed_then_no_copy_transaction_is_found()
    {
        // Arrange: work a handler started and did not wait for, which calls once the message has been handled
        IncomingMessageCopyTransaction.Attach(this._received.TransactionContext);
        await this._received.CompleteAsync();

        // Act / Assert: left to Rebus, as it would be without one
        IncomingMessageCopyTransaction.Find(this._received.TransactionContext).Should().BeNull();
    }

    private static Task Record(List<string> committed, string what)
    {
        committed.Add(what);

        return Task.CompletedTask;
    }
}
