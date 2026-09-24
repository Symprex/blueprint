using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that <see cref="BlueprintHttpOptions.PublicApiHost" />, once registered via DI and resolved by
/// <c>MapBlueprintApi</c>, actually reaches the OpenAPI document served by the real endpoint — not merely
/// the <c>OpenApiOutputRenderingContext.Create</c> seam that Task 10 proved directly. Without this
/// wiring the option is inert: <c>MapBlueprintApi</c> resolves <see cref="BlueprintHttpOptions" /> for
/// routing but never hands it to <see cref="OpenApiDocumentEndpoint.Create" />.
/// </summary>
public class OpenApiServerFromHttpOptionsEndpointTests
{
    [Fact]
    public async Task When_public_api_host_configured_in_di_then_served_document_first_server_is_that_address()
    {
        var (json, statusCode) = await InvokeOpenApiEndpointAsync(
            new BlueprintHttpOptions { PublicApiHost = "gateway.example.com/import" });

        statusCode.Should().Be(StatusCodes.Status200OK);

        using var doc = JsonDocument.Parse(json);
        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();

        servers.Should().HaveCount(1);
        var expectedUrl = "https://gateway.example.com/import/api/";
        servers[0].GetProperty("url").GetString().Should().Be(expectedUrl, "PublicApiHost registered via DI must reach the document rendered by the real endpoint");
    }

    [Fact]
    public async Task When_public_api_host_configured_then_first_server_is_that_address_with_the_mapped_base_path()
    {
        // As legacy composed it: the gateway authority and prefix the application never sees, then the base path the
        // API is mapped at. The paths stay relative to that server, as every legacy document's were.
        var (json, _) = await InvokeOpenApiEndpointAsync(new BlueprintHttpOptions { PublicApiHost = "gateway.example.com/import" });

        using var doc = JsonDocument.Parse(json);
        var servers = doc.RootElement.GetProperty("servers").EnumerateArray().ToArray();

        servers.Should().HaveCount(1);
        servers[0].GetProperty("url").GetString().Should().Be("https://gateway.example.com/import/api/");

        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(static p => p.Name).ToArray();
        paths.Should().Contain("/templates/{Id}", "a path is relative to the server, which already carries the base path");
        paths.Should().NotContain(static p => p.StartsWith("/api/", StringComparison.Ordinal), "the base path would then be in the address twice");
    }

    [Fact]
    public async Task When_no_public_api_host_configured_then_served_document_omits_servers_section()
    {
        var (json, _) = await InvokeOpenApiEndpointAsync(new BlueprintHttpOptions());

        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "servers should stay omitted when the DI-registered options carry no host");
    }

    [Fact]
    public async Task When_only_public_host_configured_in_di_then_served_document_omits_servers_section()
    {
        // PublicHost is a bare host for link generation, not the complete address a servers entry has to be.
        // OpenApiServerDefaultTests proves this at the OpenApiOutputRenderingContext.Create seam directly; this
        // proves the same option, once resolved by DI and handed to the real MapBlueprintApi-registered endpoint,
        // does not leak through as a servers entry either — the wiring, not just the renderer, has to respect it.
        var (json, _) = await InvokeOpenApiEndpointAsync(new BlueprintHttpOptions { PublicHost = "internal.example.com" });

        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("servers", out _).Should().BeFalse(
            "PublicHost is not a complete public address and must not stand in for PublicApiHost, even once resolved by the real endpoint");
    }

    private static async Task<(string Json, int StatusCode)> InvokeOpenApiEndpointAsync(BlueprintHttpOptions httpOptions)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TemplateStore>()
            .AddSingleton<SampleBlueprintApp>()
            .AddSingleton<SampleBlueprintAppExecutor>()
            .AddSingleton<IOptions<BlueprintHttpOptions>>(Options.Create(httpOptions));

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());
        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");

        var endpoint = endpoints.DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(endpoint =>
                string.Equals(endpoint.RoutePattern.RawText, "api/.blueprint/openapi.json", StringComparison.Ordinal)
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } meta
                && meta.HttpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase));

        endpoint.Should().NotBeNull("the OpenAPI endpoint should be registered");

        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = endpoints.ServiceProvider,
            Response = { Body = body },
        };

        context.SetEndpoint(endpoint);
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/.blueprint/openapi.json";

        await endpoint!.RequestDelegate!(context);
        body.Position = 0;

        var json = await new StreamReader(body).ReadToEndAsync();
        return (json, context.Response.StatusCode);
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
