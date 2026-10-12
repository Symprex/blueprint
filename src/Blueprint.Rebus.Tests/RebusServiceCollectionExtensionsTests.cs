using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Transport.InMem;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="RebusServiceCollectionExtensions.AddBlueprintRebusConsumer"/> correctly
/// registers configuration and <see cref="RebusServiceCollectionExtensions.AddBlueprintRebus"/>
/// guards against double-registration.
/// </summary>
public class RebusServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBlueprintRebusConsumer_registers_configuration()
    {
        var services = new ServiceCollection();

        services.AddBlueprintRebusConsumer(config =>
        {
            config.Subscribe<TestMessage>();
        });

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<BlueprintRebusConfiguration>();

        config.SubscribedMessages.Should().ContainSingle()
            .Which.Should().Be(typeof(TestMessage));
    }

    [Fact]
    public void AddBlueprintRebusConsumer_registers_a_convention_marking_only_subscribed_messages_as_anonymous_and_unexposed()
    {
        // As legacy Signature365's RebusScannerConvention marked a subscribed message at start-up; AddBlueprintApi applies
        // the convention as the app is made.
        var services = new ServiceCollection();
        services.AddBlueprintRebusConsumer(static config => config.Subscribe<TestMessage>());
        using var sp = services.BuildServiceProvider();

        var subscribed = new Model.ApiOperationDescriptor(typeof(TestMessage), nameof(TestMessage), "test");
        var other = new Model.ApiOperationDescriptor(typeof(OtherMessage), nameof(OtherMessage), "test");

        foreach (var convention in sp.GetServices<IOperationDescriptorConvention>())
        {
            convention.Apply(subscribed);
            convention.Apply(other);
        }

        subscribed.AnonymousAccessAllowed.Should().BeTrue();
        subscribed.IsExposed.Should().BeFalse();
        subscribed.TryGetFeatureData<RebusMessageFeature>(out _).Should().BeTrue();
        other.AnonymousAccessAllowed.Should().BeFalse("only a subscribed message is marked");
        other.IsExposed.Should().BeTrue();
    }

    [Fact]
    public void AddBlueprintRebus_when_non_fake_bus_already_registered_throws()
    {
        var services = new ServiceCollection();

        // ServiceDescriptor stores types without validation — we only need ImplementationType != null
        // and Name not ending with "FakeBus" to trigger the guard.
        var descriptor = new ServiceDescriptor(typeof(IBus), typeof(object), ServiceLifetime.Singleton);
        ((IList<ServiceDescriptor>)services).Add(descriptor);

        var act = () => services.AddBlueprintRebus((_, configurer) => configurer);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been configured*");
    }

    [Fact]
    public void AddBlueprintRebus_when_fake_bus_registered_skips_silently()
    {
        var services = new ServiceCollection();

        // Instance-based registration has null ImplementationType, treated as "possibly fake"
        services.AddSingleton<IBus>(NSubstitute.Substitute.For<IBus>());

        var act = () => services.AddBlueprintRebus((_, configurer) => configurer);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddBlueprintRebus_registers_a_bus_that_wraps_the_started_one()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Model.BlueprintApp());
        services.AddBlueprintRebusConsumer(static _ => { });
        services.AddBlueprintRebus(static (_, configurer) => configurer.Transport(static t => t.UseInMemoryTransport(new InMemNetwork(), "wrapped-bus")));

        using var sp = services.BuildServiceProvider();

        var bus = sp.GetRequiredService<IBus>();

        bus.Should().BeOfType<IncomingMessageAwareBus>("deferring a message from a pipeline needs the bus that knows the message being handled");
        bus.Should().NotBeSameAs(sp.GetRequiredService<IBusStarter>().Bus);
    }

    [Fact]
    public void AddBlueprintRebus_registers_a_message_context_that_is_the_one_a_pipeline_is_handling()
    {
        var services = new ServiceCollection();
        services.AddBlueprintRebus(static (_, configurer) => configurer);
        using var sp = services.BuildServiceProvider();

        var incoming = NSubstitute.Substitute.For<IMessageContext>();

        // Rebus' own MessageContext.Current is null, as it is inside the pipeline's Rebus transaction scope
        using (IncomingMessageContext.Begin(incoming))
        {
            sp.GetRequiredService<IMessageContext>().Should().BeSameAs(incoming);
        }

        var outside = () => sp.GetRequiredService<IMessageContext>();

        outside.Should().Throw<InvalidOperationException>().WithMessage("Attempted to resolve IMessageContext outside of a Rebus handler*");
    }

    [Fact]
    public void AddBlueprintRebusConsumer_with_null_services_throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddBlueprintRebusConsumer(_ => { });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBlueprintRebusConsumer_with_null_configure_throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddBlueprintRebusConsumer(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private class TestMessage;

    private class OtherMessage;
}
