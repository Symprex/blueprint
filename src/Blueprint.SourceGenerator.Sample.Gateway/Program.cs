var host = Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Blueprint.SourceGenerator.Sample.Gateway.Startup>();
    }).Build();

host.Run();
