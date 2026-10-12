using System.Collections.Concurrent;
using Blueprint.Model;
using Blueprint.Rebus;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Retry.Simple;
using Rebus.Timeouts;
using Rebus.Transport.InMem;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the handler of an operation delivered as a Rebus message can act on the message it is handling — defer it,
/// forward it, read its headers through an injected <see cref="IMessageContext"/> — although the generated pipeline runs it
/// inside a <c>RebusTransactionScope</c> of its own, which hides that message from Rebus.
/// </summary>
/// <remarks>
/// <para>
/// Each test runs a real bus on the in-memory transport, through <c>AddBlueprintRebus</c>, the real
/// <see cref="BlueprintApiActivator"/> and the generated pipelines of <see cref="IntegrationTestBlueprintApp"/>. Rebus finds
/// the message being handled through the ambient transaction context, and inside a handler that is the pipeline's scope, so
/// <c>bus.Advanced.TransportMessage.Defer</c> and <c>Forward</c> threw a <see cref="KeyNotFoundException"/> for
/// <c>stepContext</c> there, and an injected <see cref="IMessageContext"/> could not be resolved.
/// </para>
/// <para>
/// One worker handles one message at a time unless a test says otherwise, and a failed delivery goes straight to the error
/// queue. The timeout manager is an in-memory one that records each deferral and hands it back at its next poll.
/// </para>
/// </remarks>
public sealed class RebusIncomingMessageIntegrationTests : IDisposable
{
    private const string InputQueue = "rebus-incoming-message-tests";
    private const string ForwardDestination = "rebus-incoming-message-tests-forwarded";
    private const string Subscriber = "rebus-incoming-message-tests-subscriber";
    private const string ErrorQueue = "error";

    private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(30);

    private readonly InMemNetwork _network = new();
    private readonly RecordingTimeoutManager _timeoutManager = new();
    private readonly RebusIncomingMessageProbe _probe = new();

    private ServiceProvider? _services;

    private IBus Bus => this._services!.GetRequiredService<IBus>();

    public void Dispose()
    {
        // Disposing the bus stops its workers
        this._services?.Dispose();
    }

    [Fact]
    public async Task When_a_handler_defers_the_message_it_is_handling_then_it_is_delivered_again_with_a_defer_count()
    {
        // Arrange
        this.StartBus();
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new DeferringRebusCommand(), WithMessageId(messageId));

        await this.WaitForAsync(() => this._probe.Deliveries >= 2);

        // Assert
        this._probe.Failures.Should().BeEmpty("deferring the message being handled must not fail");
        this._probe.Deliveries.Should().Be(2, "the message is handled, deferred, then handled again once it is due");

        var deferral = this._timeoutManager.Deferrals.Should().ContainSingle().Subject;
        deferral.Headers[Headers.MessageId].Should().Be(messageId, "it is the message being handled that is deferred");
        deferral.Headers[Headers.DeferCount].Should().Be("1");
    }

    [Fact]
    public async Task When_a_handler_takes_the_message_context_then_it_has_the_headers_of_the_message_being_handled_as_it_is_deferred()
    {
        // Arrange
        this.StartBus();

        // Act
        await this.Bus.SendLocal(new CountedDeferralRebusCommand());

        await this.WaitForAsync(() => this._probe.DeferCounts.Count >= 3);

        // Assert: never deferred, then deferred once, then twice
        this._probe.Failures.Should().BeEmpty();
        this._probe.DeferCounts.Should().Equal(null, "1", "2");
    }

    [Fact]
    public async Task When_a_handler_forwards_the_message_it_is_handling_then_it_arrives_at_the_destination_once_handled()
    {
        // Arrange
        this.StartBus();
        var messageId = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new ForwardingRebusCommand(), WithMessageId(messageId));

        var forwarded = await this.WaitForMessageAsync(ForwardDestination);

        // Assert
        this._probe.Failures.Should().BeEmpty("forwarding the message being handled must not fail");
        forwarded.Should().NotBeNull("the handler forwards the message it is handling");
        forwarded!.Headers[Headers.MessageId].Should().Be(messageId);
        this._network.GetCount(ErrorQueue).Should().Be(0);
    }

    [Fact]
    public async Task When_a_handler_defers_the_message_and_then_throws_then_no_deferred_copy_is_sent()
    {
        // Arrange: anything that was deferred is held, so that it is recorded once and never handled again
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus();

        // Act
        await this.Bus.SendLocal(new DeferThenThrowRebusCommand());

        await this.WaitForAsync(() => this._network.GetCount(ErrorQueue) == 1);
        await this.WaitPastMarkerAsync();

        // Assert
        this._probe.Failures.Should().BeEmpty("the deferral itself must succeed for this to prove anything");
        this._probe.DeferredBeforeThrowing.Should().BeTrue();
        this._network.GetCount(ErrorQueue).Should().Be(1, "the handler threw, so the message failed");
        this._timeoutManager.Deferrals.Should().BeEmpty(
            "the deferred copy goes out with the message's own transaction, which a handler that throws never commits");
    }

    [Fact]
    public async Task When_a_handler_publishes_then_the_published_message_has_correlation_headers_of_its_own()
    {
        // Arrange: a bus with no workers subscribes the subscriber queue, so what is published there stays there to be read
        this.StartBus();

        using var subscriber = Configure.With(new BuiltinHandlerActivator())
            .Transport(t => t.UseInMemoryTransport(this._network, Subscriber))
            .Options(static o => o.SetNumberOfWorkers(0))
            .Start();
        await subscriber.Subscribe<RebusIncomingMessagePublished>();

        var headers = new Dictionary<string, string>
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.CorrelationId] = "correlation-of-the-handled-message",
        };

        // Act
        await this.Bus.SendLocal(new PublishingRebusCommand(), headers);

        var published = await this.WaitForMessageAsync(Subscriber);

        // Assert: published in the pipeline's own transaction, in which Rebus finds no message being handled, so it is
        // correlated with itself rather than with the message being handled. Publishing in the transaction of the message
        // being handled, as a deferral is, would change these, and would bypass an outbox enlisted in the pipeline's scope
        published.Should().NotBeNull("the handler publishes a message");
        published!.Headers[Headers.CorrelationId].Should().Be(published.Headers[Headers.MessageId]);
        published.Headers[Headers.CorrelationSequence].Should().Be("0");
    }

    [Fact]
    public async Task When_work_a_handler_started_carries_on_after_the_message_has_been_handled_then_it_has_no_message_context()
    {
        // Arrange
        this.StartBus();

        // Act
        await this.Bus.SendLocal(new LingeringWorkRebusCommand());

        var (resolving, deferring) = await this._probe.AfterHandling.Task.WaitAsync(_waitLimit);

        // Assert: the message it was started for has been handled and its transaction completed, so neither may be used
        this._probe.Failures.Should().BeEmpty();
        resolving.Should().BeOfType<InvalidOperationException>("by then it is not running in a Rebus handler, so there is no message context to give it")
            .Which.Message.Should().StartWith("Attempted to resolve IMessageContext outside of a Rebus handler");

        deferring.Should().NotBeNull("there is no message being handled for it to defer");
        this._timeoutManager.Deferrals.Should().BeEmpty();
    }

    [Fact]
    public async Task When_two_messages_are_handled_at_once_then_each_handler_has_and_defers_its_own_message()
    {
        // Arrange: both handlers are inside the pipeline at the same time, behind a barrier, before either reads its context
        this._timeoutManager.MaxHandBacks = 0;
        this.StartBus(maxParallelism: 2);
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();

        // Act
        await this.Bus.SendLocal(new ConcurrentRebusCommand { Name = "first" }, WithMessageId(first));
        await this.Bus.SendLocal(new ConcurrentRebusCommand { Name = "second" }, WithMessageId(second));

        await this.WaitForAsync(() => this._timeoutManager.Deferrals.Count >= 2 || !this._probe.Failures.IsEmpty);

        // Assert
        this._probe.Failures.Should().BeEmpty();
        this._probe.HandledMessageIds.Should().BeEquivalentTo(new Dictionary<string, string> { ["first"] = first, ["second"] = second });
        this._timeoutManager.Deferrals.Select(static d => d.Headers[Headers.MessageId]).Should().BeEquivalentTo([first, second]);
    }

    [Fact]
    public async Task When_an_operation_without_a_rebus_transaction_defers_the_message_it_is_handling_then_it_is_delivered_again()
    {
        // Arrange: without a scope of the pipeline's own, the handler runs in the message's own transaction
        this.StartBus();

        // Act
        await this.Bus.SendLocal(new UnscopedDeferringRebusCommand());

        await this.WaitForAsync(() => this._probe.Deliveries >= 2);

        // Assert
        this._probe.Failures.Should().BeEmpty();
        this._probe.Deliveries.Should().Be(2);
        this._timeoutManager.Deferrals.Should().ContainSingle().Which.Headers[Headers.DeferCount].Should().Be("1");
    }

    private static Dictionary<string, string> WithMessageId(string messageId)
    {
        return new Dictionary<string, string> { [Headers.MessageId] = messageId };
    }

    private void StartBus(int maxParallelism = 1)
    {
        this._network.CreateQueue(ForwardDestination);

        var services = new ServiceCollection().AddLogging();

        services.AddSingleton(this._probe);

        // Resolves from the root provider, which, unlike the handler's own scope, is still there once the message has been
        // handled, and resolves when called rather than when the handler is
        services.AddSingleton<Func<IMessageContext>>(static s => () => s.GetRequiredService<IMessageContext>());

        services.AddBlueprintRebusConsumer(static _ => { });
        services.AddBlueprintRebus((_, configurer) => configurer
            .Transport(t => t.UseInMemoryTransport(this._network, InputQueue))
            .Timeouts(t => t.Register(_ => this._timeoutManager))
            .Options(o =>
            {
                o.RetryStrategy(maxDeliveryAttempts: 1);
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(maxParallelism);
            }));
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        this._services = services.BuildServiceProvider();

        // Builds the pipelines up front, so that a failure to do so is reported as such rather than as a timeout
        this._services.GetRequiredService<IntegrationTestBlueprintAppExecutor>();
        this._services.StartRebus();
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> holds, or a handler has recorded a failure, which no test expects and whose
    /// exception says more than a timeout would.
    /// </summary>
    private async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + _waitLimit;

        while (!condition() && this._probe.Failures.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    private async Task<InMemTransportMessage?> WaitForMessageAsync(string queue)
    {
        var deadline = DateTime.UtcNow + _waitLimit;

        while (DateTime.UtcNow < deadline && this._probe.Failures.IsEmpty)
        {
            if (this._network.GetNextOrNull(queue) is { } message)
            {
                return message;
            }

            await Task.Delay(50);
        }

        return null;
    }

    /// <summary>
    /// Sends a marker and waits for it to be handled: with one worker taking one message at a time, anything sent to the
    /// input queue before it, such as a deferred copy on its way to the timeout manager, has been received by then.
    /// </summary>
    private async Task WaitPastMarkerAsync()
    {
        await this.Bus.SendLocal(new MarkerRebusCommand());
        await this._probe.MarkerHandled.Task.WaitAsync(_waitLimit);
    }

    /// <summary>
    /// A deferral the bus asked the timeout manager to hold back.
    /// </summary>
    /// <param name="Headers">The headers of the deferred message.</param>
    private sealed record Deferral(Dictionary<string, string> Headers);

    /// <summary>
    /// A timeout manager that records each message it is asked to defer and returns it as due at its next poll, so a chain
    /// of deferrals plays out in seconds rather than minutes.
    /// </summary>
    private sealed class RecordingTimeoutManager : ITimeoutManager
    {
        private readonly ConcurrentQueue<DueMessage> _pending = new();

        private int _handedBack;

        /// <summary>
        /// Gets or sets how many deferred messages are handed back in all, after which they are held for good, so that a
        /// handler that defers without end cannot loop for as long as the test runs.
        /// </summary>
        public int MaxHandBacks { get; set; } = 10;

        public ConcurrentQueue<Deferral> Deferrals { get; } = new();

        public Task Defer(DateTimeOffset approximateDueTime, Dictionary<string, string> headers, byte[] body)
        {
            this.Deferrals.Enqueue(new Deferral(new Dictionary<string, string>(headers)));
            this._pending.Enqueue(new DueMessage(headers, body, static () => Task.CompletedTask));

            return Task.CompletedTask;
        }

        public Task<DueMessagesResult> GetDueMessages()
        {
            var due = new List<DueMessage>();

            while (this._handedBack < this.MaxHandBacks && this._pending.TryDequeue(out var message))
            {
                due.Add(message);
                this._handedBack++;
            }

            return Task.FromResult(new DueMessagesResult(due, static () => Task.CompletedTask));
        }
    }
}

/// <summary>Records what the handlers of <see cref="RebusIncomingMessageIntegrationTests"/> saw.</summary>
public sealed class RebusIncomingMessageProbe
{
    private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _deliveries;
    private int _arrived;

    /// <summary>Gets how many times a deferring handler has been given its message.</summary>
    public int Deliveries => Volatile.Read(ref this._deliveries);

    /// <summary>Gets every exception a handler met acting on its message, which it then threw.</summary>
    public ConcurrentQueue<Exception> Failures { get; } = new();

    /// <summary>Gets the defer count header of each delivery of a <see cref="CountedDeferralRebusCommand"/>.</summary>
    public ConcurrentQueue<string?> DeferCounts { get; } = new();

    /// <summary>Gets the message id each <see cref="ConcurrentRebusCommand"/> read from its message context, by name.</summary>
    public ConcurrentDictionary<string, string> HandledMessageIds { get; } = new();

    /// <summary>Gets or sets whether the handler of <see cref="DeferThenThrowRebusCommand"/> deferred before it threw.</summary>
    public bool DeferredBeforeThrowing { get; set; }

    /// <summary>Gets a task that completes when a <see cref="MarkerRebusCommand"/> has been handled.</summary>
    public TaskCompletionSource MarkerHandled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets what the work started by the handler of <see cref="LingeringWorkRebusCommand"/> met when, after the message had
    /// been handled, it resolved the message context and deferred the message.
    /// </summary>
    public TaskCompletionSource<(Exception? Resolving, Exception? Deferring)> AfterHandling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Records a delivery and returns how many there have been.</summary>
    /// <returns>The number of this delivery, from 1.</returns>
    public int RecordDelivery()
    {
        return Interlocked.Increment(ref this._deliveries);
    }

    /// <summary>Runs <paramref name="action"/>, recording any exception it throws before throwing it on.</summary>
    /// <param name="action">What the handler does with its message.</param>
    /// <returns>A task that completes when the action has.</returns>
    public async Task RecordingFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            this.Failures.Enqueue(exception);

            throw;
        }
    }

    /// <summary>Waits until two handlers have arrived, so that both are handling their messages at once.</summary>
    /// <returns>A task that completes when both have arrived.</returns>
    public Task ArriveAndWaitForBothAsync()
    {
        if (Interlocked.Increment(ref this._arrived) == 2)
        {
            this._bothArrived.TrySetResult();
        }

        return this._bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}

/// <summary>A message whose handler defers it on its first delivery.</summary>
public partial class DeferringRebusCommand : ICommand
{
    public static Task HandleAsync(DeferringRebusCommand operation, IBus bus, RebusIncomingMessageProbe probe)
    {
        return probe.RecordDelivery() == 1
            ? probe.RecordingFailureAsync(() => bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1)))
            : Task.CompletedTask;
    }
}

/// <summary>A message whose handler records its defer count, and defers it until it has been deferred twice.</summary>
public partial class CountedDeferralRebusCommand : ICommand
{
    public static Task HandleAsync(CountedDeferralRebusCommand operation, Func<IMessageContext> resolveMessageContext, IBus bus, RebusIncomingMessageProbe probe)
    {
        return probe.RecordingFailureAsync(async () =>
        {
            var deferCount = resolveMessageContext().Headers.GetValueOrDefault(Headers.DeferCount);

            probe.DeferCounts.Enqueue(deferCount);

            if (deferCount != "2")
            {
                await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1));
            }
        });
    }
}

/// <summary>A message whose handler forwards it to another queue.</summary>
public partial class ForwardingRebusCommand : ICommand
{
    public static Task HandleAsync(ForwardingRebusCommand operation, IBus bus, RebusIncomingMessageProbe probe)
    {
        return probe.RecordingFailureAsync(() => bus.Advanced.TransportMessage.Forward("rebus-incoming-message-tests-forwarded"));
    }
}

/// <summary>A message whose handler defers it, then throws.</summary>
public partial class DeferThenThrowRebusCommand : ICommand
{
    public static async Task HandleAsync(DeferThenThrowRebusCommand operation, IBus bus, RebusIncomingMessageProbe probe)
    {
        await probe.RecordingFailureAsync(() => bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1)));

        probe.DeferredBeforeThrowing = true;

        throw new InvalidOperationException("The handler failed after deferring its message.");
    }
}

/// <summary>A message whose handler publishes a <see cref="RebusIncomingMessagePublished"/>.</summary>
public partial class PublishingRebusCommand : ICommand
{
    public static Task HandleAsync(PublishingRebusCommand operation, IBus bus)
    {
        return bus.Publish(new RebusIncomingMessagePublished());
    }
}

/// <summary>The message published by the handler of <see cref="PublishingRebusCommand"/>.</summary>
public sealed class RebusIncomingMessagePublished;

/// <summary>
/// A message whose handler starts work that it does not wait for, which, once the message has been handled, resolves the
/// message context and defers the message.
/// </summary>
public partial class LingeringWorkRebusCommand : ICommand
{
    public static Task HandleAsync(LingeringWorkRebusCommand operation, IMessageContext messageContext, IBus bus, Func<IMessageContext> resolveMessageContext, RebusIncomingMessageProbe probe)
    {
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Rebus disposes the message's transaction once the handler, and the pipeline around it, have finished
        messageContext.TransactionContext.OnDisposed(_ => handled.TrySetResult());

        // Deliberately not awaited
        _ = Task.Run(async () =>
        {
            await handled.Task;

            Exception? resolving = null;
            Exception? deferring = null;

            try
            {
                _ = resolveMessageContext();
            }
            catch (Exception e)
            {
                resolving = e;
            }

            try
            {
                await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                deferring = e;
            }

            probe.AfterHandling.TrySetResult((resolving, deferring));
        });

        return Task.CompletedTask;
    }
}

/// <summary>
/// A message handled alongside another: its handler waits for the other to arrive, then reads its message context and
/// defers its message on its first delivery.
/// </summary>
public partial class ConcurrentRebusCommand : ICommand
{
    public string Name { get; set; } = string.Empty;

    public static Task HandleAsync(ConcurrentRebusCommand operation, Func<IMessageContext> resolveMessageContext, IBus bus, RebusIncomingMessageProbe probe)
    {
        return probe.RecordingFailureAsync(async () =>
        {
            await probe.ArriveAndWaitForBothAsync();

            var messageContext = resolveMessageContext();

            probe.HandledMessageIds[operation.Name] = messageContext.Headers[Headers.MessageId];

            if (!messageContext.Headers.ContainsKey(Headers.DeferCount))
            {
                await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1));
            }
        });
    }
}

/// <summary>A message, opted out of the pipeline's Rebus transaction, whose handler defers it on its first delivery.</summary>
[NoRebusTransaction]
public partial class UnscopedDeferringRebusCommand : ICommand
{
    public static Task HandleAsync(UnscopedDeferringRebusCommand operation, IBus bus, RebusIncomingMessageProbe probe)
    {
        return probe.RecordDelivery() == 1
            ? probe.RecordingFailureAsync(() => bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1)))
            : Task.CompletedTask;
    }
}

/// <summary>A message sent after the one under test, whose handling bounds when anything sent before it has arrived.</summary>
public partial class MarkerRebusCommand : ICommand
{
    public static Task HandleAsync(MarkerRebusCommand operation, RebusIncomingMessageProbe probe)
    {
        probe.MarkerHandled.TrySetResult();

        return Task.CompletedTask;
    }
}
