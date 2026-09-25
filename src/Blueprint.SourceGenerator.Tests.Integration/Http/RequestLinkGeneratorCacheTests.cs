using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the link generator a request's links are made with is its own app's, however many apps share the process
/// and whatever base URI they share.
/// </summary>
/// <remarks>
/// Legacy made each request's <c>ApiLinkGenerator</c> from the app's own <c>ApiDataModel</c> (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint.Http/ApiLinkGenerator.cs:24-31</c>), so its <c>CreateSelfLink</c> and
/// <c>CreateUrl(operation)</c> found the links of the app the request was for. <c>Blueprint.Testing</c>'s host
/// gives every app the same base URI string, so two apps tested in one process share it by reference.
/// </remarks>
public sealed class RequestLinkGeneratorCacheTests
{
    [Fact]
    public void Two_apps_sharing_a_base_uri_instance_each_get_a_link_generator_for_their_own_app()
    {
        var baseUri = "https://api.blueprint-testing.com/api/";
        var integrationApp = new IntegrationTestBlueprintApp();
        var sampleApp = new SampleBlueprintApp();

        var integrationGenerator = PipelineHelpers.GetRequestLinkGenerator(CreateContext(integrationApp, typeof(GetHrefWidgetQuery), baseUri));
        var sampleGenerator = PipelineHelpers.GetRequestLinkGenerator(CreateContext(sampleApp, typeof(GetTemplateQuery), baseUri));

        integrationGenerator!.CreateSelfLink<HrefWidgetApiResource>("widget-1").Href.Should().Be(
            "https://api.blueprint-testing.com/api/href-events/widgets/widget-1");
        sampleGenerator!.CreateSelfLink<TemplateApiResource>("t-1").Href.Should().Be(
            "https://api.blueprint-testing.com/api/templates/t-1",
            "the sample app's request makes its links from the sample app, not the app the generator was first made for");
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, Type operationType, string baseUri)
    {
        return new ApiOperationContext(NullServiceProvider.Instance, app, app.FindOperation(operationType)!, CancellationToken.None)
        {
            BaseUri = baseUri,
        };
    }
}
