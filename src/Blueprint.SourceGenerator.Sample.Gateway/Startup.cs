namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// Minimal gateway startup demonstrating inter-service communication via <c>BlueprintHttpClient</c>.
/// The gateway registers its own operations from the gateway contract assembly and delegates execution
/// to the upstream template service through a typed HTTP client.
/// </summary>
public class Startup
{
    /// <summary>
    /// The configuration key holding the upstream template service's API base address, such as
    /// <c>http://localhost:5000/api/</c>. A deployment, or the end-to-end harness, points the gateway at
    /// wherever the template service is listening by setting it.
    /// </summary>
    public const string TemplateServiceAddressKey = "Upstreams:TemplateService";

    private const string DefaultTemplateServiceAddress = "http://localhost:5000/api/";

    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="Startup"/> class with the app's configuration.
    /// </summary>
    /// <param name="configuration">Supplies <see cref="TemplateServiceAddressKey"/>.</param>
    public Startup(IConfiguration configuration)
    {
        this._configuration = configuration;
    }

    /// <summary>
    /// Registers gateway services including the typed HTTP client for the upstream template service.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();

        // Register the upstream service's BlueprintApp so the HTTP client can discover
        // operation descriptors, URLs, and HTTP methods at zero-reflection cost.
        services.AddSingleton<SampleBlueprintApp>();
        var templateServiceAddress = new Uri(this._configuration[TemplateServiceAddressKey] ?? DefaultTemplateServiceAddress);
        services.AddBlueprintHttpClient<SampleBlueprintApp>(templateServiceAddress);

        // The typed client the [Expose<TInner>] reads forward through: their generated handlers use its named HTTP client.
        services.AddBlueprintClient<Api.ITemplateServiceApi, Api.TemplateServiceApi>((_, client) => client.BaseAddress = templateServiceAddress);

        services.AddBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>(blueprint =>
        {
            blueprint.ConfigureHttp(http =>
            {
                http.ExposeExceptionDetailsInErrorResponses = true;
            });

            // Declared here, not passed to MapBlueprintApi, so that the BlueprintEmitOpenApi build target, which stops
            // the app once its host is built, writes the document the gateway serves.
            blueprint.UseBasePath("gateway/");
            blueprint.ConfigureOpenApi(info =>
            {
                info.Title = "Template Gateway API";
                info.Version = "1.0.0";
                info.Description = "Gateway service that proxies template operations to the upstream template service.";
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
            endpoints.MapBlueprintApi<GatewayBlueprintApp, GatewayBlueprintAppExecutor>();
        });
    }
}
