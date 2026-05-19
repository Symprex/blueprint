using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class ApiResourceDescriptorTests
{
    [Fact]
    public void Freeze_Should_Auto_Derive_Resource_Descriptors_From_Links()
    {
        // Arrange
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var selfLink = new ApiOperationLink(descriptor, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        descriptor.AddLink(selfLink);
        app.Register(descriptor);

        // Act
        app.Freeze();

        // Assert
        app.TryGetResourceDescriptor(typeof(UserApiResource), out var resourceDescriptor).Should().BeTrue();
        resourceDescriptor!.ResourceType.Should().Be(typeof(UserApiResource));
        resourceDescriptor.Name.Should().Be("user");
        resourceDescriptor.SelfLink.Should().BeSameAs(selfLink);
        resourceDescriptor.Links.Should().Contain(selfLink);
        resourceDescriptor.Producers.Should().Contain(descriptor);
    }

    [Fact]
    public void Freeze_Should_Derive_Multiple_Resource_Descriptors()
    {
        // Arrange
        var app = new BlueprintApp();
        var getUserDesc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var getOrderDesc = new ApiOperationDescriptor(typeof(GetOrderQuery), "GetOrder", "test");

        var userLink = new ApiOperationLink(getUserDesc, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        var orderLink = new ApiOperationLink(getOrderDesc, "/orders/{Id}", "orders/{Id}", "self", typeof(OrderApiResource));

        getUserDesc.AddLink(userLink);
        getOrderDesc.AddLink(orderLink);
        app.Register(getUserDesc);
        app.Register(getOrderDesc);

        // Act
        app.Freeze();

        // Assert
        app.ResourceCount.Should().Be(2);
        app.TryGetResourceDescriptor(typeof(UserApiResource), out _).Should().BeTrue();
        app.TryGetResourceDescriptor(typeof(OrderApiResource), out _).Should().BeTrue();
    }

    [Fact]
    public void Freeze_Should_Collect_Multiple_Links_For_Same_Resource()
    {
        // Arrange
        var app = new BlueprintApp();
        var getUserDesc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var updateUserDesc = new ApiOperationDescriptor(typeof(UpdateUserCommand), "UpdateUser", "test");

        getUserDesc.SetFeatureData(WellKnownFeatureData.HttpGet);
        updateUserDesc.SetFeatureData(WellKnownFeatureData.HttpPut);

        var selfLink = new ApiOperationLink(getUserDesc, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        var updateLink = new ApiOperationLink(updateUserDesc, "/users/{Id}", "users/{Id}", "update", typeof(UserApiResource));

        getUserDesc.AddLink(selfLink);
        updateUserDesc.AddLink(updateLink);
        app.Register(getUserDesc);
        app.Register(updateUserDesc);

        // Act
        app.Freeze();

        // Assert
        app.TryGetResourceDescriptor(typeof(UserApiResource), out var resource).Should().BeTrue();
        resource!.Links.Should().HaveCount(2);
        resource.SelfLink.Should().BeSameAs(selfLink);
        resource.Producers.Should().ContainSingle().Which.Should().BeSameAs(getUserDesc);
    }

    [Fact]
    public void GetResourceDescriptor_Should_Throw_When_Not_Found()
    {
        // Arrange
        var app = new BlueprintApp();
        app.Freeze();

        // Act
        var act = () => app.GetResourceDescriptor(typeof(UserApiResource));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UserApiResource*");
    }

    [Fact]
    public void TryGetResourceDescriptor_Should_Return_False_When_Not_Found()
    {
        // Arrange
        var app = new BlueprintApp();
        app.Freeze();

        // Act
        var result = app.TryGetResourceDescriptor(typeof(UserApiResource), out var descriptor);

        // Assert
        result.Should().BeFalse();
        descriptor.Should().BeNull();
    }

    [Fact]
    public void RegisterResource_Should_Allow_Manual_Pre_Registration()
    {
        // Arrange
        var app = new BlueprintApp();
        var manual = new ApiResourceDescriptor(typeof(UserApiResource), "user");
        manual.AddStateBasedChecker(typeof(FakeStateChecker));
        app.RegisterResource(manual);

        var getUserDesc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var selfLink = new ApiOperationLink(getUserDesc, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        getUserDesc.AddLink(selfLink);
        app.Register(getUserDesc);

        // Act
        app.Freeze();

        // Assert — the pre-registered descriptor is enriched, not replaced
        app.TryGetResourceDescriptor(typeof(UserApiResource), out var resource).Should().BeTrue();
        resource.Should().BeSameAs(manual);
        resource!.StateBasedCheckers.Should().Contain(typeof(FakeStateChecker));
        resource.SelfLink.Should().BeSameAs(selfLink);
        resource.Producers.Should().Contain(getUserDesc);
    }

    [Fact]
    public void DeriveResourceName_Should_Strip_ApiResource_Suffix()
    {
        // Arrange & Act
        var app = new BlueprintApp();
        var desc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var link = new ApiOperationLink(desc, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        desc.AddLink(link);
        app.Register(desc);
        app.Freeze();

        // Assert
        app.TryGetResourceDescriptor(typeof(UserApiResource), out var resource).Should().BeTrue();
        resource!.Name.Should().Be("user");
    }

    [Fact]
    public void DeriveResourceName_Should_Strip_Resource_Suffix()
    {
        // Arrange & Act
        var app = new BlueprintApp();
        var desc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var link = new ApiOperationLink(desc, "/users/{Id}", "users/{Id}", "self", typeof(UserResource));
        desc.AddLink(link);
        app.Register(desc);
        app.Freeze();

        // Assert
        app.TryGetResourceDescriptor(typeof(UserResource), out var resource).Should().BeTrue();
        resource!.Name.Should().Be("user");
    }

    [Fact]
    public void Resources_Property_Should_Return_All_Descriptors()
    {
        // Arrange
        var app = new BlueprintApp();
        var desc = new ApiOperationDescriptor(typeof(GetUserQuery), "GetUser", "test");
        var link = new ApiOperationLink(desc, "/users/{Id}", "users/{Id}", "self", typeof(UserApiResource));
        desc.AddLink(link);
        app.Register(desc);
        app.Freeze();

        // Act
        var resources = app.Resources.ToList();

        // Assert
        resources.Should().HaveCount(1);
        resources[0].ResourceType.Should().Be(typeof(UserApiResource));
    }

    [Fact]
    public void ApiResourceDescriptor_ToString_Should_Include_Summary()
    {
        // Arrange
        var descriptor = new ApiResourceDescriptor(typeof(UserApiResource), "user");

        // Assert
        descriptor.ToString().Should().Contain("user").And.Contain("0 links").And.Contain("0 producers");
    }

    private class GetUserQuery;

    private class GetOrderQuery;

    private class UpdateUserCommand;

    private class UserApiResource;

    private class UserResource;

    private class OrderApiResource;

    private class FakeStateChecker;
}
