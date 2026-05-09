using Blueprint.Model;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// Proves that <see cref="ServiceCollectionExtensions.AddBlueprintApi{TApp,TExec}"/> registers
/// both concrete and base types so handlers can inject <see cref="BlueprintAppExecutor"/> or
/// <see cref="BlueprintApp"/> without knowing the generated concrete type.
/// </summary>
public class DiRegistrationTests
{
    [Fact]
    public void AddBlueprintApi_registers_concrete_app_type()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        using var sp = services.BuildServiceProvider();

        sp.GetService<IntegrationTestBlueprintApp>().Should().NotBeNull();
    }

    [Fact]
    public void AddBlueprintApi_registers_base_BlueprintApp_type()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        using var sp = services.BuildServiceProvider();

        var baseApp = sp.GetService<BlueprintApp>();
        baseApp.Should().NotBeNull();
        baseApp.Should().BeOfType<IntegrationTestBlueprintApp>();
    }

    [Fact]
    public void AddBlueprintApi_registers_concrete_executor_type()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        using var sp = services.BuildServiceProvider();

        sp.GetService<IntegrationTestBlueprintAppExecutor>().Should().NotBeNull();
    }

    [Fact]
    public void AddBlueprintApi_registers_base_BlueprintAppExecutor_type()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        using var sp = services.BuildServiceProvider();

        var baseExec = sp.GetService<BlueprintAppExecutor>();
        baseExec.Should().NotBeNull();
        baseExec.Should().BeOfType<IntegrationTestBlueprintAppExecutor>();
    }

    [Fact]
    public void AddBlueprintApi_base_and_concrete_resolve_same_instance()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        using var sp = services.BuildServiceProvider();

        var concreteApp = sp.GetRequiredService<IntegrationTestBlueprintApp>();
        var baseApp = sp.GetRequiredService<BlueprintApp>();
        baseApp.Should().BeSameAs(concreteApp);

        var concreteExec = sp.GetRequiredService<IntegrationTestBlueprintAppExecutor>();
        var baseExec = sp.GetRequiredService<BlueprintAppExecutor>();
        baseExec.Should().BeSameAs(concreteExec);
    }
}
