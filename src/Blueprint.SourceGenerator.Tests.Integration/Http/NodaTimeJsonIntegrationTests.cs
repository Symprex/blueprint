using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that NodaTime types (Instant, LocalDate, etc.) round-trip correctly through
/// Blueprint's JSON pipeline when <c>ConfigureForNodaTime</c> is wired via
/// <see cref="BlueprintJsonOptions"/>. ALL Signature 365 services depend on NodaTime,
/// making this a critical migration compatibility test.
/// </summary>
public class NodaTimeJsonIntegrationTests
{
    [Fact]
    public async Task When_NodaTime_converters_configured_then_Instant_serializes_as_ISO_string()
    {
        var endpoints = CreateEndpointsWithNodaTime();

        // Verify DI: BlueprintJsonOptions should have NodaTime converters
        var jsonOpts = endpoints.ServiceProvider.GetService<IOptions<BlueprintJsonOptions>>();
        jsonOpts.Should().NotBeNull("BlueprintJsonOptions should be registered");

        // Verify: options have NodaTime converters (don't deserialize — that freezes the options instance)
        var converterTypes = jsonOpts!.Value.SerializerOptions.Converters.Select(static c => c.GetType().Name).ToList();
        converterTypes.Should().Contain(
            n => n.Contains("NodaTime") || n.Contains("Noda"),
            "NodaTime converters should be in the serializer options");

        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/nodatime-echo");
        endpoint.Should().NotBeNull("the NodaTime echo query route should be registered");

        var bodyJson = """{"createdAt":"2024-06-15T10:30:00Z","eventDate":"2024-06-15","duration":"2:30:00"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = "POST";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        response.StatusCode.Should().Be(StatusCodes.Status200OK,
            $"Response body: {response.Body}");
        response.ContentType.Should().StartWith("application/json");

        // Verify NodaTime types serialize correctly in the response
        response.Body.Should().Contain("2024-06-15T10:30:00Z", "Instant should serialize as ISO 8601");
        response.Body.Should().Contain("2024-06-15", "LocalDate should serialize as ISO date");
        response.Body.Should().Contain("2:30:00", "Duration should serialize in NodaTime round-trip format");
    }

    [Fact]
    public async Task When_NodaTime_converters_NOT_configured_then_Instant_serialization_fails_gracefully()
    {
        // Without NodaTime converters, the serializer context won't know how to serialize NodaTime types.
        // This proves that the ConfigureJson step is essential.
        var endpoints = CreateEndpointsWithoutNodaTime();
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        var endpoint = FindEndpoint(endpoints, "api/e2e/nodatime-echo");
        endpoint.Should().NotBeNull();

        var bodyJson = """{"createdAt":"2024-06-15T10:30:00Z","eventDate":"2024-06-15","duration":"2:30:00"}"""u8.ToArray();
        var response = await InvokeEndpointAsync(endpoint!, endpoints.ServiceProvider, context =>
        {
            context.Request.Method = "POST";
            context.Request.ContentType = "application/json; charset=utf-8";
            context.Request.ContentLength = bodyJson.Length;
            context.Request.Body = new MemoryStream(bodyJson);
        });

        // Without NodaTime converters, the body binding will fail because the
        // Utf8JsonReader doesn't know how to deserialize NodaTime types
        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
            "without NodaTime converters, NodaTime type deserialization should fail at body binding");
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static TestEndpointRouteBuilder CreateEndpointsWithNodaTime()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>(sp =>
            {
                var app = new IntegrationTestBlueprintApp();
                var jsonOptions = sp.GetService<IOptions<BlueprintJsonOptions>>()?.Value;
                if (jsonOptions is not null)
                {
                    app.ApplySerializerOptions(jsonOptions.SerializerOptions);
                }

                return app;
            })
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();

        // Wire NodaTime converters into Blueprint's JSON options — exactly as S365 services do
        services.Configure<BlueprintJsonOptions>(json =>
        {
            json.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        });

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static TestEndpointRouteBuilder CreateEndpointsWithoutNodaTime()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>();

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static Endpoint? FindEndpoint(IEndpointRouteBuilder builder, string expectedRoute)
    {
        return builder.DataSources
            .SelectMany(static ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .FirstOrDefault(e => e.RoutePattern.RawText == expectedRoute);
    }

    private static async Task<TestHttpResponse> InvokeEndpointAsync(
        Endpoint endpoint,
        IServiceProvider services,
        Action<HttpContext>? configureContext = null)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.SetEndpoint(endpoint);
        httpContext.Request.Method = "GET";

        // Set the path from the endpoint route pattern
        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var rawPath = routeEndpoint.RoutePattern.RawText ?? string.Empty;
            httpContext.Request.Path = "/" + rawPath.TrimStart('/');
        }

        httpContext.Response.Body = new MemoryStream();

        configureContext?.Invoke(httpContext);

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.Body.Position = 0;
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();

        return new TestHttpResponse(httpContext.Response.StatusCode, httpContext.Response.ContentType, body);
    }

    private sealed record TestHttpResponse(int StatusCode, string? ContentType, string Body);

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

// ─── NodaTime E2E test operations ─────────────────────────────────────────────
// These are picked up by the IntegrationTestBlueprintApp scanner at compile time.

/// <summary>
/// Response type containing NodaTime types, proving they serialize correctly through Blueprint's JSON pipeline.
/// </summary>
public record NodaTimeEchoResponse(Instant CreatedAt, LocalDate EventDate, Duration Duration);

/// <summary>
/// Anonymous POST that accepts and returns NodaTime types (Instant, LocalDate, Duration).
/// This proves the full round-trip: JSON body deserialization → handler → JSON response serialization
/// with NodaTime converters configured via <see cref="BlueprintJsonOptions"/>.
/// </summary>
[AllowAnonymous]
[RootLink("/e2e/nodatime-echo")]
[HttpPost]
public partial class E2ENodaTimeEchoCommand : ICommand<NodaTimeEchoResponse>
{
    /// <summary>An <see cref="Instant"/> value representing a point in time.</summary>
    public Instant CreatedAt { get; set; }

    /// <summary>A <see cref="LocalDate"/> value representing a calendar date without time.</summary>
    public LocalDate EventDate { get; set; }

    /// <summary>A <see cref="Duration"/> value representing a time span.</summary>
    public Duration Duration { get; set; }

    /// <summary>Echoes the NodaTime values back as a response to prove round-trip serialization.</summary>
    /// <param name="command">The command containing NodaTime values to echo.</param>
    /// <returns>A response containing the same NodaTime values.</returns>
    public static Task<NodaTimeEchoResponse> HandleAsync(E2ENodaTimeEchoCommand command)
    {
        return Task.FromResult(new NodaTimeEchoResponse(command.CreatedAt, command.EventDate, command.Duration));
    }
}
