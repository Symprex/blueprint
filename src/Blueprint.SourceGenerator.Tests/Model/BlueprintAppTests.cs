using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class BlueprintAppTests
{
    [Fact]
    public void Register_Should_Add_Operation_Descriptor()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");

        // Act
        model.Register(descriptor);

        // Assert
        model.Operations.Should().Contain(descriptor);
    }

    [Fact]
    public void GetLinksForOperation_Should_Return_Empty_When_No_Links_Registered()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        model.Register(descriptor);

        // Act
        var links = model.GetLinksForOperation(typeof(TestOperation));

        // Assert
        links.Should().BeEmpty();
    }

    [Fact]
    public void GetLinksForResource_Should_Return_Empty_When_No_Links_Registered()
    {
        // Arrange
        var model = new BlueprintApp();

        // Act
        var links = model.GetLinksForResource(typeof(TestResource));

        // Assert
        links.Should().BeEmpty();
    }

    [Fact]
    public void FindOperation_Should_Return_Descriptor_When_Operation_Registered()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        model.Register(descriptor);

        // Act
        var foundDescriptor = model.FindOperation(typeof(TestOperation));

        // Assert
        foundDescriptor.Should().Be(descriptor);
    }

    [Fact]
    public void FindOperation_Should_Throw_Exception_When_Operation_Not_Registered()
    {
        // Arrange
        var model = new BlueprintApp();

        // Act
        Action act = () => model.FindOperation(typeof(TestOperation));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Could not find an operation of the type Blueprint.SourceGenerator.Tests.Model.BlueprintAppTests+TestOperation.\n\nMake sure that it has been correctly registered through the use of the .Operations(...) configuration method at startup.\n\nOperations will be found polymorphically, meaning an operation could be registered as an interface with the model but found as a concrete implementation type at runtime");
    }

    [Fact]
    public void TryFindOperation_Should_Return_True_And_Descriptor_When_Operation_Registered()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        model.Register(descriptor);

        // Act
        var result = model.TryFindOperation(typeof(TestOperation), out var foundDescriptor);

        // Assert
        result.Should().BeTrue();
        foundDescriptor.Should().Be(descriptor);
    }

    [Fact]
    public void TryFindOperation_Should_Return_False_When_Operation_Not_Registered()
    {
        // Arrange
        var model = new BlueprintApp();

        // Act
        var result = model.TryFindOperation(typeof(TestOperation), out var foundDescriptor);

        // Assert
        result.Should().BeFalse();
        foundDescriptor.Should().BeNull();
    }

    [Fact]
    public void GetLinksForOperation_Should_Throw_Exception_When_Operation_Not_Registered()
    {
        // Arrange
        var model = new BlueprintApp();

        // Act
        Action act = () => model.GetLinksForOperation(typeof(TestOperation));

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot get links for operation 'TestOperation' as it has not been registered.");
    }

    [Fact]
    public void GetLinkFor_Should_Return_Null_When_Resource_Type_Not_Registered()
    {
        // Arrange
        var model = new BlueprintApp();

        // Act
        var link = model.GetLinkFor(typeof(TestResource), "self");

        // Assert
        link.Should().BeNull();
    }

    [Fact]
    public void Register_Should_Throw_Exception_When_Duplicate_Url_Across_Descriptors()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor1 = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation1", "TestSource1");
        var descriptor2 = new ApiOperationDescriptor(typeof(TestOperation2), "TestOperation2", "TestSource2");
        var url = new ApiOperationUrl("/test");
        descriptor1.AddUrl(url);
        descriptor2.AddUrl(url);
        model.Register(descriptor1);

        // Act
        var act = () => model.Register(descriptor2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot register URL '/test' for operation 'TestOperation2' as it is already registered by operation 'TestOperation1'.");
    }

    [Fact]
    public void Register_Should_Throw_Exception_When_Duplicate_Operation_Type()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor1 = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation1", "TestSource1");
        var descriptor2 = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation2", "TestSource2");
        model.Register(descriptor1);

        // Act
        var act = () => model.Register(descriptor2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot register operation 'TestOperation2' as an operation with type 'TestOperation' is already registered.");
    }

    [Fact]
    public void Register_Should_Add_Link_For_Operation()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        var link = new ApiOperationLink(descriptor, "/test", "test", "self", typeof(TestResource));
        descriptor.AddLink(link);

        // Act
        model.Register(descriptor);

        // Assert
        model.GetLinksForOperation(typeof(TestOperation)).Should().Contain(link);
    }

    [Fact]
    public void Register_Should_Throw_Exception_When_Duplicate_Link_To_Same_Resource_From_Different_Descriptors()
    {
        // Arrange
        var model = new BlueprintApp();
        var descriptor1 = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation1", "TestSource1");
        var descriptor2 = new ApiOperationDescriptor(typeof(TestOperation2), "TestOperation2", "TestSource2");
        var link1 = new ApiOperationLink(descriptor1, "/test", "test", "self", typeof(TestResource));
        var link2 = new ApiOperationLink(descriptor2, "/test", "test", "self", typeof(TestResource));
        descriptor1.AddLink(link1);
        descriptor2.AddLink(link2);
        model.Register(descriptor1);

        // Act
        var act = () => model.Register(descriptor2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate link found for resource type TestResource with rel self. Existing link is for the operation: \n\n Blueprint.SourceGenerator.Tests.Model.BlueprintAppTests+TestOperation: test\n\nEvery link must be a unique pairing of resource type (i.e. UserApiResource) and rel (i.e. \"self\" or \"activate\")");
    }

    private class TestOperation;

    private class TestOperation2;

    private class TestResource;
}
