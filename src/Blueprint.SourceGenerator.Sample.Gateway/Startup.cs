namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// Minimal gateway startup demonstrating inter-service communication via <c>BlueprintHttpClient</c>.
/// The gateway registers its own operations from the gateway contract assembly and delegates execution
/// to the upstream template service through a typed HTTP client.
/// </summary>
public class Startup
{
    /// <summary>
    /// Registers gateway services including the typed HTTP client for the upstream template service.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();

        // Register the upstream service's BlueprintApp so the HTTP client can discover
        // operation descriptors, URLs, and HTTP methods at zero-reflection cost.
        services.AddBlueprintHttpClient<SampleBlueprintApp>(
            new Uri("http://localhost:5000/api/"));

        services.AddBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>(blueprint =>
        {
            blueprint.ConfigureHttp(http =>
            {
                http.ExposeExceptionDetailsInErrorResponses = true;
            });
        });
    }

    /// <summary>
    /// Maps gateway endpoints under the <c>/gateway/</c> route prefix.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public void Configure(IApplicationBuilder app)
    {
        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>(
                "gateway/",
                configureOpenApi: info =>
                {
                    info.Title = "Template Gateway API";
                    info.Version = "1.0.0";
                    info.Description = "Gateway service that proxies template operations to the upstream template service.";
                });
        });
    }
}
