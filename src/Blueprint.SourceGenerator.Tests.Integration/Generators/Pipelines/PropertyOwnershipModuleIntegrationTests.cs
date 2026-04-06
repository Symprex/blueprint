using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves generated population stage binds route and body properties end-to-end.
/// </summary>
public class PropertyOwnershipModuleIntegrationTests
{
    [Fact]
    public void When_post_with_route_and_body_then_descriptor_has_ownership_feature_data()
    {
        var descriptor = UpdateUserPopulationCommand.Configure();

        // Verify the descriptor has ownership feature data
        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue("descriptor should contain property ownership feature data");

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Name" && e.Source == PropertyBindingSource.Body);
        ownershipData.Entries.Should().Contain(
            static e => e.PropertyName == "Email" && e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_get_query_with_route_param_then_remaining_properties_are_not_body_owned()
    {
        var descriptor = GetUserPopulationQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);

        // GET query: remaining properties should NOT be body-owned
        ownershipData.Entries.Should().NotContain(
            static e => e.Source == PropertyBindingSource.Body);
    }

    [Fact]
    public void When_route_property_is_int_then_descriptor_shows_route_ownership()
    {
        var descriptor = GetUserByIntIdQuery.Configure();

        descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData)
            .Should().BeTrue();

        ownershipData!.Entries.Should().Contain(
            static e => e.PropertyName == "Id" && e.Source == PropertyBindingSource.Route);
    }

    [Fact]
    public void When_http_method_metadata_set_then_descriptor_reports_correct_methods()
    {
        var descriptor = UpdateUserPopulationCommand.Configure();

        descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var methodData)
            .Should().BeTrue();

        methodData!.HttpMethods.Should().Contain("POST");
    }
}

// Test operations — these are discovered by the source generator at compile time.

[Blueprint.Model.Attributes.RootLink("/population-test/users-update/{Id}")]
public partial class UpdateUserPopulationCommand : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(UpdateUserPopulationCommand operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Id}:{operation.Name}:{operation.Email}");
    }
}

[Blueprint.Model.Attributes.RootLink("/population-test/users/{Id}")]
public partial class GetUserPopulationQuery : IQuery<string>
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public static Task<string> HandleAsync(GetUserPopulationQuery operation)
    {
        return Task.FromResult($"{operation.Id}:{operation.Name}");
    }
}

[Blueprint.Model.Attributes.RootLink("/population-test/users-int/{Id}")]
public partial class GetUserByIntIdQuery : IQuery<string>
{
    public int Id { get; set; }

    public static Task<string> HandleAsync(GetUserByIntIdQuery operation)
    {
        return Task.FromResult($"user-{operation.Id}");
    }
}
