using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Tests for <see cref="ApiLinkGenerator"/> and <see cref="ResourceLinkGenerator{T}"/>
/// proving runtime URL construction from registered links.
/// </summary>
public class ApiLinkGeneratorTests
{
    [Fact]
    public void CreateUrl_with_link_and_values_produces_correct_url()
    {
        var (app, link) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var url = generator.CreateUrl(link, new { Id = "abc-123" });

        url.Should().Be("https://api.example.com/resources/abc-123");
    }

    [Fact]
    public void CreateUrl_without_placeholders_returns_base_plus_format()
    {
        var (app, link) = CreateAppWithLink("all-resources");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var url = generator.CreateUrl(link);

        url.Should().Be("https://api.example.com/all-resources");
    }

    [Fact]
    public void CreateUrl_with_missing_placeholder_value_returns_null()
    {
        var (app, link) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var url = generator.CreateUrl(link, new { Name = "wrong-property" });

        url.Should().BeNull();
    }

    [Fact]
    public void CreateUrl_from_operation_instance_uses_first_link()
    {
        var (app, _) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var operation = new LinkGenTestOperation { Id = "op-id" };
        var url = generator.CreateUrl(operation);

        url.Should().Be("https://api.example.com/resources/op-id");
    }

    [Fact]
    public void CreateUrl_from_operation_throws_when_no_links()
    {
        var app = new BlueprintApp();
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var act = () => generator.CreateUrl(new LinkGenTestOperation { Id = "x" });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*LinkGenTestOperation*");
    }

    [Fact]
    public void Base_uri_without_trailing_slash_still_works()
    {
        var (app, link) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com");

        var url = generator.CreateUrl(link, new { Id = "test" });

        url.Should().Be("https://api.example.com/resources/test");
    }

    [Fact]
    public void CreateUrl_with_multiple_placeholders_fills_all()
    {
        var (app, link) = CreateAppWithLink("tenants/{TenantId}/resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var url = generator.CreateUrl(link, new { TenantId = "t1", Id = "r2" });

        url.Should().Be("https://api.example.com/tenants/t1/resources/r2");
    }

    [Fact]
    public void CreateUrl_escapes_special_characters_in_values()
    {
        var (app, link) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(app, "https://api.example.com/");

        var url = generator.CreateUrl(link, new { Id = "hello world/test" });

        url.Should().Be("https://api.example.com/resources/hello%20world%2Ftest");
    }

    [Fact]
    public async Task ResourceLinkGenerator_only_processes_matching_type()
    {
        var linkGen = new TestResourceLinkGenerator();
        var (app, _) = CreateAppWithLink("resources/{Id}");
        var apiLinkGenerator = new ApiLinkGenerator(app, "https://api.example.com/");

        var matchingResource = new LinkGenTestResource { Id = "1" };
        var nonMatchingResource = new LinkGenOtherResource();

        await linkGen.AddLinksAsync(apiLinkGenerator, null!, matchingResource);
        await linkGen.AddLinksAsync(apiLinkGenerator, null!, nonMatchingResource);

        matchingResource.Links.Should().ContainKey("custom");
        nonMatchingResource.Links.Should().BeEmpty();
    }

    [Fact]
    public void HasPlaceholders_returns_true_when_url_has_braces()
    {
        var (_, link) = CreateAppWithLink("resources/{Id}");
        link.HasPlaceholders().Should().BeTrue();
    }

    [Fact]
    public void HasPlaceholders_returns_false_when_url_is_static()
    {
        var (_, link) = CreateAppWithLink("all-resources");
        link.HasPlaceholders().Should().BeFalse();
    }

    [Fact]
    public void CreateRelativeUrl_with_null_values_on_placeholder_link_returns_null()
    {
        var (_, link) = CreateAppWithLink("resources/{Id}");
        link.CreateRelativeUrl(null).Should().BeNull();
    }

    [Fact]
    public void CreateRelativeUrl_without_placeholders_returns_format()
    {
        var (_, link) = CreateAppWithLink("all-resources");
        link.CreateRelativeUrl(null).Should().Be("all-resources");
    }

    private static (BlueprintApp App, ApiOperationLink Link) CreateAppWithLink(string urlFormat)
    {
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(LinkGenTestOperation), "LinkGenTestOperation", "test");
        var link = new ApiOperationLink(descriptor, urlFormat, urlFormat, "self", typeof(LinkGenTestResource));
        descriptor.AddLink(link);
        app.Register(descriptor);

        return (app, link);
    }

    private class LinkGenTestResource : ILinkableResource
    {
        private readonly Dictionary<string, Link> _links = new();

        public string Id { get; set; } = string.Empty;

        public IDictionary<string, Link> Links => this._links;

        public void AddLink(string rel, Link link)
        {
            this._links[rel] = link;
        }
    }

    private class LinkGenOtherResource : ILinkableResource
    {
        private readonly Dictionary<string, Link> _links = new();

        public IDictionary<string, Link> Links => this._links;

        public void AddLink(string rel, Link link)
        {
            this._links[rel] = link;
        }
    }

    private class LinkGenTestOperation
    {
        public string Id { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;
    }

    private class TestResourceLinkGenerator : ResourceLinkGenerator<LinkGenTestResource>
    {
        protected override ValueTask AddLinksAsync(IApiLinkGenerator apiLinkGenerator, ApiOperationContext context, LinkGenTestResource resource)
        {
            resource.AddLink("custom", new Link { Href = "https://custom.com/" + resource.Id });
            return default;
        }
    }
}
