using Blueprint.SourceGenerator.Sample;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
    }).Build();

// Development helper: writes Spec/openapi.json to disk when running locally.
// Compiled out in Release builds and only runs in the Development environment.
host.OutputOpenApi();

host.Run();
