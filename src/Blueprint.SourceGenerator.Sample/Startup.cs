namespace Blueprint.SourceGenerator.Sample;

/// <summary>
/// Minimal ASP.NET Core startup used by the sample app to demonstrate Blueprint endpoint registration in a real host.
/// </summary>
public class Startup
{
    /// <summary>
    /// Registers the sample Blueprint services needed to execute the sample operations.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SampleBlueprintAppExecutor, SampleBlueprintAppExecutor>();

        // services.AddControllers();
        //
        // services.AddSingleton<IWeatherDataSource, WeatherDataSource>();
        //
        // services.AddHangfire(h =>
        // {
        //     h
        //         .UseStorage(new SqlServerStorage("Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Initial Catalog=blueprint-examples"))
        //         .UseRecommendedSerializerSettings();
        // });
        //
        // services.AddOpenTelemetryTracing(
        //     builder => builder
        //         .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("web-api"))
        //         .AddAspNetCoreInstrumentation()
        //         .AddBlueprintInstrumentation()
        //         .AddJaegerExporter()
        //         .AddConsoleExporter()
        // );

        // services.AddBlueprintApi(b => b
        //     .Http()
        //     .Operations(o => o.Scan(typeof(WebApi.Startup).Assembly))
        //     .AddTasksClient(t => t.UseHangfire())
        //     .AddOpenApi()
        //     .AddAuthentication(a => a.UseContextLoader<AnonymousUserAuthorisationContextFactory>())
        //     .AddResourceEvents<NullResourceEventRepository>());
    }

    /// <summary>
    /// Configures the HTTP pipeline for the sample host and maps Blueprint endpoints under the sample API route.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="env">The hosting environment for the sample app.</param>
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>("api/");
        });
    }
}
