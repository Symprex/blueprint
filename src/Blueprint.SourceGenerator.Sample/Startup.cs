using Blueprint.SourceGenerator.Sample.Api;

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
    /// <param name="environment">The hosting environment for conditional configuration.</param>
    public void ConfigureServices(IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddLogging();
        services.AddSingleton<TemplateStore>();

        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(blueprint =>
        {
            blueprint.AddAuditSink<LoggingAuditSink>();
            blueprint.AddResourceEventAuditSink<LoggingResourceEventAuditSink>();
            blueprint.AddResourceAuthorizationProvider<SampleResourceAuthorizationProvider>();
            blueprint.AddBackgroundTaskScheduler<InProcessBackgroundTaskScheduler>();

            blueprint.ConfigureHttp(http =>
            {
                // Expose full exception details in non-production environments for easier debugging.
                http.ExposeExceptionDetailsInErrorResponses = environment.IsDevelopment();
            });
        });
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
