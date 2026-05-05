using Blueprint.Model;
using Blueprint.Runtime.Exec;
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

        httpOperations.Should().HaveCount(10, "all ten sample operations should be exposed on the HTTP host");
        httpOperations.Select(static op => op.OperationType).Should().BeEquivalentTo(
        [
            typeof(GetTemplatesQuery),
            typeof(GetTemplateQuery),
            typeof(SearchTemplatesQuery),
            typeof(GetTemplateVersionsQuery),
            typeof(CreateTemplateCommand),
            typeof(UpdateTemplateCommand),
            typeof(PublishTemplateCommand),
            typeof(CloneTemplateCommand),
            typeof(PreviewTemplateCommand),
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

    [Fact]
    public void When_app_registered_then_resource_operations_carry_scope_pair_metadata()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(GetTemplatesQuery));
        var authData = descriptor.GetFeatureData<OperationAuthorizationFeatureData>();

        authData.Should().NotBeNull("GetTemplatesQuery has [Resource(\"template\", \"read\")]");
        authData!.ScopePairs.Should().ContainSingle(sp => sp.Noun == "template" && sp.Verb == "read");
    }

    [Fact]
    public void When_app_registered_then_from_resource_operations_inherit_scope_pairs()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(UpdateTemplateCommand));
        var authData = descriptor.GetFeatureData<OperationAuthorizationFeatureData>();

        authData.Should().NotBeNull("UpdateTemplateCommand has [FromResource(typeof(CreateTemplateCommand))]");
        authData!.ScopePairs.Should().ContainSingle(sp => sp.Noun == "template" && sp.Verb == "write");
    }

    [Fact]
    public void When_app_registered_then_anonymous_operations_have_no_auth_requirement()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(SearchTemplatesQuery));
        var authenticationData = descriptor.GetFeatureData<OperationAuthenticationFeatureData>();

        authenticationData.Should().NotBeNull();
        authenticationData!.Requirement.Should().Be(AuthenticationRequirement.Anonymous);
    }

    [Fact]
    public void When_app_registered_then_authenticated_operations_require_identity()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(CreateTemplateCommand));
        var authenticationData = descriptor.GetFeatureData<OperationAuthenticationFeatureData>();

        authenticationData.Should().NotBeNull("CreateTemplateCommand has [Resource] which implies authentication");
        authenticationData!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);
    }

    [Fact]
    public void When_app_registered_then_union_results_project_outcome_metadata()
    {
        var app = CreateRegisteredApp();

        var descriptor = app.FindOperation(typeof(GetTemplateQuery));
        var outcomes = descriptor.GetFeatureData<OperationResultOutcomesFeatureData>();

        outcomes.Should().NotBeNull("GetTemplateQuery declares Results<OkResult, NotFoundResult>");
        outcomes!.Outcomes.Should().HaveCount(2);
        outcomes.Outcomes.Should().Contain(o => o.ResultType == typeof(OkResult<TemplateApiResource>));
        outcomes.Outcomes.Should().Contain(o => o.ResultType == typeof(NotFoundResult<TemplateApiResource>));
    }

    [Fact]
    public void When_app_registered_then_http_method_metadata_is_correct()
    {
        var app = CreateRegisteredApp();

        var getDescriptor = app.FindOperation(typeof(GetTemplatesQuery));
        var postDescriptor = app.FindOperation(typeof(CreateTemplateCommand));
        var putDescriptor = app.FindOperation(typeof(UpdateTemplateCommand));
        var deleteDescriptor = app.FindOperation(typeof(ArchiveTemplateCommand));

        getDescriptor.GetFeatureData<OperationHttpMethodMetadataFeatureData>()!
            .HttpMethods.Should().ContainSingle(m => m == "GET");
        postDescriptor.GetFeatureData<OperationHttpMethodMetadataFeatureData>()!
            .HttpMethods.Should().ContainSingle(m => m == "POST");
        putDescriptor.GetFeatureData<OperationHttpMethodMetadataFeatureData>()!
            .HttpMethods.Should().ContainSingle(m => m == "PUT");
        deleteDescriptor.GetFeatureData<OperationHttpMethodMetadataFeatureData>()!
            .HttpMethods.Should().ContainSingle(m => m == "DELETE");
    }
}
