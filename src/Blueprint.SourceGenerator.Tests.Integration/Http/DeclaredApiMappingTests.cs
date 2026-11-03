using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an API's base path and OpenAPI description can be declared with <c>AddBlueprintApi</c>, which is all the
/// <c>BlueprintEmitOpenApi</c> build target reads, and that <c>MapBlueprintApi</c> then maps and describes the API with
/// them, so the document written at build time is the one the app serves.
/// </summary>
public sealed class DeclaredApiMappingTests
{
    [Fact]
    public void When_the_base_path_and_openapi_are_declared_then_MapBlueprintApi_maps_and_describes_the_api_with_them()
    {
        using var app = CreateApp(static blueprint => blueprint
            .UseBasePath("declared/")
            .ConfigureOpenApi(static info => info.Title = "Declared API")
            .ConfigureOpenApi(static info => info.Version = "9.8.7"));

        app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(static endpoint => endpoint.RoutePattern.RawText);
        routes.Should().Contain("declared/.blueprint/openapi.json");

        var document = app.Services.GetRequiredService<PreRenderedOpenApiDocumentHolder>().Document!.Json;
        document.Should().Contain("\"Declared API\"").And.Contain("\"9.8.7\"", "each ConfigureOpenApi call applies");
    }

    [Fact]
    public void When_no_base_path_is_declared_then_MapBlueprintApi_without_one_fails()
    {
        using var app = CreateApp(static _ => { });

        var map = () => app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        map.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("UseBasePath");
    }

    [Fact]
    public void When_the_api_is_declared_and_MapBlueprintApi_is_given_its_own_base_path_then_it_fails()
    {
        // Two places to configure one document would let the document written at build time differ from the one served.
        using var app = CreateApp(static blueprint => blueprint.ConfigureOpenApi(static info => info.Title = "Declared API"));

        var map = () => app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        map.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("AddBlueprintApi");
    }

    private static WebApplication CreateApp(Action<BlueprintServiceBuilder> declare)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint =>
        {
            blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>();
            declare(blueprint);
        });

        return builder.Build();
    }
}
