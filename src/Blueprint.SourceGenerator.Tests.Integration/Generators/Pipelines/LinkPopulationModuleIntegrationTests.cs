using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class LinkPopulationModuleIntegrationTests
{
    [Fact]
    public async Task When_handler_returns_resource_event_result_and_self_link_is_declared_then_links_self_is_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(CreateLinkTestResourceCommand));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "res-123";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, CreateLinkTestResourceCommand.Configure(), CancellationToken.None)
        {
            Operation = new CreateLinkTestResourceCommand { Name = "Test" },
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<LinkTestApiResource>>();
        var resourceResult = (ResourceEventResult<LinkTestApiResource>)result;
        resourceResult.Content.Links.Should().ContainKey("self");
        resourceResult.Content.Links["self"].Href.Should().Be("/link-test-resources/res-123");
    }

    [Fact]
    public async Task When_handler_returns_ok_result_with_linkable_resource_and_self_link_declared_then_links_self_is_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetLinkTestResourceQuery));
        var context = CreateContext(app, GetLinkTestResourceQuery.Configure());
        context.Operation = new GetLinkTestResourceQuery { Id = "query-456" };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<LinkTestApiResource>>();
        var okResult = (OkResult<LinkTestApiResource>)result;
        okResult.Content.Links.Should().ContainKey("self");
        okResult.Content.Links["self"].Href.Should().Be("/link-test-resources/query-456");
    }

    [Fact]
    public async Task When_handler_returns_resource_without_self_link_declared_then_no_links_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetResourceWithoutSelfLinkQuery));
        var context = CreateContext(app, GetResourceWithoutSelfLinkQuery.Configure());
        context.Operation = new GetResourceWithoutSelfLinkQuery();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<ResourceWithoutSelfLink>>();
        var okResult = (OkResult<ResourceWithoutSelfLink>)result;
        okResult.Content.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task When_self_link_has_mapped_placeholder_then_resource_property_is_used_in_href()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetMappedLinkResourceQuery));
        var context = CreateContext(app, GetMappedLinkResourceQuery.Configure());
        context.Operation = new GetMappedLinkResourceQuery { Slug = "my-slug" };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<MappedLinkApiResource>>();
        var okResult = (OkResult<MappedLinkApiResource>)result;
        okResult.Content.Links.Should().ContainKey("self");
        okResult.Content.Links["self"].Href.Should().Be("/mapped-resources/mapped-id-99");
    }

    [Fact]
    public void When_self_link_has_placeholders_then_compiled_url_builder_is_set()
    {
        var descriptor = CreateLinkTestResourceCommand.Configure();

        var link = descriptor.Links.Single(static l => l.Rel == "self");
        link.CompiledUrlBuilder.Should().NotBeNull("links with placeholders should have a compiled URL builder");
    }

    [Fact]
    public void When_compiled_url_builder_is_set_then_creates_url_without_reflection()
    {
        var descriptor = CreateLinkTestResourceCommand.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        var resource = new LinkTestApiResource { Id = "abc-123", Name = "Test" };
        var url = link.CompiledUrlBuilder!(resource);

        url.Should().Be("/link-test-resources/abc-123");
    }

    [Fact]
    public void When_compiled_url_builder_receives_null_property_then_returns_null()
    {
        var descriptor = CreateLinkTestResourceCommand.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        var resource = new LinkTestApiResource { Id = null!, Name = "Test" };
        var url = link.CompiledUrlBuilder!(resource);

        url.Should().BeNull("null properties should cause compiled builder to return null");
    }

    [Fact]
    public void When_compiled_url_builder_with_mapped_placeholder_then_uses_resource_property()
    {
        var descriptor = GetMappedLinkResourceQuery.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        link.CompiledUrlBuilder.Should().NotBeNull();

        var resource = new MappedLinkApiResource { MappedId = "mapped-42", Title = "Test" };
        var url = link.CompiledUrlBuilder!(resource);

        url.Should().Be("/mapped-resources/mapped-42");
    }

    [Fact]
    public void When_compiled_url_builder_encodes_special_characters_then_uri_escaped()
    {
        var descriptor = CreateLinkTestResourceCommand.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        var resource = new LinkTestApiResource { Id = "has spaces/and+more", Name = "Test" };
        var url = link.CompiledUrlBuilder!(resource);

        url.Should().Be("/link-test-resources/has%20spaces%2Fand%2Bmore");
    }

    [Fact]
    public void When_compiled_url_builder_uses_catch_all_then_slashes_are_not_escaped()
    {
        var descriptor = GetCatchAllFolderResourceQuery.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        var resource = new CatchAllFolderApiResource { FolderPath = "UK/Marketing" };
        var url = link.CompiledUrlBuilder!(resource);

        url.Should().Be("/folder-link-test/UK/Marketing");
    }

    [Fact]
    public async Task When_self_link_uses_catch_all_then_links_self_preserves_path_separators()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(GetCatchAllFolderResourceQuery));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["FolderPath"] = "UK/Marketing";

        var context = new Blueprint.Runtime.Exec.ApiOperationContext(new EmptyServiceProvider(), app, GetCatchAllFolderResourceQuery.Configure(), CancellationToken.None)
        {
            Operation = new GetCatchAllFolderResourceQuery { FolderPath = "UK/Marketing" },
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<CatchAllFolderApiResource>>();
        var okResult = (OkResult<CatchAllFolderApiResource>)result;
        okResult.Content.Links.Should().ContainKey("self");
        okResult.Content.Links["self"].Href.Should().Be("/folder-link-test/UK/Marketing");
    }

    [Fact]
    public void When_CreateRelativeUrl_called_with_compiled_builder_then_uses_fast_path()
    {
        var descriptor = CreateLinkTestResourceCommand.Configure();
        var link = descriptor.Links.Single(static l => l.Rel == "self");

        var resource = new LinkTestApiResource { Id = "fast-path-42", Name = "Test" };

        // CreateRelativeUrl should delegate to CompiledUrlBuilder
        var url = link.CreateRelativeUrl(resource);

        url.Should().Be("/link-test-resources/fast-path-42");
    }

    [Fact]
    public async Task When_IApiLinkGenerator_registered_then_hateoas_link_generators_are_invoked()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(CreateLinkTestResourceCommand));
        var descriptor = CreateLinkTestResourceCommand.Configure();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApiLinkGenerator, StubApiLinkGenerator>();
        services.AddSingleton<IResourceLinkGenerator, TestResourceLinkGenerator>();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "res-123";

        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = new CreateLinkTestResourceCommand { Name = "Test" },
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<LinkTestApiResource>>();
        var resourceResult = (ResourceEventResult<LinkTestApiResource>)result;
        resourceResult.Content.Links.Should().ContainKey("self", "inline self-link should still be populated");
        resourceResult.Content.Links.Should().ContainKey("edit", "HATEOAS link generator should have added the edit link");
        resourceResult.Content.Links["edit"].Href.Should().Be("/link-test-resources/res-123/edit");
    }

    [Fact]
    public async Task When_no_IApiLinkGenerator_registered_then_hateoas_block_is_skipped()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(CreateLinkTestResourceCommand));
        var descriptor = CreateLinkTestResourceCommand.Configure();

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.RouteValues["Id"] = "res-123";

        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = new CreateLinkTestResourceCommand { Name = "Test" },
            HttpContext = httpContext,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<LinkTestApiResource>>();
        var resourceResult = (ResourceEventResult<LinkTestApiResource>)result;
        resourceResult.Content.Links.Should().ContainKey("self", "inline self-link should still work");
        resourceResult.Content.Links.Should().NotContainKey("edit", "no link generator registered so no HATEOAS links");
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None)
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

// --- Resource types ---

public partial class LinkTestApiResource : ApiResource
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;
}

public partial class ResourceWithoutSelfLink : ApiResource
{
    public string Value { get; set; } = "default";
}

public partial class MappedLinkApiResource : ApiResource
{
    public string MappedId { get; set; } = null!;

    public string Title { get; set; } = null!;
}

public partial class CatchAllFolderApiResource : ApiResource
{
    public string FolderPath { get; set; } = null!;
}

// --- Operations ---

/// <summary>
/// A command that creates a resource and returns it via ResourceEventResult.
/// The SelfLink declares how to build the $links.self href for this resource type.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(LinkTestApiResource), "/link-test-resources/{Id}")]
public partial class CreateLinkTestResourceCommand : ICommand<ResourceEventResult<LinkTestApiResource>>
{
    public required string Name { get; init; }

    public string Id { get; init; } = "res-123";

    public static Task<ResourceEventResult<LinkTestApiResource>> HandleAsync(CreateLinkTestResourceCommand operation)
    {
        var resource = new LinkTestApiResource { Id = operation.Id, Name = operation.Name };
        return Task.FromResult(new ResourceEventResult<LinkTestApiResource>(ChangeType.Created, "created", operation.Id, resource));
    }
}

/// <summary>
/// A query that returns the resource — self-link is declared by CreateLinkTestResourceCommand above,
/// and should be resolved cross-operation for this resource type.
/// </summary>
public partial class GetLinkTestResourceQuery : IQuery<LinkTestApiResource>
{
    public required string Id { get; init; }

    public static Task<LinkTestApiResource> HandleAsync(GetLinkTestResourceQuery operation)
    {
        return Task.FromResult(new LinkTestApiResource { Id = operation.Id, Name = "Fetched" });
    }
}

/// <summary>
/// A query that returns a resource type for which no SelfLink is declared anywhere.
/// </summary>
public partial class GetResourceWithoutSelfLinkQuery : IQuery<ResourceWithoutSelfLink>
{
    public static Task<ResourceWithoutSelfLink> HandleAsync()
    {
        return Task.FromResult(new ResourceWithoutSelfLink { Value = "no-links" });
    }
}

/// <summary>
/// Declares a self-link with a mapped placeholder: route uses Slug, resource uses MappedId.
/// </summary>
[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(MappedLinkApiResource), "/mapped-resources/{Slug:MappedId}")]
public partial class GetMappedLinkResourceQuery : IQuery<MappedLinkApiResource>
{
    public required string Slug { get; init; }

    public static Task<MappedLinkApiResource> HandleAsync(GetMappedLinkResourceQuery operation)
    {
        return Task.FromResult(new MappedLinkApiResource { MappedId = "mapped-id-99", Title = "Mapped" });
    }
}

[Blueprint.Model.Attributes.AllowAnonymous]
[Blueprint.Model.Attributes.SelfLink(typeof(CatchAllFolderApiResource), "/folder-link-test/{**FolderPath}")]
public partial class GetCatchAllFolderResourceQuery : IQuery<CatchAllFolderApiResource>
{
    public required string FolderPath { get; init; }

    public static Task<CatchAllFolderApiResource> HandleAsync(GetCatchAllFolderResourceQuery operation)
    {
        return Task.FromResult(new CatchAllFolderApiResource { FolderPath = operation.FolderPath });
    }
}

/// <summary>
/// A test <see cref="IResourceLinkGenerator"/> that adds an "edit" link to any
/// <see cref="LinkTestApiResource"/> — proving the HATEOAS link generation pipeline works.
/// </summary>
internal sealed class TestResourceLinkGenerator : IResourceLinkGenerator
{
    public ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, ILinkableResource linkableResource)
    {
        if (linkableResource is LinkTestApiResource resource)
        {
            resource.AddLink("edit", new Link { Href = $"/link-test-resources/{resource.Id}/edit" });
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Minimal <see cref="IApiLinkGenerator"/> stub that satisfies the DI requirement
/// without needing the full ApiLinkGenerator infrastructure.
/// </summary>
internal sealed class StubApiLinkGenerator : IApiLinkGenerator
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

    public string CreateUrl(object operation)
    {
        return string.Empty;
    }
}
