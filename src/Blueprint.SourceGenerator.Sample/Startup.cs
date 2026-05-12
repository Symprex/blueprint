using System.Collections.Immutable;
using Blueprint.SourceGenerator.Sample.Api;

namespace Blueprint.SourceGenerator.Sample;

/// <summary>
/// Minimal ASP.NET Core startup used by the sample app to demonstrate Blueprint endpoint registration in a real host.
/// </summary>
public class Startup
{
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="Startup"/> class with the hosting environment.
    /// </summary>
    /// <param name="environment">The hosting environment for conditional configuration.</param>
    public Startup(IWebHostEnvironment environment)
    {
        this._environment = environment;
    }

    /// <summary>
    /// Registers the sample Blueprint services needed to execute the sample operations.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    public void ConfigureServices(IServiceCollection services)
    {
        var environment = this._environment;
        services.AddLogging();
        services.AddSingleton<TemplateStore>();

        // Multi-tenancy: first-class compile-time tenant support.
        // TenantModule in the generated pipeline resolves tenant from claims via ITenantResolver
        // and directly assigns TenantId properties (no reflection, no IPropertyPopulator needed).
        services.AddSingleton<Blueprint.Tenancy.ITenantAccessor, Blueprint.Tenancy.AsyncLocalTenantAccessor>();

        // ASP.NET Core authentication must be configured so that HttpContext.User is
        // populated before Blueprint's generated [MustBeAuthenticated] / [Resource]
        // pipeline checks run.  A real consumer would wire a scheme here, e.g.:
        //
        //   services.AddAuthentication("Bearer")
        //       .AddJwtBearer(options => { /* configure issuer, audience, key */ });
        //   services.AddAuthorization();

        services.AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(blueprint =>
        {
            blueprint.AddAuditSink<LoggingAuditSink>();
            blueprint.AddResourceEventAuditSink<LoggingResourceEventAuditSink>();
            blueprint.AddResourceAuthorizationProvider<SampleResourceAuthorizationProvider>();
            blueprint.AddBackgroundTaskScheduleProvider<SampleHangfireTaskProvider>();
            blueprint.AddBackgroundTaskContextProvider<SampleTenantContextProvider>();
            blueprint.AddUnitOfWork<SampleMartenUnitOfWork>();
            blueprint.AddMessageOutbox<SampleRebusMessageOutbox>();

            // User context: converts ClaimsIdentity → IUserAuthorisationContext,
            // making user/account info available to authorisers and handler code
            blueprint.AddUserAuthorisationContextFactory<SampleUserAuthorisationContextFactory>();

            // Business authorization: enforces tenant isolation by comparing the
            // user's AccountId to the operation's TenantId property
            blueprint.AddOperationAuthoriser<TenantIsolationApiAuthoriser>();

            // Claims-based authorization: registers the reusable ClaimRequiredAttribute pipeline
            // for consumers that define custom claim-demand attributes.
            blueprint.AddClaimsAuthorisation();

            // Context metadata: enriches resource events with tenant context
            blueprint.AddContextMetadataProvider<SampleContextMetadataProvider>();

            // State-based link visibility: hide the Archive link on already-archived templates
            blueprint.AddLinkVisibilityAuthoriser<ArchiveTemplateLinkChecker>();

            // Recurring tasks: declares cron-based schedules that a provider (e.g., Hangfire)
            // registers for periodic execution. This proves the recurring task consumer seam.
            blueprint.AddRecurringTaskScheduler<SampleRecurringTaskScheduler>();

            // First-class tenancy: resolve tenant from JWT claims at compile time.
            // Replaces the old IPropertyPopulator-based TenantPropertyPopulator that used reflection.
            blueprint.AddTenancy<Blueprint.Tenancy.AsyncLocalTenantAccessor, SampleTenantResolver>();

            // Resource expansion: populate Expandable<OwnerApiResource> when client
            // requests ?expand=Owner (like Signature365's AzureAdGroupApiResourceExpander).
            // The generated pipeline discovers Expandable<T> properties at compile time.
            blueprint.AddResourceExpander<OwnerResourceExpander>();

            // ETag support: enables conditional request handling (If-None-Match / 304 Not Modified).
            // Handlers inject ETagger and call WithETag() to avoid re-computing responses.
            blueprint.AddETagSupport();

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

        // In a production app, place authentication middleware before routing so
        // that HttpContext.User is populated before Blueprint endpoint execution:
        //
        //   app.UseAuthentication();
        //   app.UseAuthorization();

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>(
                "api/",
                configureOpenApi: info =>
                {
                    info.Title = "Template Management API";
                    info.Version = "1.0.0";
                    info.Description = "Blueprint sample API demonstrating template lifecycle operations.";
                    info.TermsOfService = "https://example.com/terms";
                    info.Contact = new Blueprint.Runtime.Http.OpenApiContactInfo
                    {
                        Name = "API Support",
                        Email = "support@example.com",
                        Url = "https://example.com/support",
                    };
                    info.License = new Blueprint.Runtime.Http.OpenApiLicenseInfo
                    {
                        Name = "MIT",
                        Url = "https://opensource.org/licenses/MIT",
                    };
                },
                configureOpenApiOperations: ops =>
                {
                    // Remove auto-populated TenantId parameters from public API docs.
                    // In production, TenantId is set by the TenantModule from JWT claims,
                    // so external callers should not see it in the OpenAPI spec.
                    ops.Processors.Add(static (operation, descriptor) =>
                    {
                        var filtered = operation.Parameters
                            .Where(static p => !string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
                            .ToImmutableArray();

                        return filtered.Length != operation.Parameters.Length
                            ? operation with { Parameters = filtered }
                            : operation;
                    });
                });
        });
    }
}
