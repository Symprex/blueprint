using System.Collections.Concurrent;
using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Transport;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="RebusServiceCollectionExtensions.AddBlueprintRebusConsumer"/> registers a plain
/// <see cref="IHandleMessages{TMessage}"/> of a base class or interface for every subscribed message type derived from it,
/// as legacy's scanner did, so that <see cref="BlueprintApiActivator"/> runs it for such a message.
/// </summary>
/// <remarks>
/// <para>
/// Legacy made every subscribed message an operation (<c>Symprex.Signature365.BlueprintRebus/BlueprintRebusConfiguration.cs:34-42</c>
/// at Signature365 <c>8b40ecc961</c>), and gave each operation every scanned <c>IHandleMessages&lt;X&gt;</c> whose
/// <c>X</c> it could be assigned to (<c>RebusHandleMessageOperationExecutorBuilderScanner.cs:50-75</c>, the
/// <c>IsAssignableFrom</c> at <c>:67</c>). So Signature365's <c>SurveyThankYouKvPushHandler</c>, an
/// <c>IHandleMessages&lt;SurveyEditedEvent&gt;</c>, ran for the subscribed <c>SurveyPausedEvent : SurveyEditedEvent</c>
/// (Portal's generated <c>SurveyPausedEventExecutorPipeline.cs:68-71</c>). Rebus took no part: legacy's activator matched
/// the message's own type to an operation and returned that operation's pipeline (<c>BlueprintApiActivator.cs:41-56</c>).
/// </para>
/// <para>
/// The scanner's other direction — an <c>IHandleMessages&lt;X&gt;</c> for an <c>X</c> derived from the operation's type
/// (<c>IsAssignableTo</c>) — was never reached for a message: its generated call passed the operation's variable to a
/// parameter of the derived type, and legacy's activator only ever ran the pipeline of the message's own, concrete type.
/// It is not reproduced.
/// </para>
/// </remarks>
public class RebusBaseTypeHandlerRegistrationTests
{
    [Fact]
    public async Task A_handler_of_a_base_class_or_interface_handles_a_subscribed_derived_message()
    {
        using var provider = BuildProvider(static c => c.Subscribe<BaseTypeEditedEvent>().Subscribe<BaseTypePausedEvent>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var message = new BaseTypePausedEvent { Name = "paused" };
        var handlers = (await activator.GetHandlers(message, transactionScope.TransactionContext)).ToList();

        // In the order legacy's scanner gave an operation its handlers: by handler type as the assembly lists them (their
        // declaration order below), not the message's own type first.
        handlers.Select(static h => h.GetType()).Should().Equal(
            [typeof(BaseTypeEditedEventHandler), typeof(BaseTypeMarkerHandler), typeof(BaseTypePausedEventHandler)],
            "the base class's, the interface's and the message's own handler each handle it, once");

        foreach (var handler in handlers)
        {
            await handler.Handle(message);
        }

        provider.GetRequiredService<BaseTypeLog>().Entries.Should().Equal(
            "edited handler: BaseTypePausedEvent paused",
            "marker handler: BaseTypePausedEvent paused",
            "paused handler: BaseTypePausedEvent paused");
    }

    [Fact]
    public async Task A_handler_of_a_base_class_does_not_handle_a_derived_message_that_is_not_subscribed()
    {
        // Legacy ran nothing at all for a message that was not subscribed: it was not an operation (BlueprintApiActivator.cs:41-46).
        using var provider = BuildProvider(static c => c.Subscribe<BaseTypeEditedEvent>().Subscribe<BaseTypePausedEvent>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new BaseTypeUnsubscribedEvent(), transactionScope.TransactionContext);

        handlers.Should().BeEmpty();
    }

    [Fact]
    public async Task A_handler_of_a_derived_message_does_not_handle_its_base_message()
    {
        using var provider = BuildProvider(static c => c.Subscribe<BaseTypeEditedEvent>().Subscribe<BaseTypePausedEvent>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new BaseTypeEditedEvent(), transactionScope.TransactionContext);

        handlers.Should().ContainSingle().Which.Should().BeOfType<BaseTypeEditedEventHandler>();
    }

    [Fact]
    public async Task A_derived_message_subscribed_twice_is_given_its_base_class_handler_once()
    {
        // Legacy kept one operation per type (ApiDataModel.cs RegisterOperation, keyed by the operation's type).
        using var provider = BuildProvider(static c => c.Subscribe<BaseTypePausedEvent>().Subscribe<BaseTypePausedEvent>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handlers = await activator.GetHandlers(new BaseTypePausedEvent(), transactionScope.TransactionContext);

        handlers.Select(static h => h.GetType()).Should().BeEquivalentTo(
            [typeof(BaseTypeEditedEventHandler), typeof(BaseTypeMarkerHandler), typeof(BaseTypePausedEventHandler)]);
    }

    [Fact]
    public async Task A_handler_of_both_a_base_and_a_derived_message_runs_twice_for_the_derived_one_through_its_derived_method()
    {
        // Legacy's scanner matched the handler once per interface (RebusHandleMessageOperationExecutorBuilderScanner.cs:54-74),
        // an operation kept every handler registered (ApiOperationDescriptor.cs:179-194, multiple allowed by
        // RebusScannerConvention.cs:41), and its generated source called handler.Handle(message) for each, which C#
        // resolved to the derived overload both times.
        using var provider = BuildProvider(static c => c.Subscribe<BaseTypeRenamedEvent>().Subscribe<BaseTypeArchivedEvent>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var archived = new BaseTypeArchivedEvent();

        foreach (var handler in await activator.GetHandlers(archived, transactionScope.TransactionContext))
        {
            await handler.Handle(archived);
        }

        var renamed = new BaseTypeRenamedEvent();

        foreach (var handler in await activator.GetHandlers(renamed, transactionScope.TransactionContext))
        {
            await handler.Handle(renamed);
        }

        provider.GetRequiredService<BaseTypeLog>().Entries.Should().Equal(
            "both handler, archived method",
            "both handler, archived method",
            "both handler, renamed method");
    }

    [Fact]
    public async Task Rebus_bus_delivers_a_subscribed_derived_message_to_the_handler_of_its_base_class()
    {
        var network = new InMemNetwork();
        var services = new ServiceCollection();

        services.AddSingleton(new BlueprintApp());
        services.AddSingleton<BaseTypeLog>();
        services.AddBlueprintRebusConsumer(static c => c.Subscribe<BaseTypeEditedEvent>().Subscribe<BaseTypePausedEvent>());
        services.AddBlueprintRebus((_, configurer) => configurer.Transport(t => t.UseInMemoryTransport(network, "base-type-handlers")));

        await using var provider = services.BuildServiceProvider();
        provider.StartRebus();

        await provider.GetRequiredService<IBus>().SendLocal(new BaseTypePausedEvent { Name = "sent" });

        var log = provider.GetRequiredService<BaseTypeLog>();
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (log.Entries.Count < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        log.Entries.Should().BeEquivalentTo(
            "edited handler: BaseTypePausedEvent sent",
            "marker handler: BaseTypePausedEvent sent",
            "paused handler: BaseTypePausedEvent sent");
    }

    private static ServiceProvider BuildProvider(Action<BlueprintRebusConfiguration> subscribe)
    {
        var services = new ServiceCollection();

        // An empty app, so every message type takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddSingleton<BaseTypeLog>();

        // Called from this assembly, so it registers this assembly's IHandleMessages<T> implementations, these included.
        services.AddBlueprintRebusConsumer(subscribe);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

/// <summary>What the handlers of <see cref="RebusBaseTypeHandlerRegistrationTests"/> were given.</summary>
public sealed class BaseTypeLog
{
    public ConcurrentQueue<string> Entries { get; } = new();
}

/// <summary>A marker interface a derived message implements, as a base type a handler can handle.</summary>
public interface IBaseTypeMarker;

/// <summary>A base message, as Signature365's <c>SurveyEditedEvent</c>.</summary>
public class BaseTypeEditedEvent
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>A subscribed message derived from <see cref="BaseTypeEditedEvent"/>, as <c>SurveyPausedEvent</c>.</summary>
public sealed class BaseTypePausedEvent : BaseTypeEditedEvent, IBaseTypeMarker;

/// <summary>A message derived from <see cref="BaseTypeEditedEvent"/> that no test subscribes.</summary>
public sealed class BaseTypeUnsubscribedEvent : BaseTypeEditedEvent;

/// <summary>A handler of the base message, as <c>SurveyThankYouKvPushHandler</c> handles <c>SurveyEditedEvent</c>.</summary>
public sealed class BaseTypeEditedEventHandler(BaseTypeLog log) : IHandleMessages<BaseTypeEditedEvent>
{
    public Task Handle(BaseTypeEditedEvent message)
    {
        log.Entries.Enqueue($"edited handler: {message.GetType().Name} {message.Name}");

        return Task.CompletedTask;
    }
}

/// <summary>A handler of an interface the derived message implements.</summary>
public sealed class BaseTypeMarkerHandler(BaseTypeLog log) : IHandleMessages<IBaseTypeMarker>
{
    public Task Handle(IBaseTypeMarker message)
    {
        log.Entries.Enqueue($"marker handler: {message.GetType().Name} {((BaseTypeEditedEvent)message).Name}");

        return Task.CompletedTask;
    }
}

/// <summary>A handler of the derived message's own type.</summary>
public sealed class BaseTypePausedEventHandler(BaseTypeLog log) : IHandleMessages<BaseTypePausedEvent>
{
    public Task Handle(BaseTypePausedEvent message)
    {
        log.Entries.Enqueue($"paused handler: {message.GetType().Name} {message.Name}");

        return Task.CompletedTask;
    }
}

/// <summary>A base message of a second pair, handled by <see cref="BaseTypeBothHandler"/> alone.</summary>
public class BaseTypeRenamedEvent;

/// <summary>A message derived from <see cref="BaseTypeRenamedEvent"/>.</summary>
public sealed class BaseTypeArchivedEvent : BaseTypeRenamedEvent;

/// <summary>A handler of both the base message and the message derived from it.</summary>
public sealed class BaseTypeBothHandler(BaseTypeLog log) : IHandleMessages<BaseTypeRenamedEvent>, IHandleMessages<BaseTypeArchivedEvent>
{
    public Task Handle(BaseTypeRenamedEvent message)
    {
        log.Entries.Enqueue("both handler, renamed method");

        return Task.CompletedTask;
    }

    public Task Handle(BaseTypeArchivedEvent message)
    {
        log.Entries.Enqueue("both handler, archived method");

        return Task.CompletedTask;
    }
}
