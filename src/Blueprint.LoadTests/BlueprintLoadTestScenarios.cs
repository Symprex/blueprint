using System.Security.Claims;
using System.Text;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBomber.CSharp;

namespace Blueprint.LoadTests;

/// <summary>
/// NBomber load test scenarios exercising Blueprint's generated HTTP pipeline
/// through the sample application host. Covers query, command, validation failure,
/// and OpenAPI spec endpoints to profile throughput and latency under concurrent load.
/// </summary>
public static class BlueprintLoadTestScenarios
{
    private static readonly string[] TemplateIds = ["template-001", "template-002"];

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
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices((hostContext, services) =>
                {
                    var env = hostContext.HostingEnvironment;
                    services.AddLogging();
                    services.AddSingleton<TemplateStore>();
                    services.AddSingleton<ITenantAccessor, AsyncLocalTenantAccessor>();

                    services.AddAuthentication("LoadTest")
                        .AddScheme<AuthenticationSchemeOptions, LoadTestAuthHandler>("LoadTest", _ => { });

                    services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(blueprint =>
                    {
                        blueprint.AddAuditSink<LoggingAuditSink>();
                        blueprint.AddResourceEventAuditSink<LoggingResourceEventAuditSink>();
                        blueprint.AddResourceAuthorizationProvider<SampleResourceAuthorizationProvider>();
                        blueprint.AddBackgroundTaskScheduleProvider<SampleHangfireTaskProvider>();
                        blueprint.AddBackgroundTaskContextProvider<SampleTenantContextProvider>();
                        blueprint.AddUnitOfWork<SampleMartenUnitOfWork>();
                        blueprint.AddMessageOutbox<SampleRebusMessageOutbox>();
                        blueprint.AddUserAuthorisationContextFactory<SampleUserAuthorisationContextFactory>();
                        blueprint.AddOperationAuthoriser<TenantIsolationApiAuthoriser>();
                        blueprint.AddClaimsAuthorisation();
                        blueprint.AddContextMetadataProvider<SampleContextMetadataProvider>();
                        blueprint.AddLinkVisibilityAuthoriser<ArchiveTemplateLinkChecker>();
                        blueprint.AddRecurringTaskScheduler<SampleRecurringTaskScheduler>();
                        blueprint.AddPropertyPopulator<TenantPropertyPopulator>();
                        blueprint.AddResourceExpander<OwnerResourceExpander>();
                        blueprint.AddETagSupport();

                        blueprint.ConfigureHttp(http =>
                        {
                            http.ExposeExceptionDetailsInErrorResponses = env.IsDevelopment();
                        });
                    });
                });
                webBuilder.Configure((context, app) =>
                {
                    app.UseAuthentication();
                    app.UseRouting();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
                            "api/",
                            configureOpenApi: info =>
                            {
                                info.Title = "Template Management API";
                                info.Version = "1.0.0";
                            });
                    });
                });
            });

        return builder.Build();
    }

    private static NBomber.Contracts.ScenarioProps CreateGetTemplatesScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("get_templates_list", async context =>
            {
                using var response = await httpClient.GetAsync("http://localhost/api/templates");
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateGetTemplateByIdScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("get_template_by_id", async context =>
            {
                var id = TemplateIds[context.ScenarioInfo.ThreadNumber % TemplateIds.Length];
                using var response = await httpClient.GetAsync($"http://localhost/api/templates/{id}");
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 200, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateCreateTemplateScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("create_template_command", async context =>
            {
                var payload = $$"""
                    {
                        "name": "Load Test Template {{context.ScenarioInfo.ThreadNumber}}",
                        "htmlSource": "<p>Created during load test</p>",
                        "plainTextSource": "Created during load test"
                    }
                    """;

                using var response = await httpClient.PostAsync(
                    "http://localhost/api/templates",
                    new StringContent(payload, Encoding.UTF8, "application/json"));
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateValidationFailureScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("validation_failure_fast_path", async context =>
            {
                // Intentionally omit required Name field to trigger 422 validation failure
                using var response = await httpClient.PostAsync(
                    "http://localhost/api/templates",
                    new StringContent("""{"htmlSource": "x"}""", Encoding.UTF8, "application/json"));
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 300, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateOpenApiScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("openapi_spec_fetch", async context =>
            {
                using var response = await httpClient.GetAsync("http://localhost/api/.blueprint/openapi.json");
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 10, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }

    private static NBomber.Contracts.ScenarioProps CreateHealthCheckScenario(global::System.Net.Http.HttpClient httpClient)
    {
        return Scenario.Create("health_check", async context =>
            {
                using var response = await httpClient.GetAsync("http://localhost/api/system/health");
                return NBomber.CSharp.Response.Ok(statusCode: ((int)response.StatusCode).ToString());
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 500, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));
    }
}

/// <summary>
/// Auto-authenticates every request as a test user for load testing purposes.
/// </summary>
internal sealed class LoadTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LoadTestAuthHandler"/> class.
    /// </summary>
    /// <param name="options">The authentication scheme options.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public LoadTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "load-test-user"),
            new Claim(ClaimTypes.Name, "Load Test User"),
            new Claim("account_id", "default-account"),

            // Scope claims for SampleResourceAuthorizationProvider (checks "scope" claim type
            // matching "{noun}:{verb}" from [Resource] attributes on operations).
            new Claim("scope", "template:read"),
            new Claim("scope", "template:write"),

            // Permission claims for ClaimsAuthorizationModule (checks urn:claims/permission).
            new Claim("urn:claims/permission", "ManageTemplates"),
            new Claim("urn:claims/permission", "read:template/*"),
            new Claim("urn:claims/permission", "write:template/*"),
        };

        var identity = new ClaimsIdentity(claims, "LoadTest");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "LoadTest");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
