using Blueprint.SourceGenerator.Sample;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
    }).Build();

// Spec/openapi.json is written at build time instead: dotnet build -p:BlueprintEmitOpenApi=true.
host.Run();
