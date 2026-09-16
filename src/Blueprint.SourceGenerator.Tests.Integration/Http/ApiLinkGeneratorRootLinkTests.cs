using Blueprint.Http;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Tests for <see cref="ApiLinkGenerator.CreateUrlTemplate"/>, proving that a link carrying
/// placeholders can be rendered as a URL template — reproducing Signature365 commit
/// <c>78de8b575e</c> ("Return URL templates for placeholder root links"), where a root link
/// (an operation link with no values available to fill its placeholders, such as the vCard
/// gateway mounted at "/") previously produced a broken/null URL rather than a usable template.
/// </summary>
public class ApiLinkGeneratorRootLinkTests
{
    [Fact]
    public void CreateUrlTemplate_with_placeholders_retains_them_in_the_template()
    {
        var (_, link) = CreateAppWithLink("tenants/{TenantId}/resources/{Id}");
        var generator = new ApiLinkGenerator(new BlueprintApp(), "https://api.example.com/");

        var template = generator.CreateUrlTemplate(link);

        template.Should().Be("https://api.example.com/tenants/{TenantId}/resources/{Id}");
    }

    [Fact]
    public void CreateUrlTemplate_without_placeholders_returns_url_as_is()
    {
        var (_, link) = CreateAppWithLink("all-resources");
        var generator = new ApiLinkGenerator(new BlueprintApp(), "https://api.example.com/");

        var template = generator.CreateUrlTemplate(link);

        template.Should().Be("https://api.example.com/all-resources");
    }

    [Fact]
    public void CreateUrlTemplate_uses_RoutingUrl_rather_than_UrlFormat()
    {
        // RoutingUrl has already normalised placeholders to the property name they represent
        // (e.g. {Id:UserId} => {Id}), whereas UrlFormat may still carry the original alternate
        // name or format. CreateUrlTemplate must prefer RoutingUrl so the template reflects the
        // name a client would substitute.
        var descriptor = new ApiOperationDescriptor(typeof(RootLinkTestOperation), "RootLinkTestOperation", "test");
        var link = new ApiOperationLink(descriptor, "resources/{Id}", "resources/{Id:UserId}", "self", typeof(RootLinkTestResource));
        var generator = new ApiLinkGenerator(new BlueprintApp(), "https://api.example.com/");

        var template = generator.CreateUrlTemplate(link);

        template.Should().Be("https://api.example.com/resources/{Id}");
    }

    /// <summary>
    /// This is the defect this framework expresses: a root link — one for which there is no
    /// values object available to fill its placeholders — produces a null href via <see cref="ApiLinkGenerator.CreateUrl(ApiOperationLink, object)"/>
    /// rather than the exception legacy Blueprint threw. <see cref="ApiLinkGenerator.CreateUrlTemplate"/> is the fix:
    /// callers of a placeholder root link should call it instead of <c>CreateUrl(link)</c> with a null values object.
    /// </summary>
    [Fact]
    public void CreateUrl_with_placeholders_and_no_values_returns_null_whereas_CreateUrlTemplate_succeeds()
    {
        var (_, link) = CreateAppWithLink("resources/{Id}");
        var generator = new ApiLinkGenerator(new BlueprintApp(), "https://api.example.com/");

        generator.CreateUrl(link).Should().BeNull();
        generator.CreateUrlTemplate(link).Should().Be("https://api.example.com/resources/{Id}");
    }

    private static (ApiOperationDescriptor Descriptor, ApiOperationLink Link) CreateAppWithLink(string urlFormat)
    {
        var descriptor = new ApiOperationDescriptor(typeof(RootLinkTestOperation), "RootLinkTestOperation", "test");
        var link = new ApiOperationLink(descriptor, urlFormat, urlFormat, "self", typeof(RootLinkTestResource));
        descriptor.AddLink(link);

        return (descriptor, link);
    }

    private class RootLinkTestResource : ApiResource
    {
        public string Id { get; set; } = string.Empty;

        public override string Object => "rootLinkTest";
    }

    private class RootLinkTestOperation
    {
        public string Id { get; set; } = string.Empty;
    }
}
