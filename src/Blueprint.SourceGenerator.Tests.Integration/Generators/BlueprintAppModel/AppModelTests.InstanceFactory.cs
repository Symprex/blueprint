using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelInstanceFactoryTests
{
    [Fact]
    public void When_descriptor_has_instance_factory_then_create_instance_uses_factory()
    {
        var descriptor = StaticTypedHandlerQuery.Configure();

        descriptor.InstanceFactory.Should().NotBeNull("generated descriptors should set InstanceFactory");

        var instance = descriptor.CreateInstance();
        instance.Should().BeOfType<StaticTypedHandlerQuery>();
    }

    [Fact]
    public void When_operation_has_required_members_then_create_instance_still_works()
    {
        var descriptor = GetCatchAllFolderResourceQuery.Configure();

        descriptor.InstanceFactory.Should().NotBeNull();

        var instance = descriptor.CreateInstance();
        instance.Should().BeOfType<GetCatchAllFolderResourceQuery>();
    }

    [Fact]
    public void When_all_registered_operations_have_instance_factory_then_none_fall_back_to_activator()
    {
        var app = new IntegrationTestBlueprintApp();

        foreach (var descriptor in app.Operations)
        {
            descriptor.InstanceFactory.Should().NotBeNull(
                $"operation '{descriptor.OperationType.Name}' should have a compiled InstanceFactory");
        }
    }
}
