using System.Collections.Concurrent;
using System.Text;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the base URI the router gives a request is the one made from that request's own scheme, host and path
/// base, however many requests for other hosts it is routing at the same time.
/// </summary>
/// <remarks>
/// The base URI is what a request's links and resource event href are made under, and Signature365 stores the href with
/// every audited event. Legacy made it per request from the request itself (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/ApplicationBuilderExtensions.cs:232-234</c>), so a request never had another's.
/// </remarks>
public sealed class RouterBaseUriConcurrencyTests
{
    private const int RequestsPerWorker = 3000;

    [Fact]
    public async Task Concurrent_requests_for_different_hosts_each_have_links_made_under_their_own_host()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        await using var provider = services.BuildServiceProvider();

        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");
        var endpoint = endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static e => e.RoutePattern.RawText == "api/link-wire/widgets/{Id}"
                                && e.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
                                && methods.HttpMethods.Contains("GET"));

        var wrongHosts = new ConcurrentQueue<string>();

        // Each worker alternates between two hosts, so every request finds the router last saw another host.
        async Task RunAsync(string firstHost, string secondHost)
        {
            for (var i = 0; i < RequestsPerWorker; i++)
            {
                var host = i % 2 == 0 ? firstHost : secondHost;
                var body = await SendAsync(endpoint.RequestDelegate!, provider, host);

                if (!body.Contains($"\"http://{host}/api/link-wire/widgets/widget-1\"", StringComparison.Ordinal))
                {
                    wrongHosts.Enqueue($"{host}: {body}");
                }
            }
        }

        await Task.WhenAll(
            Task.Run(() => RunAsync("a.example.test", "b.example.test")),
            Task.Run(() => RunAsync("b.example.test", "c.example.test")),
            Task.Run(() => RunAsync("c.example.test", "d.example.test")),
            Task.Run(() => RunAsync("d.example.test", "a.example.test")));

        wrongHosts.Should().BeEmpty("every request's self link is made under its own host");
    }

    [Fact]
    public async Task A_request_whose_path_base_differs_only_by_case_has_links_made_under_its_own_path_base()
    {
        // Legacy wrote the request's own PathBase into every request's base URI (ApplicationBuilderExtensions.cs:232), so
        // a request under "/GATEWAY" had "/GATEWAY" links though the one before it was under "/gateway". PathString's own
        // equality ignores case.
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();
        await using var provider = services.BuildServiceProvider();
        var endpoint = FindWidgetEndpoint(provider);

        var first = await SendAsync(endpoint.RequestDelegate!, provider, "a.example.test", "/gateway");
        var second = await SendAsync(endpoint.RequestDelegate!, provider, "a.example.test", "/GATEWAY");

        first.Should().Contain("\"http://a.example.test/gateway/api/link-wire/widgets/widget-1\"");
        second.Should().Contain("\"http://a.example.test/GATEWAY/api/link-wire/widgets/widget-1\"", "the second request's links are made under its own path base");
    }

    private static RouteEndpoint FindWidgetEndpoint(IServiceProvider provider)
    {
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static e => e.RoutePattern.RawText == "api/link-wire/widgets/{Id}"
                                && e.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
                                && methods.HttpMethods.Contains("GET"));
    }

    private static async Task<string> SendAsync(RequestDelegate requestDelegate, IServiceProvider provider, string host, string pathBase = "")
    {
        await using var scope = provider.CreateAsyncScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.PathBase = pathBase;
        httpContext.Request.Path = "/api/link-wire/widgets/widget-1";
        httpContext.Request.RouteValues = new RouteValueDictionary { ["Id"] = "widget-1" };

        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await requestDelegate(httpContext);

        return Encoding.UTF8.GetString(responseBody.ToArray());
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
