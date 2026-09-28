using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an operation's <c>TenantId</c> is bound from the request like any other property, and filled from the
/// caller's tenant only when the request left it unset and the caller is authenticated, as legacy Blueprint did.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) bound <c>TenantId</c> in its <c>MessagePopulationMiddlewareBuilder</c> step,
/// from the query of a GET (<c>Portal/Internal/Generated/Blueprint/GetMicrosoft365AddOnQueryExecutorPipeline.cs:41-43</c>),
/// the body of any other method and the route when the URL had a <c>{TenantId}</c>, as every other property.
/// Signature365's <c>SetTenantMiddleware</c> then ran for every operation (<c>Common/Api/SetTenantMiddleware.cs:32-35</c>),
/// and only for an authenticated caller (:72-74), setting
/// <c>TenantId = TenantId != null ? TenantId : ApiId.Hydrate(currentTenant)</c> (:60-68, and the generated
/// <c>GetMicrosoft365AddOnQueryExecutorPipeline.cs:56-62</c>): "we will NOT override any existing data because we want the
/// auth system to ensure correct responses if the user does not have access to the tenant" (:60-62). Nothing refused a
/// caller-named tenant but the authorisers.
/// </para>
/// <para>
/// <c>ApiId.Hydrate</c> threw <c>ArgumentOutOfRangeException</c> for a null or malformed value
/// (<c>Common.Core/Data/ApiId.cs:116-126</c>, <c>:136-140</c>, <c>:242-247</c>), so an authenticated caller with no tenant
/// claim that left <c>TenantId</c> unset failed with it, a 500. <see cref="BoundTenantId"/> is shaped as <c>ApiId</c> is: a
/// struct with a type converter that hydrates, and a JSON converter.
/// </para>
/// </remarks>
public sealed class TenantIdBindingWireTests
{
    private const string CallerTenant = "tenant_caller";
    private const string NamedTenant = "tenant_named";

    [Fact]
    public async Task A_TenantId_in_a_commands_body_is_bound_and_not_replaced_by_the_callers_tenant()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.PostAsync("tenant-binding/commands", $$"""{"tenantId":"{{NamedTenant}}","name":"x"}""", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task A_TenantId_a_commands_body_leaves_out_is_filled_from_the_callers_tenant()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.PostAsync("tenant-binding/commands", """{"name":"x"}""", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={CallerTenant}\"");
    }

    [Fact]
    public async Task A_TenantId_in_a_querys_query_string_is_bound_and_not_replaced_by_the_callers_tenant()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync($"tenant-binding/queries?tenantId={NamedTenant}", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task A_TenantId_a_querys_query_string_leaves_out_is_filled_from_the_callers_tenant()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/queries", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={CallerTenant}\"");
    }

    [Fact]
    public async Task A_TenantId_in_the_route_is_bound_from_the_route_and_not_replaced_by_the_callers_tenant()
    {
        // Legacy bound a {TenantId} placeholder in the same population step (AdminPortal's
        // AdminPortalGetMicrosoft365AddOnQueryExecutorPipeline.cs:36), and SetTenantMiddleware then found it set.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync($"tenant-binding/tenants/{NamedTenant}", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task A_TenantId_in_a_legacy_alternate_named_route_placeholder_is_bound_from_the_route_and_not_replaced_by_the_callers_tenant()
    {
        // Shaped as Signature365 Admin Portal's GetDefaultProductForTenantQuery. Legacy routed {TenantId:Id} as {TenantId}
        // and bound it from the route (Blueprint/ApiOperationLink.cs:53-78; HttpRouteMessagePopulationSource.cs:36-41,
        // :99-107), so SetTenantMiddleware found it set (:60-68). Its resource key's {TenantId:Id} read the operation's
        // TenantId, which it has (Blueprint/Authorisation/ClaimRequiredAttribute.cs:74-99), so the key names that tenant.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync($"tenant-binding/billing/tenants/{NamedTenant}/products/default", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
        host.ExpandedResourceKeys.Should().Equal($"/Tenants/{NamedTenant}");
    }

    [Fact]
    public async Task An_anonymous_callers_TenantId_is_bound_from_the_request()
    {
        // Signature365's anonymous PortalCreateAzureAdDomainVerificationDnsRecordsCommand names its tenant in the body: with
        // no user there is nothing to fill it from.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.PostAsync("tenant-binding/anonymous", $$"""{"tenantId":"{{NamedTenant}}"}""", Caller.Anonymous);

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task An_authenticated_caller_of_an_anonymous_operation_has_an_unset_TenantId_filled()
    {
        // SetTenantMiddleware matched every operation (SetTenantMiddleware.cs:32-35), anonymous ones included.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/anonymous-queries", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={CallerTenant}\"");
    }

    [Fact]
    public async Task An_unauthenticated_identity_that_carries_a_tenant_claim_does_not_have_its_TenantId_filled()
    {
        // SetTenantMiddleware filled only inside `if (claimsIdentity?.IsAuthenticated == true)` (:72-74).
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/anonymous-queries", Caller.Unauthenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be("\"tenant=(unset)\"");
    }

    [Fact]
    public async Task An_authenticated_caller_without_a_tenant_that_leaves_TenantId_unset_fails_as_legacys_hydration_of_no_tenant_did()
    {
        // ApiId.Hydrate(null) threw ArgumentOutOfRangeException (ApiId.cs:116-126, :136-140, :242-247); Signature365 pins it
        // (Portal.Tests EditTenantIdorProtectionTests.When_partner_caller_with_tenant_scoped_role_and_TenantId_unset_then_
        // fails_with_ArgumentOutOfRangeException, at 8b40ecc961).
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/queries", Caller.Authenticated(tenant: null));

        // Legacy's pipeline caught it and wrote it as any unhandled exception, a 500 problem.
        status.Should().Be(500);
        body.Should().Contain("\"status\":500");
        host.Handled.Should().BeEmpty("the handler must not run with a TenantId nobody supplied");
    }

    [Fact]
    public async Task An_authenticated_caller_without_a_tenant_that_names_one_is_answered_for_it()
    {
        // The Partners-style caller: no tenant claim, the tenant named in the request (EditTenantIdorProtectionTests'
        // TenantId_set and PUT-body cases at 8b40ecc961).
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync($"tenant-binding/queries?tenantId={NamedTenant}", Caller.Authenticated(tenant: null));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task A_string_TenantId_a_querys_query_string_leaves_out_is_not_filled_by_an_app_that_fills_only_its_TenantId_type()
    {
        // The app declares [BlueprintAppTenancy(TenantIdType = typeof(BoundTenantId))], as Signature365's apps declare
        // ApiId. Legacy filled only an ApiId or ApiId? TenantId (SetTenantMiddleware.cs:65), so a string one the request
        // left out stayed null: Signature365's GenerateSignatureCommand and IdentityServer TenantUsers operations.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/string-queries", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be("\"tenant=(unset)\"");
    }

    [Fact]
    public async Task A_string_TenantId_in_a_querys_query_string_is_bound_by_an_app_that_fills_only_its_TenantId_type()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync($"tenant-binding/string-queries?tenantId={NamedTenant}", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task A_nullable_TenantId_of_the_apps_TenantId_type_a_querys_query_string_leaves_out_is_filled_from_the_callers_tenant()
    {
        // Legacy filled an ApiId? as it filled an ApiId (SetTenantMiddleware.cs:65, GetNonNullableType()).
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/nullable-queries", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={CallerTenant}\"");
    }

    [Fact]
    public async Task An_init_only_TenantId_a_querys_query_string_leaves_out_is_not_filled()
    {
        // Legacy skipped an init-only TenantId (SetTenantMiddleware.cs:65, :77-90).
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.GetAsync("tenant-binding/init-only-queries", Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be("\"tenant=(unset)\"");
    }

    [Fact]
    public async Task A_client_sending_an_operation_whose_only_property_is_TenantId_is_answered_for_the_tenant_it_named()
    {
        // The Admin Portal answers AdminPortalGetMicrosoft365AddOnQuery by sending the Portal a GetMicrosoft365AddOnQuery with
        // the tenant it was asked about (AdminPortalGetMicrosoft365AddOnQueryHandler.cs:23-26); the Portal's
        // [Resource(..., "/Tenants/{TenantId}")] is then checked for that tenant, and the handler reads it.
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.SendWithCompiledClientAsync(
            new GetBoundTenantAddOnQuery { TenantId = BoundTenantId.Hydrate(NamedTenant) },
            Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
        host.ExpandedResourceKeys.Should().Equal($"/Tenants/{NamedTenant}");
    }

    [Fact]
    public async Task A_client_sending_a_TenantId_beside_other_query_properties_sends_it_too()
    {
        await using var host = await TenantHost.StartAsync();

        var (status, body) = await host.SendWithCompiledClientAsync(
            new BindTenantIdQuery { TenantId = BoundTenantId.Hydrate(NamedTenant), Filter = "f" },
            Caller.Authenticated(CallerTenant));

        status.Should().Be(200);
        body.Should().Be($"\"tenant={NamedTenant}\"");
    }

    [Fact]
    public async Task An_authenticated_caller_naming_another_tenant_in_the_body_is_refused_and_the_handler_does_not_run()
    {
        // R100: TenantIdBindingWireTests' other cases prove binding leaves a caller-named tenant alone (legacy's
        // SetTenantMiddleware never refused one on its own, :60-62 above) — but that only matters if an authoriser
        // downstream still sees the bound value and can refuse it. Wire a denying IOperationAuthoriser through the real
        // router (TenantIsolationIntegrationTests.cs's equivalent is in-process, no binding) and prove the caller's
        // named tenant reaches it, is refused, and the handler never runs.
        await using var host = await TenantHost.StartAsync(withDenyingAuthoriser: true);

        var (status, body) = await host.PostAsync("tenant-binding/commands", $$"""{"tenantId":"{{NamedTenant}}","name":"x"}""", Caller.Authenticated(CallerTenant));

        status.Should().Be(403);
        body.Should().Be("""{"type":"unauthorized","title":"You may not act for another tenant","status":403}""");
        host.Handled.Should().BeEmpty("the handler must not run for a tenant the authoriser refused");
    }

    [Fact]
    public async Task An_authenticated_caller_naming_another_tenant_in_the_query_is_refused_and_the_handler_does_not_run()
    {
        await using var host = await TenantHost.StartAsync(withDenyingAuthoriser: true);

        var (status, body) = await host.GetAsync($"tenant-binding/queries?tenantId={NamedTenant}", Caller.Authenticated(CallerTenant));

        status.Should().Be(403);
        body.Should().Be("""{"type":"unauthorized","title":"You may not act for another tenant","status":403}""");
        host.Handled.Should().BeEmpty("the handler must not run for a tenant the authoriser refused");
    }

    [Fact]
    public void The_document_publishes_TenantId_where_the_request_carries_it()
    {
        // Legacy's document: every property of a GET was a parameter, and a route placeholder a path one
        // (Blueprint.OpenApi/OpenApiDocumentBuilder.cs:120-157); any other method's body was the operation's schema
        // (:159-179). Only Signature365's Portal took TenantId out, for non-anonymous operations, itself (Portal
        // Startup.cs:119-155), which its RemoveAutoPopulatedTenantId processor still does.
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var paths = document.RootElement.GetProperty("paths");

        var bodyReference = paths.GetProperty("/tenant-binding/commands").GetProperty("post").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var bodyComponent = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(bodyReference["#/components/schemas/".Length..]);
        bodyComponent.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Contain("tenantId");

        // Not legacy-derived (parameter casing, R101): legacy named a GET's query parameter by the raw property name
        // (OpenApiDocumentBuilder.cs:150, `property.Name`), so it would have written "TenantId" here too. This document
        // was rendered with default OpenApiInfoOptions, and the framework's default for a query parameter's name is
        // camel-case, matching the wire's JSON casing rather than legacy's; the opt-in
        // `QueryParameterNaming = OpenApiParameterNaming.PropertyName` reproduces legacy's "TenantId" exactly
        // (OpenApiParameterNamingTests.Query_parameters_can_be_named_by_their_property_as_legacy_named_them). A route
        // placeholder is unaffected either way — it keeps its own literal casing, "TenantId" below.
        Parameter(paths, "/tenant-binding/queries", "tenantId").GetProperty("in").GetString().Should().Be("query");
        Parameter(paths, "/tenant-binding/tenants/{TenantId}", "TenantId").GetProperty("in").GetString().Should().Be("path");

        // A {TenantId:Id} placeholder was a path parameter as {TenantId} was (OpenApiDocumentBuilder.cs:96, :145-152).
        paths.GetProperty("/tenant-binding/billing/tenants/{TenantId}/products/default").GetProperty("get").GetProperty("parameters")
            .EnumerateArray()
            .Where(static p => string.Equals(p.GetProperty("name").GetString(), "TenantId", StringComparison.OrdinalIgnoreCase))
            .Select(static p => p.GetProperty("in").GetString())
            .Should().Equal("path");

        var addOnParameter = Parameter(paths, "/tenant-binding/add-ons/microsoft365", "tenantId");
        addOnParameter.GetProperty("in").GetString().Should().Be("query");
        addOnParameter.GetProperty("required").GetBoolean().Should().BeTrue("the property is [Required]");
    }

    private static JsonElement Parameter(JsonElement paths, string path, string name)
    {
        return paths.GetProperty(path).GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name);
    }

    /// <summary>Who a request is sent as.</summary>
    /// <param name="IsAuthenticated">Whether the identity is authenticated; null sends no identity at all.</param>
    /// <param name="Tenant">The tenant claim the identity carries, if any.</param>
    private sealed record Caller(bool? IsAuthenticated, string? Tenant)
    {
        public static Caller Anonymous { get; } = new(null, null);

        public static Caller Authenticated(string? tenant)
        {
            return new Caller(true, tenant);
        }

        public static Caller Unauthenticated(string tenant)
        {
            return new Caller(false, tenant);
        }
    }

    /// <summary>
    /// Reads the tenant from its claim whether or not the identity is authenticated, as Signature365's
    /// <c>Signature365TenantResolver</c> does.
    /// </summary>
    private sealed class ClaimTenantResolver : ITenantResolver
    {
        public const string ClaimType = "tenant_id";

        public string? ResolveTenantId(ClaimsPrincipal principal)
        {
            return principal.FindFirst(ClaimType)?.Value;
        }
    }

    /// <summary>
    /// Refuses an authenticated caller whose bound <c>TenantId</c> is not their own claimed tenant, proving that an
    /// authoriser downstream of binding still sees — and can refuse — a tenant the caller named rather than left unset.
    /// </summary>
    private sealed class DenyingOtherTenantAuthoriser : IOperationAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType.GetProperty("TenantId") is not null;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            var callerTenant = operationContext.UserPrincipal?.FindFirst(ClaimTenantResolver.ClaimType)?.Value;
            var namedTenant = descriptor.OperationType.GetProperty("TenantId")!.GetValue(operation)?.ToString();

            if (callerTenant is not null && namedTenant is not null && !string.Equals(callerTenant, namedTenant, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    ExecutionAllowed.No("tenant mismatch", "You may not act for another tenant", ExecutionAllowedFailureType.Authorisation));
            }

            return ExecutionAllowed.YesTask;
        }
    }

    /// <summary>Records every resource key the operation's authorisation expanded, and allows it.</summary>
    private sealed class RecordingResourceAuthorizationProvider(ConcurrentQueue<string> expandedKeys) : IResourceAuthorizationProvider
    {
        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
            CancellationToken cancellationToken)
        {
            foreach (var pair in resolvedScopePairs)
            {
                expandedKeys.Enqueue(pair.ExpandedResourceKey ?? "(null)");
            }

            return Task.FromResult(true);
        }
    }

    /// <summary>The integration app with tenancy, served by Kestrel.</summary>
    private sealed class TenantHost : IAsyncDisposable
    {
        private const string IdentityHeader = "X-Test-Identity";
        private const string TenantHeader = "X-Test-Tenant";

        private readonly WebApplication _app;

        private TenantHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<string> expandedResourceKeys)
        {
            this._app = app;
            this.Client = client;
            this.ExpandedResourceKeys = expandedResourceKeys;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<string> ExpandedResourceKeys { get; }

        public IReadOnlyCollection<string> Handled => TenantBindingHandlers.Handled;

        public static async Task<TenantHost> StartAsync(bool withDenyingAuthoriser = false)
        {
            TenantBindingHandlers.Handled.Clear();

            var expandedResourceKeys = new ConcurrentQueue<string>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton<IResourceAuthorizationProvider>(new RecordingResourceAuthorizationProvider(expandedResourceKeys));

            if (withDenyingAuthoriser)
            {
                builder.Services.AddSingleton<IOperationAuthoriser, DenyingOtherTenantAuthoriser>();
            }

            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                blueprint => blueprint.AddTenancy<AsyncLocalTenantAccessor, ClaimTenantResolver>());

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.TryGetValue(IdentityHeader, out var identity))
                {
                    var claims = new List<Claim> { new(System.Security.Claims.ClaimTypes.Name, "user-1") };

                    if (httpContext.Request.Headers.TryGetValue(TenantHeader, out var tenant))
                    {
                        claims.Add(new Claim(ClaimTenantResolver.ClaimType, tenant.ToString()));
                    }

                    // An identity with no authentication type is not authenticated, whatever claims it carries.
                    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                        claims,
                        authenticationType: identity == "authenticated" ? "Test" : null));
                }

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new TenantHost(app, client, expandedResourceKeys);
        }

        public Task<(int Status, string Body)> GetAsync(string url, Caller caller)
        {
            return this.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url), caller);
        }

        public Task<(int Status, string Body)> PostAsync(string url, string json, Caller caller)
        {
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
            {
                Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"),
            };

            return this.SendAsync(request, caller);
        }

        /// <summary>
        /// Builds the request as the generated client does: the URL and query string from the operation's compiled request
        /// feature, which <c>BlueprintHttpClient</c> uses whenever a descriptor has one.
        /// </summary>
        public Task<(int Status, string Body)> SendWithCompiledClientAsync(IOperation operation, Caller caller)
        {
            var descriptor = new IntegrationTestBlueprintApp().FindOperation(operation.GetType())!;
            var compiled = descriptor.GetFeatureData<CompiledHttpRequestFeatureData>()!;
            var url = compiled.BuildUrl(operation)!;
            url = compiled.AppendQueryString!(operation, url).TrimStart('/');

            return this.GetAsync(url, caller);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }

        private async Task<(int Status, string Body)> SendAsync(System.Net.Http.HttpRequestMessage request, Caller caller)
        {
            using (request)
            {
                if (caller.IsAuthenticated is { } isAuthenticated)
                {
                    request.Headers.Add(IdentityHeader, isAuthenticated ? "authenticated" : "unauthenticated");
                }

                if (caller.Tenant is not null)
                {
                    request.Headers.Add(TenantHeader, caller.Tenant);
                }

                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await this.Client.SendAsync(request);

                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }
    }
}

/// <summary>
/// A tenant id shaped as Signature365's <c>ApiId</c>: a struct whose type converter hydrates it, throwing
/// <see cref="ArgumentOutOfRangeException"/> for a null or empty value as <c>ApiId.Hydrate</c> did, and a JSON converter.
/// </summary>
[TypeConverter(typeof(BoundTenantIdTypeConverter))]
[JsonConverter(typeof(BoundTenantIdJsonConverter))]
public readonly struct BoundTenantId : IEquatable<BoundTenantId>
{
    private BoundTenantId(string value)
    {
        this.Value = value;
    }

    /// <summary>Gets the id, or null for an unset one.</summary>
    public string? Value { get; }

    public static BoundTenantId Hydrate(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? throw new ArgumentOutOfRangeException(nameof(value), $"ApiId is not in the correct format. Was {value}")
            : new BoundTenantId(value);
    }

    public bool Equals(BoundTenantId other)
    {
        return string.Equals(this.Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is BoundTenantId other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        return this.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(this.Value);
    }

    public override string? ToString()
    {
        return this.Value;
    }
}

/// <summary>Hydrates a <see cref="BoundTenantId"/> from its string, as <c>ApiId</c>'s type converter did.</summary>
public sealed class BoundTenantIdTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string);
    }

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return BoundTenantId.Hydrate((string)value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        return ((BoundTenantId?)value)?.Value;
    }
}

/// <summary>Reads and writes a <see cref="BoundTenantId"/> as its string, as <c>ApiId</c>'s JSON converter did.</summary>
public sealed class BoundTenantIdJsonConverter : JsonConverter<BoundTenantId>
{
    public override BoundTenantId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value == null ? default : BoundTenantId.Hydrate(value);
    }

    public override void Write(Utf8JsonWriter writer, BoundTenantId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Records which tenant each tenant-binding operation's handler ran for.</summary>
internal static class TenantBindingHandlers
{
    public static ConcurrentQueue<string> Handled { get; } = new();

    public static string Describe(string? tenant)
    {
        var description = "tenant=" + (tenant ?? "(unset)");
        Handled.Enqueue(description);

        return description;
    }
}

/// <summary>A command naming its tenant in its body.</summary>
[RootLink("/tenant-binding/commands")]
[HttpPost]
public partial class BindTenantIdBodyCommand : ICommand<string>
{
    public BoundTenantId TenantId { get; set; }

    public string? Name { get; set; }

    public static string Handle(BindTenantIdBodyCommand command)
    {
        return TenantBindingHandlers.Describe(command.TenantId.Value);
    }
}

/// <summary>A query naming its tenant in its query string.</summary>
[RootLink("/tenant-binding/queries")]
public partial class BindTenantIdQuery : IQuery<string>
{
    public BoundTenantId TenantId { get; set; }

    public string? Filter { get; set; }

    public static string Handle(BindTenantIdQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}

/// <summary>A query naming its tenant in its route.</summary>
[RootLink("/tenant-binding/tenants/{TenantId}")]
public partial class BindTenantIdRouteQuery : IQuery<string>
{
    public BoundTenantId TenantId { get; set; }

    public static string Handle(BindTenantIdRouteQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}

/// <summary>
/// Shaped as Signature365 Admin Portal's <c>GetDefaultProductForTenantQuery</c>: a link whose <c>{TenantId:Id}</c>
/// placeholder names the resource's <c>Id</c>, and a resource key naming the tenant. (Signature365's key is
/// <c>/Tenants/{TenantId:Id}</c>, whose fallback names no operation property: BPAU010 warns, and the primary is used.)
/// </summary>
[Resource("billing-product", "read", "/Tenants/{TenantId}")]
[Link(typeof(BoundTenantLinkApiResource), "/tenant-binding/billing/tenants/{TenantId:Id}/products/default", Rel = "defaultProduct")]
public partial class GetBoundTenantDefaultProductQuery : IQuery<string>
{
    public BoundTenantId TenantId { get; set; }

    public static string Handle(GetBoundTenantDefaultProductQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}

/// <summary>The tenant resource <see cref="GetBoundTenantDefaultProductQuery"/> is linked from, by its <c>Id</c>.</summary>
public partial class BoundTenantLinkApiResource : ApiResource
{
    public string Id { get; set; } = null!;
}

/// <summary>
/// An anonymous command naming its tenant in its body, shaped as Signature365's
/// <c>PortalCreateAzureAdDomainVerificationDnsRecordsCommand</c> (a string <c>TenantId</c>).
/// </summary>
[AllowAnonymous]
[RootLink("/tenant-binding/anonymous")]
[HttpPost]
public partial class BindTenantIdAnonymousCommand : ICommand<string>
{
    public string? TenantId { get; set; }

    public static string Handle(BindTenantIdAnonymousCommand command)
    {
        return TenantBindingHandlers.Describe(command.TenantId);
    }
}

/// <summary>
/// A query with a string <c>TenantId</c>, which is not the app's <c>TenantIdType</c>: bound from the request, never filled.
/// </summary>
[RootLink("/tenant-binding/string-queries")]
public partial class GetStringTenantIdQuery : IQuery<string>
{
    public string? TenantId { get; set; }

    public static string Handle(GetStringTenantIdQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId);
    }
}

/// <summary>A query with a nullable <see cref="BoundTenantId"/> <c>TenantId</c>, the app's <c>TenantIdType</c>.</summary>
[RootLink("/tenant-binding/nullable-queries")]
public partial class GetNullableBoundTenantIdQuery : IQuery<string>
{
    public BoundTenantId? TenantId { get; set; }

    public static string Handle(GetNullableBoundTenantIdQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId?.Value);
    }
}

/// <summary>A query with an init-only <see cref="BoundTenantId"/> <c>TenantId</c>.</summary>
[RootLink("/tenant-binding/init-only-queries")]
public partial class GetInitOnlyBoundTenantIdQuery : IQuery<string>
{
    public BoundTenantId TenantId { get; init; }

    public static string Handle(GetInitOnlyBoundTenantIdQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}

/// <summary>An anonymous query with a tenant id.</summary>
[AllowAnonymous]
[RootLink("/tenant-binding/anonymous-queries")]
public partial class GetAnonymousTenantIdQuery : IQuery<string>
{
    public BoundTenantId TenantId { get; set; }

    public static string Handle(GetAnonymousTenantIdQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}

/// <summary>
/// Shaped as Signature365's Portal <c>GetMicrosoft365AddOnQuery</c>: a GET whose only property is a required
/// <c>TenantId</c>, not in its URL, and named in its resource key.
/// </summary>
[Resource("microsoft365-add-on", "read", "/Tenants/{TenantId}")]
[RootLink("/tenant-binding/add-ons/microsoft365")]
public partial class GetBoundTenantAddOnQuery : IQuery<string>
{
    [Required]
    public BoundTenantId TenantId { get; set; }

    public static string Handle(GetBoundTenantAddOnQuery query)
    {
        return TenantBindingHandlers.Describe(query.TenantId.Value);
    }
}
