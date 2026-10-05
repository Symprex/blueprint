using FluentAssertions;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Pins, for Rebus 8.6's <see cref="RebusTransactionScope"/>, the callback order that the check for work queued on a unit
/// of work after its save depends on.
/// </summary>
/// <remarks>
/// <para>
/// Rebus's <c>TransactionContext.Complete</c> (8.6.0, as decompiled) takes the commit callbacks registered so far and
/// runs them (<c>:111-112</c>, invoking the list it took, <c>:209-218</c>), and only then takes and runs the ack
/// callbacks (<c>:133-137</c>). So an ack callback registered before a handler runs still runs after every commit callback
/// the handler caused, which is where a check must run to see what a commit callback queued; and a commit callback
/// registered while the commit callbacks run is not run by that completion, so a check registered as one would never
/// run.
/// </para>
/// <para>
/// A received message's worker commits in two rounds, so there a commit callback registered during the first is run by
/// the second (<see cref="RebusCommitAmbientContextTests"/>). That is not pinned here.
/// </para>
/// </remarks>
public class RebusTransactionCallbackOrderTests
{
    [Fact]
    public async Task An_ack_callback_registered_before_the_commit_callbacks_runs_after_all_of_them()
    {
        var log = new List<string>();
        using var scope = new RebusTransactionScope();
        var context = scope.TransactionContext;

        context.OnAck(_ => Log(log, "ack, registered first"));
        context.OnCommit(_ => Log(log, "commit, registered second"));
        context.OnCommit(_ => Log(log, "commit, registered third"));

        await scope.CompleteAsync();

        log.Should().Equal("commit, registered second", "commit, registered third", "ack, registered first");
    }

    [Fact]
    public async Task A_commit_callback_registered_while_the_commit_callbacks_run_is_not_run_but_an_ack_callback_is()
    {
        var log = new List<string>();
        using var scope = new RebusTransactionScope();
        var context = scope.TransactionContext;

        context.OnCommit(_ =>
        {
            context.OnCommit(_ => Log(log, "commit, registered during commit"));
            context.OnAck(_ => Log(log, "ack, registered during commit"));

            return Log(log, "commit");
        });

        await scope.CompleteAsync();

        log.Should().Equal("commit", "ack, registered during commit");
    }

    private static Task Log(List<string> log, string entry)
    {
        log.Add(entry);

        return Task.CompletedTask;
    }
}
