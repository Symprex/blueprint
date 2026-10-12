using FluentAssertions;
using NSubstitute;
using Rebus.Pipeline;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="IncomingMessageContext"/> holds the message being handled only in the flow handling it, and only
/// until handling it ends, including for work started from that flow that carries on afterwards.
/// </summary>
public sealed class IncomingMessageContextTests
{
    private readonly IMessageContext _incoming = Substitute.For<IMessageContext>();

    [Fact]
    public void When_a_message_is_being_handled_then_it_is_current_until_its_scope_is_disposed()
    {
        // Act
        var handling = IncomingMessageContext.Begin(this._incoming);
        var during = IncomingMessageContext.Current;
        handling.Dispose();

        // Assert
        during.Should().BeSameAs(this._incoming);
        IncomingMessageContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task When_a_message_is_handled_in_an_async_method_then_its_caller_never_sees_it()
    {
        // Arrange
        IMessageContext? inside = null;

        async Task HandleAsync()
        {
            // Not disposed, so that only the flow the value was set in keeps it from the caller
            _ = IncomingMessageContext.Begin(this._incoming);

            await Task.Yield();

            inside = IncomingMessageContext.Current;
        }

        // Act
        await HandleAsync();

        // Assert
        inside.Should().BeSameAs(this._incoming);
        IncomingMessageContext.Current.Should().BeNull("the activator calls it from its handler, so the Rebus step that called that must not see it");
    }

    [Fact]
    public async Task When_work_started_while_handling_a_message_carries_on_after_handling_ends_then_the_message_is_no_longer_current_there()
    {
        // Arrange
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<IMessageContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IMessageContext?> lingeringWork;

        using (IncomingMessageContext.Begin(this._incoming))
        {
            // Deliberately not awaited while the message is being handled
            lingeringWork = Task.Run(async () =>
            {
                started.SetResult(IncomingMessageContext.Current);

                await handled.Task;

                return IncomingMessageContext.Current;
            });

            await started.Task;
        }

        // Act
        handled.SetResult();
        var afterHandling = await lingeringWork;

        // Assert
        (await started.Task).Should().BeSameAs(this._incoming, "the work was started while the message was being handled");
        afterHandling.Should().BeNull("its transaction has completed, so nothing may act on it any more");
    }

    [Fact]
    public async Task When_two_messages_are_handled_at_once_then_each_flow_has_its_own()
    {
        // Arrange
        var other = Substitute.For<IMessageContext>();
        var bothBegun = new Barrier(2);

        Task<IMessageContext?> Handle(IMessageContext context)
        {
            return Task.Run(() =>
            {
                using (IncomingMessageContext.Begin(context))
                {
                    // Both have begun before either reads, so one shared slot would give both the same
                    bothBegun.SignalAndWait(TimeSpan.FromSeconds(10)).Should().BeTrue();

                    return IncomingMessageContext.Current;
                }
            });
        }

        // Act
        var results = await Task.WhenAll(Handle(this._incoming), Handle(other));

        // Assert
        results.Should().Equal(this._incoming, other);
    }

    [Fact]
    public void When_beginning_with_no_context_then_it_throws()
    {
        // Act
        var act = static () => IncomingMessageContext.Begin(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
