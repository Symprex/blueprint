using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class ApiOperationDescriptorTests
{
    [Fact]
    public void AddUrl_Should_Add_Url()
    {
        // Arrange
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        var url = new ApiOperationUrl("/test");

        // Act
        descriptor.AddUrl(url);

        // Assert
        descriptor.Urls.Should().Contain(url);
    }

    [Fact]
    public void AddUrl_Should_Throw_Exception_When_DuplicateUrl()
    {
        // Arrange
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        var url1 = new ApiOperationUrl("/test");
        var url2 = new ApiOperationUrl("/test");
        descriptor.AddUrl(url1);

        // Act
        var act = () => descriptor.AddUrl(url2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot add a URL with the same URL format as an existing URL. Tried to add /test");
    }

    [Fact]
    public void AddLink_Should_Throw_Exception_When_Duplicate_Link_For_Same_Resource()
    {
        // Arrange
        var descriptor = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation", "TestSource");
        var link1 = new ApiOperationLink(descriptor, "/test", "test", "self", typeof(TestResource));
        var link2 = new ApiOperationLink(descriptor, "/test", "test", "self", typeof(TestResource));
        descriptor.AddLink(link1);

        // Act
        var act = () => descriptor.AddLink(link2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Could not register TestResource#self => TestOperation from TestSource as it conflicts with existing Link registrations:\n\n TestResource#self => TestOperation sourced from TestSource");
    }

    [Fact]
    public void AddLink_Should_Throw_Exception_When_Link_Not_Created_For_Descriptor()
    {
        // Arrange
        var descriptor1 = new ApiOperationDescriptor(typeof(TestOperation), "TestOperation1", "TestSource1");
        var descriptor2 = new ApiOperationDescriptor(typeof(TestOperation2), "TestOperation2", "TestSource2");

        var link = new ApiOperationLink(descriptor2, "/test", "test", "self", typeof(TestResource));

        // Act
        var act = () => descriptor1.AddLink(link);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("The ApiOperationLink MUST have been created for this ApiOperationDescriptor, but was instead created for the description TestOperation2");
    }

    private class TestOperation;

    private class TestOperation2;

    private class TestResource;
}
