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
/// Proves that the RFC 9727 api-catalog document and its <c>Link</c> header are the ones made from the request's own
/// scheme, host and path base, however many requests for other hosts it answers at the same time.
/// </summary>
/// <remarks>
/// Not legacy-derived: legacy had no api-catalog endpoint. The endpoint caches what it made for the last request, as the
/// router caches its base URI, whose cache <c>RouterBaseUriConcurrencyTests</c> proves the same of.
/// </remarks>
public sealed class ApiCatalogConcurrencyTests
{
    private const int RequestsPerWorker = 3000;

    [Fact]
    public async Task Concurrent_requests_for_different_hosts_each_have_a_catalog_made_under_their_own_host()
    {
        await using var provider = CreateProvider();
        var requestDelegate = FindApiCatalogDelegate(provider);
        var wrongHosts = new ConcurrentQueue<string>();

        // Each worker alternates between two hosts, so every request finds the endpoint last answered another host.
        async Task RunAsync(string firstHost, string secondHost)
        {
            for (var i = 0; i < RequestsPerWorker; i++)
            {
                var host = i % 2 == 0 ? firstHost : secondHost;
                var (body, link) = await SendAsync(requestDelegate, host);

                if (!body.Contains($"\"anchor\": \"http://{host}/\"", StringComparison.Ordinal)
                    || !body.Contains($"\"href\": \"http://{host}/api/.blueprint/openapi.json\"", StringComparison.Ordinal)
                    || !link.StartsWith($"<http://{host}/api/.blueprint/openapi.json>", StringComparison.Ordinal))
                {
                    wrongHosts.Enqueue($"{host}: {link} {body}");
                }
            }
        }

        await Task.WhenAll(
            Task.Run(() => RunAsync("a.example.test", "b.example.test")),
            Task.Run(() => RunAsync("b.example.test", "c.example.test")),
            Task.Run(() => RunAsync("c.example.test", "d.example.test")),
            Task.Run(() => RunAsync("d.example.test", "a.example.test")));

        wrongHosts.Should().BeEmpty("every catalog is made under its own request's host");
    }

    [Fact]
    public async Task A_request_whose_path_base_differs_only_by_case_has_a_catalog_made_under_its_own_path_base()
    {
        // PathString's own equality ignores case, so the endpoint took "/GATEWAY" for the "/gateway" it had cached.
        await using var provider = CreateProvider();
        var requestDelegate = FindApiCatalogDelegate(provider);

        var (first, _) = await SendAsync(requestDelegate, "a.example.test", "/gateway");
        var (second, secondLink) = await SendAsync(requestDelegate, "a.example.test", "/GATEWAY");

        first.Should().Contain("\"anchor\": \"http://a.example.test/gateway/\"");
        second.Should().Contain("\"anchor\": \"http://a.example.test/GATEWAY/\"", "the second catalog is made under its own path base");
        secondLink.Should().StartWith("<http://a.example.test/GATEWAY/api/.blueprint/openapi.json>");
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

        return services.BuildServiceProvider();
    }

    private static RequestDelegate FindApiCatalogDelegate(IServiceProvider provider)
    {
        var endpoints = new TestEndpointRouteBuilder(provider);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        return endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(static e => e.RoutePattern.RawText == "/.well-known/api-catalog")
            .RequestDelegate!;
    }

    private static async Task<(string Body, string Link)> SendAsync(RequestDelegate requestDelegate, string host, string pathBase = "")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.PathBase = pathBase;
        httpContext.Request.Path = "/.well-known/api-catalog";

        using var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        await requestDelegate(httpContext);

        return (Encoding.UTF8.GetString(responseBody.ToArray()), httpContext.Response.Headers.Link.ToString());
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
