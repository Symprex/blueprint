using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Rebus.Bus;
using Xunit;

namespace Blueprint.Marten.Rebus.Tests;

/// <summary>
/// Verifies that <see cref="MartenRebusServiceCollectionExtensions.AddBlueprintMartenRebus"/>
/// correctly registers the <see cref="RebusDomainEventPublisher"/> in DI.
/// </summary>
public class MartenRebusServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBlueprintMartenRebus_registers_RebusDomainEventPublisher()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IBus>());

        services.AddBlueprintMartenRebus();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetService<IDomainEventPublisher>();

        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<RebusDomainEventPublisher>();
    }

    [Fact]
    public void AddBlueprintMartenRebus_registers_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IBus>());

        services.AddBlueprintMartenRebus();

        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var publisher1 = scope1.ServiceProvider.GetRequiredService<IDomainEventPublisher>();
        var publisher2 = scope2.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

        publisher1.Should().NotBeSameAs(publisher2);
    }
}
