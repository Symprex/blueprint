using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the <c>{basePath}openapi</c> route legacy Blueprint served from <c>OpenApiQuery</c> (Signature365
/// <c>backend/src/Blueprint/Blueprint.OpenApi/OpenApiQuery.cs</c>, <c>[RootLink("/openapi")]</c>, anonymous): the OpenAPI
/// document as JSON, or, for a browser, a Scalar API reference page that loads that document relative to itself.
/// Consumers still call this address: Signature365's gateway availability monitors request <c>/api/openapi</c> and expect
/// a 200, and its documentation pages are reached there.
/// </summary>
public class OpenApiDocumentationRouteTests
{
    [Fact]
    public async Task When_openapi_route_requested_without_accepting_html_then_serves_the_json_document()
    {
        var (body, response) = await InvokeAsync("api/openapi", accept: "application/json");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().StartWith("application/json");

        var (documentBody, _) = await InvokeAsync("api/.blueprint/openapi.json", accept: "application/json");
        body.Should().Be(documentBody, "legacy served the same document at openapi that this framework serves at .blueprint/openapi.json");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task When_browser_requests_openapi_route_then_serves_the_scalar_page_loading_the_document_relative_to_itself()
    {
        var (body, response) = await InvokeAsync("api/openapi", accept: "text/html,application/xhtml+xml", title: "Sample API");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().StartWith("text/html");

        body.Should().Contain("<title>Sample API</title>");
        body.Should().Contain("https://cdn.jsdelivr.net/npm/@scalar/api-reference");
        body.Should().Contain("window.location.pathname + '?json'", "the page must load the document relative to itself, so it resolves on every host and gateway prefix");
        body.Should().NotContain("proxy.scalar.com", "a same-origin document needs no proxy");
    }

    [Fact]
    public async Task When_browser_requests_openapi_route_with_json_query_then_serves_the_json_document()
    {
        var (body, response) = await InvokeAsync("api/openapi", accept: "text/html", queryString: "?json");

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().StartWith("application/json", "?json is how the documentation page asks for the document");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task When_api_title_contains_markup_then_the_page_encodes_it()
    {
        var (body, _) = await InvokeAsync("api/openapi", accept: "text/html", title: "Orders & <Billing>");

        body.Should().Contain("<title>Orders &amp; &lt;Billing&gt;</title>");
    }

    private static async Task<(string Body, HttpResponse Response)> InvokeAsync(
        string routePattern,
        string accept,
        string queryString = "",
        string? title = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
            "api/",
            configureOpenApi: title is null ? null : options => options.Title = title);

        var endpoint = endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal)
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } meta
                && meta.HttpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase));

        endpoint.Should().NotBeNull($"a GET endpoint should be registered at {routePattern}");

        var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/" + routePattern;
        context.Request.QueryString = new QueryString(queryString.Length == 0 ? null : queryString);
        context.Request.Headers.Accept = accept;

        await endpoint!.RequestDelegate!(context);
        body.Position = 0;

        return (await new StreamReader(body).ReadToEndAsync(), context.Response);
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }
}
