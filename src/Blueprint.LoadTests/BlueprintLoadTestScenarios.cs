using System.Text;
using Blueprint.SourceGenerator.Sample;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using NBomber.CSharp;
using NBomber.Http.CSharp;
using HttpPlugin = NBomber.Http.CSharp.Http;

namespace Blueprint.LoadTests;

/// <summary>
/// NBomber load test scenarios exercising Blueprint's generated HTTP pipeline
/// through the sample application host. Covers query, command, validation failure,
/// and OpenAPI spec endpoints to profile throughput and latency under concurrent load.
/// </summary>
public static class BlueprintLoadTestScenarios
{
    private static readonly string[] TemplateIds = ["tmpl-001", "tmpl-002", "tmpl-003", "tmpl-004", "tmpl-005"];

    /// <summary>
    /// Entry point — starts the sample host, runs all load scenarios, then reports results.
    /// </summary>
    /// <param name="args">Command-line arguments (unused).</param>
    /// <returns>A task representing the load test execution.</returns>
    public static async Task Main(string[] args)
    {
        using var host = CreateTestHost();
        await host.StartAsync();

        var httpClient = host.GetTestClient();
        httpClient.BaseAddress = new Uri("http://localhost/api/");

        var getTemplatesScenario = CreateGetTemplatesScenario(httpClient);
        var getTemplateByIdScenario = CreateGetTemplateByIdScenario(httpClient);
        var createTemplateScenario = CreateCreateTemplateScenario(httpClient);
        var validationFailureScenario = CreateValidationFailureScenario(httpClient);
        var openApiScenario = CreateOpenApiScenario(httpClient);
        var healthCheckScenario = CreateHealthCheckScenario(httpClient);

        NBomberRunner
            .RegisterScenarios(
                getTemplatesScenario,
                getTemplateByIdScenario,
                createTemplateScenario,
                validationFailureScenario,
                openApiScenario,
                healthCheckScenario)
            .WithReportFolder("reports")
            .Run();

        await host.StopAsync();
    }

    private static IHost CreateTestHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseStartup<Startup>();
            })
            .Build();
    }

    private static NBomber.Contracts.ScenarioProps CreateGetTemplatesScenario(HttpClient httpClient)
    {
        return Scenario.Create("get_templates_list", async context =>
            {
                var request = HttpPlugin.CreateRequest("GET", "http://localhost/api/templates");
                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateGetTemplateByIdScenario(HttpClient httpClient)
    {
        return Scenario.Create("get_template_by_id", async context =>
            {
                var id = TemplateIds[context.ScenarioInfo.ThreadNumber % TemplateIds.Length];
                var request = HttpPlugin.CreateRequest("GET", $"http://localhost/api/templates/{id}");
                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateCreateTemplateScenario(HttpClient httpClient)
    {
        return Scenario.Create("create_template_command", async context =>
            {
                var payload = $$"""
                    {
                        "name": "Load Test Template {{context.ScenarioInfo.ThreadNumber}}",
                        "description": "Created during load test",
                        "category": "Testing",
                        "tenantId": "tenant-load-test"
                    }
                    """;

                var request = HttpPlugin.CreateRequest("POST", "http://localhost/api/templates")
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent(payload, Encoding.UTF8, "application/json"));

                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateValidationFailureScenario(HttpClient httpClient)
    {
        return Scenario.Create("validation_failure_fast_path", async context =>
            {
                var request = HttpPlugin.CreateRequest("POST", "http://localhost/api/templates")
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(new StringContent("""{"tenantId": ""}""", Encoding.UTF8, "application/json"));

                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 300, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateOpenApiScenario(HttpClient httpClient)
    {
        return Scenario.Create("openapi_spec_fetch", async context =>
            {
                var request = HttpPlugin.CreateRequest("GET", "http://localhost/api/$openapi");
                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateHealthCheckScenario(HttpClient httpClient)
    {
        return Scenario.Create("health_check", async context =>
            {
                var request = HttpPlugin.CreateRequest("GET", "http://localhost/api/system/health");
                var response = await HttpPlugin.Send(httpClient, request);

                return response;
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 500, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }
}
