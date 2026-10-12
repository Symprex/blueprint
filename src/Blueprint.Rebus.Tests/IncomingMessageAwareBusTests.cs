using FluentAssertions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves which transaction <see cref="IncomingMessageAwareBus"/> makes each call in: deferring and forwarding the message
/// being handled in that message's own, everything else in whatever is ambient, which inside a handler is the pipeline's.
/// </summary>
public sealed class IncomingMessageAwareBusTests : IDisposable
{
    private static readonly object _message = new();

    private static readonly Dictionary<string, string> _headers = new() { ["x-test"] = "value" };

    private static readonly Dictionary<string, Func<IBus, Task>> _calls = new()
    {
        [nameof(IBus.SendLocal)] = static b => b.SendLocal(_message, _headers),
        [nameof(IBus.Send)] = static b => b.Send(_message, _headers),
        [nameof(IBus.DeferLocal)] = static b => b.DeferLocal(TimeSpan.FromMinutes(3), _message, _headers),
        [nameof(IBus.Defer)] = static b => b.Defer(TimeSpan.FromMinutes(3), _message, _headers),
        [nameof(IBus.Reply)] = static b => b.Reply(_message, _headers),
        [nameof(IBus.Subscribe) + "<T>"] = static b => b.Subscribe<SubscribedEvent>(),
        [nameof(IBus.Subscribe)] = static b => b.Subscribe(typeof(SubscribedEvent)),
        [nameof(IBus.Unsubscribe) + "<T>"] = static b => b.Unsubscribe<SubscribedEvent>(),
        [nameof(IBus.Unsubscribe)] = static b => b.Unsubscribe(typeof(SubscribedEvent)),
        [nameof(IBus.Publish)] = static b => b.Publish(_message, _headers),
    };

    private readonly IBus _inner = Substitute.For<IBus>();
    private readonly ITransactionContext _pipelineTransaction = Substitute.For<ITransactionContext>();
    private readonly IMessageContext _incoming = Substitute.For<IMessageContext>();

    private ITransactionContext? _ambientWhenCalled;
    private IDisposable? _handling;

    public IncomingMessageAwareBusTests()
    {
        this._incoming.TransactionContext.Returns(Substitute.For<ITransactionContext>());

        // Whatever is called on the wrapped bus records the transaction context that was ambient when it was
        this._inner.WhenForAnyArgs(static b => b.Publish(default!, default)).Do(_ => this.RecordAmbient());
        this._inner.WhenForAnyArgs(static b => b.Send(default!, default)).Do(_ => this.RecordAmbient());
        this._inner.Advanced.TransportMessage.WhenForAnyArgs(static t => t.Defer(default, default)).Do(_ => this.RecordAmbient());
        this._inner.Advanced.TransportMessage.WhenForAnyArgs(static t => t.Forward(default!, default)).Do(_ => this.RecordAmbient());
    }

    public void Dispose()
    {
        this._handling?.Dispose();
        AmbientTransactionContext.SetCurrent(null);
    }

    [Fact]
    public async Task When_deferring_while_handling_a_message_then_it_is_deferred_in_the_transaction_of_that_message_not_the_pipeline()
    {
        // Arrange
        this.HandlingMessageInPipeline();

        // Act
        await new IncomingMessageAwareBus(this._inner).Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5), _headers);

        // Assert
        await this._inner.Advanced.TransportMessage.Received(1).Defer(TimeSpan.FromMinutes(5), _headers);

        this._ambientWhenCalled.Should().BeSameAs(this._incoming.TransactionContext);
        AmbientTransactionContext.Current.Should().BeSameAs(this._pipelineTransaction, "the pipeline's transaction is ambient again afterwards");
    }

    [Fact]
    public async Task When_forwarding_while_handling_a_message_then_it_is_forwarded_in_the_transaction_of_that_message_not_the_pipeline()
    {
        // Arrange
        this.HandlingMessageInPipeline();

        // Act
        await new IncomingMessageAwareBus(this._inner).Advanced.TransportMessage.Forward("elsewhere", _headers);

        // Assert
        await this._inner.Advanced.TransportMessage.Received(1).Forward("elsewhere", _headers);

        this._ambientWhenCalled.Should().BeSameAs(this._incoming.TransactionContext);
        AmbientTransactionContext.Current.Should().BeSameAs(this._pipelineTransaction);
    }

    [Fact]
    public async Task When_deferring_without_handling_a_message_then_the_ambient_transaction_is_left_alone()
    {
        // Arrange: an HTTP request, say, or a plain handler, where Rebus' own handling applies
        AmbientTransactionContext.SetCurrent(this._pipelineTransaction);

        // Act
        await new IncomingMessageAwareBus(this._inner).Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));

        // Assert
        this._ambientWhenCalled.Should().BeSameAs(this._pipelineTransaction);
    }

    [Fact]
    public async Task When_work_started_while_handling_a_message_defers_after_the_message_has_been_handled_then_the_ambient_transaction_is_left_alone()
    {
        // Arrange
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task lingeringWork;

        using (IncomingMessageContext.Begin(this._incoming))
        {
            AmbientTransactionContext.SetCurrent(this._pipelineTransaction);

            // Deliberately not awaited while the message is being handled
            lingeringWork = Task.Run(async () =>
            {
                await handled.Task;
                await new IncomingMessageAwareBus(this._inner).Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));
            });
        }

        // Act
        handled.SetResult();
        await lingeringWork;

        // Assert: the message's own transaction has completed by now, so it must not be joined
        this._ambientWhenCalled.Should().BeSameAs(this._pipelineTransaction);
    }

    [Fact]
    public async Task When_deferring_fails_then_the_failure_propagates_and_the_pipeline_transaction_is_ambient_again()
    {
        // Arrange
        this.HandlingMessageInPipeline();

        this._inner.Advanced.TransportMessage
            .Defer(Arg.Any<TimeSpan>(), Arg.Any<Dictionary<string, string>>())
            .Returns<Task>(static _ => throw new InvalidOperationException("boom"));

        // Act
        var act = () => new IncomingMessageAwareBus(this._inner).Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(5));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        AmbientTransactionContext.Current.Should().BeSameAs(this._pipelineTransaction);
    }

    [Fact]
    public async Task When_publishing_while_handling_a_message_then_it_is_published_in_the_pipeline_transaction()
    {
        // Arrange
        this.HandlingMessageInPipeline();

        // Act
        await new IncomingMessageAwareBus(this._inner).Publish(_message, _headers);

        // Assert: the pipeline's transaction is the one an outbox is enlisted in, and is saved with its unit of work
        this._ambientWhenCalled.Should().BeSameAs(this._pipelineTransaction);
    }

    [Fact]
    public async Task When_sending_while_handling_a_message_then_it_is_sent_in_the_pipeline_transaction()
    {
        // Arrange
        this.HandlingMessageInPipeline();

        // Act
        await new IncomingMessageAwareBus(this._inner).Send(_message, _headers);

        // Assert
        this._ambientWhenCalled.Should().BeSameAs(this._pipelineTransaction);
    }

    [Theory]
    [InlineData(nameof(IBus.SendLocal))]
    [InlineData(nameof(IBus.Send))]
    [InlineData(nameof(IBus.DeferLocal))]
    [InlineData(nameof(IBus.Defer))]
    [InlineData(nameof(IBus.Reply))]
    [InlineData(nameof(IBus.Subscribe) + "<T>")]
    [InlineData(nameof(IBus.Subscribe))]
    [InlineData(nameof(IBus.Unsubscribe) + "<T>")]
    [InlineData(nameof(IBus.Unsubscribe))]
    [InlineData(nameof(IBus.Publish))]
    public async Task When_calling_the_bus_then_exactly_that_call_is_made_on_the_wrapped_bus(string call)
    {
        // Arrange
        this.HandlingMessageInPipeline();

        // Act
        await _calls[call](new IncomingMessageAwareBus(this._inner));

        // Assert
        _ = _calls[call](this._inner.Received(1));
    }

    [Fact]
    public void When_getting_the_rest_of_the_advanced_api_then_it_is_that_of_the_wrapped_bus()
    {
        // Act
        var advanced = new IncomingMessageAwareBus(this._inner).Advanced;

        // Assert
        advanced.Workers.Should().BeSameAs(this._inner.Advanced.Workers);
        advanced.Topics.Should().BeSameAs(this._inner.Advanced.Topics);
        advanced.Routing.Should().BeSameAs(this._inner.Advanced.Routing);
        advanced.DataBus.Should().BeSameAs(this._inner.Advanced.DataBus);
        advanced.SyncBus.Should().BeSameAs(this._inner.Advanced.SyncBus);
    }

    [Fact]
    public void When_disposed_then_the_wrapped_bus_is_disposed_which_stops_its_workers()
    {
        // Act
        new IncomingMessageAwareBus(this._inner).Dispose();

        // Assert
        this._inner.Received(1).Dispose();
    }

    private void HandlingMessageInPipeline()
    {
        // As inside a handler: the activator has captured the message being handled, then the pipeline has opened its own scope
        this._handling = IncomingMessageContext.Begin(this._incoming);
        AmbientTransactionContext.SetCurrent(this._pipelineTransaction);
    }

    private void RecordAmbient()
    {
        this._ambientWhenCalled = AmbientTransactionContext.Current;
    }

    private sealed class SubscribedEvent;
}
