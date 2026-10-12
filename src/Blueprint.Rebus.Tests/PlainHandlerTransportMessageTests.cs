using System.Collections.Concurrent;
using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Retry.Simple;
using Rebus.Timeouts;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that a plain (non-operation) <see cref="IHandleMessages{TMessage}"/>, which Rebus runs in the message's own
/// transaction with no pipeline around it, can defer the message it is handling and read its headers through an injected
/// <see cref="IMessageContext"/>, through the <see cref="IBus"/> that <c>AddBlueprintRebus</c> registers.
/// </summary>
public sealed class PlainHandlerTransportMessageTests
{
    [Fact]
    public async Task A_plain_handler_defers_the_message_it_is_handling_and_reads_its_defer_count()
    {
        var network = new InMemNetwork();
        var recorder = new PlainDeferralRecorder();
        var services = new ServiceCollection();

        services.AddSingleton(recorder);
        services.AddSingleton(new BlueprintApp());
        services.AddBlueprintRebusConsumer(static _ => { });
        services.AddBlueprintRebus((_, configurer) => configurer
            .Transport(t => t.UseInMemoryTransport(network, "plain-handler-transport-message"))
            .Timeouts(static t => t.Register(static _ => new HandBackTimeoutManager()))
            .Options(static o => o.RetryStrategy(maxDeliveryAttempts: 1)));

        await using var provider = services.BuildServiceProvider();
        provider.StartRebus();

        await provider.GetRequiredService<IBus>().SendLocal(new PlainDeferredMessage());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (recorder.DeferCounts.Count < 3 && recorder.Failures.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        recorder.Failures.Should().BeEmpty();
        recorder.DeferCounts.Should().Equal(null, "1", "2");
    }

    /// <summary>A message the real bus serialises, so it must be public.</summary>
    public sealed class PlainDeferredMessage;

    /// <summary>What the handler of <see cref="PlainDeferredMessage"/> saw.</summary>
    public sealed class PlainDeferralRecorder
    {
        public ConcurrentQueue<string?> DeferCounts { get; } = new();

        public ConcurrentQueue<Exception> Failures { get; } = new();
    }

    /// <summary>
    /// Records the defer count of each delivery, and defers the message until it has been deferred twice. Registered by
    /// <c>AddBlueprintRebusConsumer</c>'s scan of this assembly.
    /// </summary>
    public sealed class PlainDeferredMessageHandler(IMessageContext messageContext, IBus bus, PlainDeferralRecorder recorder) : IHandleMessages<PlainDeferredMessage>
    {
        public async Task Handle(PlainDeferredMessage message)
        {
            try
            {
                var deferCount = messageContext.Headers.GetValueOrDefault(Headers.DeferCount);

                recorder.DeferCounts.Enqueue(deferCount);

                if (deferCount != "2")
                {
                    await bus.Advanced.TransportMessage.Defer(TimeSpan.FromMinutes(1));
                }
            }
            catch (Exception exception)
            {
                recorder.Failures.Enqueue(exception);

                throw;
            }
        }
    }

    /// <summary>A timeout manager that hands each deferred message back at its next poll, at most ten in all.</summary>
    private sealed class HandBackTimeoutManager : ITimeoutManager
    {
        private readonly ConcurrentQueue<DueMessage> _pending = new();

        private int _handedBack;

        public Task Defer(DateTimeOffset approximateDueTime, Dictionary<string, string> headers, byte[] body)
        {
            this._pending.Enqueue(new DueMessage(headers, body, static () => Task.CompletedTask));

            return Task.CompletedTask;
        }

        public Task<DueMessagesResult> GetDueMessages()
        {
            var due = new List<DueMessage>();

            while (this._handedBack < 10 && this._pending.TryDequeue(out var message))
            {
                due.Add(message);
                this._handedBack++;
            }

            return Task.FromResult(new DueMessagesResult(due, static () => Task.CompletedTask));
        }
    }
}
