using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Tests proving <see cref="LinkGeneratorHandler"/> recurses into a resource collection
/// (<see cref="IApiResourceCollection"/>) found nested inside an enumerated item, rather than
/// stopping at the outer enumeration. Reproduces Signature365 commit 286bcab22a.
/// </summary>
public class LinkGeneratorHandlerNestedCollectionTests
{
    [Fact]
    public async Task When_enumerated_item_is_itself_a_resource_collection_then_its_children_are_linked()
    {
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(NestedCollectionOperation), "NestedCollectionOperation", "test");
        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);

        var children = new[]
        {
            new ChildResource { Id = "child-1" },
            new ChildResource { Id = "child-2" },
        };

        // The top-level content is a plain enumerable whose sole item is itself a resource
        // collection (ListApiResource<ChildResource>) — the shape that regressed.
        var nestedCollection = new ListApiResource<ChildResource>(children);
        var content = new List<ListApiResource<ChildResource>> { nestedCollection };
        var result = new OkResult(content);

        await LinkGeneratorHandler.AddLinksAsync(
            new StubApiLinkGenerator(),
            [new ChildResourceLinkGenerator()],
            context,
            result);

        children[0].Links.Should().ContainKey("self", "children of a collection nested inside an enumerated item should be linked");
        children[0].Links["self"].Href.Should().Be("/children/child-1");
        children[1].Links["self"].Href.Should().Be("/children/child-2");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private sealed class ChildResourceLinkGenerator : ResourceLinkGenerator<ChildResource>
    {
        protected override ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ChildResource resource)
        {
            resource.AddLink("self", new Link { Href = $"/children/{resource.Id}" });
            return default;
        }
    }

    private sealed class StubApiLinkGenerator : IApiLinkGenerator
    {
        public Link CreateSelfLink<T>(object idDefinition, object? queryString = null) where T : ApiResource
        {
            return new() { Href = string.Empty };
        }

        public Link CreateSelfLink<T>(string id, object? queryString = null) where T : ApiResource
        {
            return new() { Href = string.Empty };
        }

        public Link CreateSelfLink<T>(int id, object? queryString = null) where T : ApiResource
        {
            return new() { Href = string.Empty };
        }

        public Link CreateSelfLink<T>(Guid id, object? queryString = null) where T : ApiResource
        {
            return new() { Href = string.Empty };
        }

        public string? CreateUrl(ApiOperationLink link, object? result = null)
        {
            return null;
        }

        public string CreateUrlTemplate(ApiOperationLink link)
        {
            return string.Empty;
        }

        public string CreateUrl(object operation)
        {
            return string.Empty;
        }
    }

    private class ChildResource : ApiResource
    {
        public string Id { get; set; } = string.Empty;

        public override string Object => "child";
    }

    private class NestedCollectionOperation
    {
    }
}
