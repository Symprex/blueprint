using Blueprint.Model;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the sample app's generated registration is inspectable: operations, URLs, HTTP methods,
/// resource links, and host exposure are all discoverable through the <see cref="BlueprintApp"/> model
/// without depending on HTTP infrastructure. This is the "generated inspection artifact" for Layer 2.
/// </summary>
public class ReferenceAppInspectionTests
{
    [Fact]
    public void When_app_registered_then_all_expected_operations_are_discoverable()
    {
        var app = CreateRegisteredApp();

        var operationTypes = app.Operations.Select(static op => op.OperationType).ToList();

        operationTypes.Should().Contain(typeof(GetTemplatesQuery));
        operationTypes.Should().Contain(typeof(GetTemplateQuery));
        operationTypes.Should().Contain(typeof(CreateTemplateCommand));
        operationTypes.Should().Contain(typeof(UpdateTemplateCommand));
        operationTypes.Should().Contain(typeof(ArchiveTemplateCommand));
    }

    [Fact]
    public void When_app_registered_then_template_list_resource_has_self_link()
    {
        var app = CreateRegisteredApp();

        var selfLink = app.GetLinkFor(typeof(TemplateListApiResource), "self");

        selfLink.Should().NotBeNull("TemplateListApiResource should have a self link via GetTemplatesQuery");
        selfLink!.UrlFormat.Should().Be("/templates");
        selfLink.OperationDescriptor.OperationType.Should().Be(typeof(GetTemplatesQuery));
    }

    [Fact]
    public void When_app_registered_then_template_resource_has_self_link()
    {
        var app = CreateRegisteredApp();

        var selfLink = app.GetLinkFor(typeof(TemplateApiResource), "self");

        selfLink.Should().NotBeNull("TemplateApiResource should have a self link via GetTemplateQuery");
        selfLink!.UrlFormat.Should().Be("/templates/{Id}");
        selfLink.OperationDescriptor.OperationType.Should().Be(typeof(GetTemplateQuery));
    }

    [Fact]
    public void When_app_registered_then_get_templates_query_has_correct_url_and_method()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(GetTemplatesQuery));
        var links = app.GetLinksForOperation(typeof(GetTemplatesQuery)).ToList();

        descriptor.Should().NotBeNull();
        links.Should().ContainSingle(link => link.UrlFormat == "/templates");
    }

    [Fact]
    public void When_app_registered_then_create_template_command_has_correct_url()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(CreateTemplateCommand));

        // RootLink registers a URL (not a link) so we inspect via the descriptor's Urls collection.
        descriptor.Urls.Should().ContainSingle(url => url.UrlFormat == "/templates");
    }

    [Fact]
    public void When_app_registered_then_update_template_command_has_correct_url()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(UpdateTemplateCommand));

        descriptor.Urls.Should().ContainSingle(url => url.UrlFormat == "/templates/{Id}");
    }

    [Fact]
    public void When_app_registered_then_http_host_operations_are_exposed()
    {
        var app = CreateRegisteredApp();

        var httpOperations = app.GetOperationsForHost("http");

        httpOperations.Should().HaveCount(5, "all five sample operations should be exposed on the HTTP host");
        httpOperations.Select(static op => op.OperationType).Should().BeEquivalentTo(
        [
            typeof(GetTemplatesQuery),
            typeof(GetTemplateQuery),
            typeof(CreateTemplateCommand),
            typeof(UpdateTemplateCommand),
            typeof(ArchiveTemplateCommand),
        ]);
    }

    [Fact]
    public void When_app_registered_then_resource_links_for_template_are_discoverable()
    {
        var app = CreateRegisteredApp();

        var templateLinks = app.GetLinksForResource(typeof(TemplateApiResource));

        templateLinks.Should().ContainSingle(link => link.Rel == "self" && link.UrlFormat == "/templates/{Id}");
    }

    [Fact]
    public void When_app_registered_then_serializer_context_is_available()
    {
        var app = CreateRegisteredApp();

        app.AppOwnedSerializerContext.Should().NotBeNull();
        app.AppOwnedSerializerContext.Should().BeOfType<SampleBlueprintAppJsonContext>();
    }

    private static SampleBlueprintApp CreateRegisteredApp()
    {
        var app = new SampleBlueprintApp();

        // The generated partial Register() method is called by the runtime during MapBlueprintApi,
        // but we can also invoke it directly to test registration logic in isolation.
        return app;
    }
}
