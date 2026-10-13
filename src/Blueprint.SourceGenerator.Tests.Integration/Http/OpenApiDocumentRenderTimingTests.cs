using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves when the OpenAPI document is rendered: as the API is mapped by default, so a document that cannot be rendered
/// fails the host's startup, or on first use when <see cref="BlueprintHttpOptions.RenderOpenApiDocumentOnFirstRequest" />
/// is set.
/// </summary>
/// <remarks>
/// Rendering is most of what mapping a large API costs. A test suite that builds a host per test pays it for every host,
/// whether or not a test reads the document: building endpoints took 13% of the sampled thread time of Signature365's
/// Portal test shard, against 22% for running the test methods themselves.
/// </remarks>
public sealed class OpenApiDocumentRenderTimingTests
{
    [Fact]
    public void When_mapped_by_default_then_the_document_is_rendered_at_once()
    {
        var renders = 0;

        using var provider = CreateProvider(renderOnFirstRequest: false);
        MapApi(provider, () => renders++);

        renders.Should().BeGreaterThan(0, "the document is rendered as the API is mapped");
    }

    [Fact]
    public async Task When_rendering_on_first_request_then_the_document_is_rendered_once_when_first_requested()
    {
        var renders = 0;

        using var provider = CreateProvider(renderOnFirstRequest: true);
        var openApi = MapApi(provider, () => renders++);

        renders.Should().Be(0, "nothing has asked for the document yet");

        var first = await SendAsync(openApi);
        var rendersAfterFirst = renders;
        var second = await SendAsync(openApi);

        rendersAfterFirst.Should().BeGreaterThan(0);
        renders.Should().Be(rendersAfterFirst, "the document is rendered once and then served as rendered");
        first.StatusCode.Should().Be(StatusCodes.Status200OK);
        second.Body.Should().Be(first.Body);
        first.Body.Should().Contain("\"openapi\"");
    }

    [Fact]
    public void When_rendering_on_first_request_then_reading_the_held_document_renders_it()
    {
        // OutputOpenApi reads the holder to write the committed spec, so it must get the document whether or not a
        // request has rendered it.
        var renders = 0;

        using var provider = CreateProvider(renderOnFirstRequest: true);
        MapApi(provider, () => renders++);

        var json = provider.GetRequiredService<PreRenderedOpenApiDocumentHolder>().Document?.Json;

        json.Should().Contain("\"openapi\"");
        renders.Should().BeGreaterThan(0);
    }

    private static ServiceProvider CreateProvider(bool renderOnFirstRequest)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());
        services.Configure<BlueprintHttpOptions>(o => o.RenderOpenApiDocumentOnFirstRequest = renderOnFirstRequest);

        return services.BuildServiceProvider();
    }

    private static RequestDelegate MapApi(IServiceProvider provider, Action onSchemaRendered)
    {
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            "api/",
            configureOpenApiSchemas: o => o.Processors.Add(_ => onSchemaRendered()));

        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static e => e.RoutePattern.RawText == "/.well-known/openapi")
            .RequestDelegate!;
    }

    private static async Task<(int StatusCode, string Body)> SendAsync(RequestDelegate openApi)
    {
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { Response = { Body = body } };
        httpContext.Request.Method = HttpMethods.Get;

        await openApi(httpContext);

        return (httpContext.Response.StatusCode, System.Text.Encoding.UTF8.GetString(body.ToArray()));
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
