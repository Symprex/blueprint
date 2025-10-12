namespace Blueprint.SourceGenerator.Sample;

public class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
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

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseForwardedHeaders();

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlueprintApi<SampleBlueprintAppExecutor>("api/");
        });
    }
}
