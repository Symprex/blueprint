using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves the shape a host with no Blueprint operations takes to use Blueprint's Rebus integration: it registers its
/// <see cref="BlueprintApp"/> directly and does not call <c>AddBlueprintApi</c>, whose executor type the generator only
/// emits for an app with a <c>[BlueprintAppScan]</c>.
/// </summary>
/// <remarks>
/// Legacy Signature365's GraphProxy, which declares no operations, called <c>AddBlueprintApi</c> only because its Rebus
/// setup went through Blueprint (Signature365 <c>8b40ecc961</c>, <c>GraphProxy/Startup.cs:95-100</c>). Legacy's
/// activator needed only the data model to be built (<c>BlueprintRebus/BlueprintApiActivator.cs:28-31</c>), the Rebus
/// configuration for a message that was not an operation (:64), and the executor only to run an operation (:88). This
/// one needs the same: the app (<c>BlueprintApiActivator.cs:52</c>), the configuration (:143) and the executor only for
/// an operation (:225).
/// </remarks>
public class PublishOnlyHostTests
{
    [Fact]
    public async Task A_host_that_registers_only_its_app_starts_rebus_and_sends_and_handles_a_message()
    {
        var network = new InMemNetwork();
        var services = new ServiceCollection();
        var received = new PublishOnlyReceipts();

        services.AddSingleton(received);
        services.AddSingleton<BlueprintApp, PublishOnlyBlueprintApp>();
        services.AddBlueprintRebusConsumer(_ => { });
        services.AddBlueprintRebus((_, configurer) => configurer.Transport(t => t.UseInMemoryTransport(network, "publish-only")));

        await using var provider = services.BuildServiceProvider();
        provider.StartRebus();

        await provider.GetRequiredService<IBus>().SendLocal(new PublishOnlyMessage { Text = "renewal failed" });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (received.Texts.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        received.Texts.Should().Equal("renewal failed");
    }

    private sealed class PublishOnlyBlueprintApp : BlueprintApp;
}

/// <summary>A message the publish-only host sends itself.</summary>
public sealed class PublishOnlyMessage
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>What the publish-only host's handler received.</summary>
public sealed class PublishOnlyReceipts
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Texts { get; } = new();
}

/// <summary>A plain Rebus handler, registered by <c>AddBlueprintRebusConsumer</c>'s scan of this assembly.</summary>
public sealed class PublishOnlyMessageHandler(PublishOnlyReceipts receipts) : IHandleMessages<PublishOnlyMessage>
{
    public Task Handle(PublishOnlyMessage message)
    {
        receipts.Texts.Enqueue(message.Text);

        return Task.CompletedTask;
    }
}
