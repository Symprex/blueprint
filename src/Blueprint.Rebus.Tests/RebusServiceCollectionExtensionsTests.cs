using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;

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
}
