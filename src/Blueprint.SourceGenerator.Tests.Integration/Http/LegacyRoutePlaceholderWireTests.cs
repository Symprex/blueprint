using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a legacy <c>{Property:ResourceProperty}</c> route placeholder binds <c>Property</c> from the path, through
/// the real router, as legacy Blueprint bound it.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint/ApiOperationLink.cs</c>) parsed a placeholder as
/// <c>{propName(:alternatePropName)?((format))?}</c> (:29), found <c>propName</c> on the operation (:57-61) and routed the
/// link at its <c>RoutingUrl</c>, in which the placeholder became <c>{PropertyName}</c> (:53-78;
/// <c>Blueprint.Http/ApplicationBuilderExtensions.cs:105</c>). The alternate name only told link generation which resource
/// property to read (:113-132, :236-246).
/// </para>
/// <para>
/// <c>HttpRouteMessagePopulationSource</c> then owned every placeholder property that was in all of the operation's links
/// (:36-41) and set it from <c>RouteData.Values[PropertyName]</c> (:99-107). The GET-only query catch-all
/// (<c>BlueprintApiBuilderExtensions.cs:68-73</c>) skipped a property another source owned
/// (<c>HttpPartMessagePopulationSource.cs:264-269</c>), and so did the body. Legacy's own test binds
/// <c>{RouteProperty:ADifferentName}</c> from the route value <c>RouteProperty</c>
/// (<c>tests/Blueprint.Tests/Http/HttpMessagePopulation_Middleware/Given_Route_Values.cs:36-40</c>, <c>:83-90</c>).
/// </para>
/// <para>
/// The operations are shaped as Signature365's: Admin Portal's <c>AdminPortalGetIdentityAuthEnforcementOverviewQuery</c>
/// (<c>/identities/{Id:IdentityId}/auth-enforcement-overview</c>), the Portal's <c>PortalGetTenantUserQuery</c>
/// (<c>/tenantUsers/{UserTenantId:TenantId}/{Id}</c>) and <c>PortalMakeTenantOwnerCommand</c>
/// (<c>.../{Id}/make-owner</c>).
/// </para>
/// </remarks>
public sealed class LegacyRoutePlaceholderWireTests
{
    [Fact]
    public async Task A_querys_alternate_named_placeholder_is_bound_from_the_path_and_not_from_the_query_string()
    {
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendAsync(
            HttpMethod.Get,
            "route-placeholders/identities/identity-in-path/auth-enforcement-overview?id=identity-in-query");

        status.Should().Be(200);
        body.Should().Be("\"id=identity-in-path\"");
    }

    [Fact]
    public async Task A_query_sent_by_the_compiled_client_is_answered_for_the_path_values_the_client_put_in_its_url()
    {
        // The compiled client builds the URL from the registered, normalised template, so it already puts UserTenantId in
        // the path; the host has to read it from there.
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendWithCompiledClientAsync(
            new GetRoutePlaceholderTenantUserQuery { UserTenantId = "tenant-1", Id = "user-1" });

        status.Should().Be(200);
        body.Should().Be("\"userTenantId=tenant-1;id=user-1\"");
    }

    [Fact]
    public async Task A_commands_alternate_named_placeholder_is_bound_from_the_path()
    {
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendAsync(
            HttpMethod.Post,
            "route-placeholders/tenant-users/tenant-1/user-1/make-owner",
            "{}");

        status.Should().Be(200);
        body.Should().Be("\"userTenantId=tenant-1;id=user-1\"");
    }

    [Fact]
    public void The_document_publishes_an_alternate_named_placeholder_as_a_required_path_parameter_and_not_as_a_query_one()
    {
        // Legacy's document keyed the path by RoutingUrl (Blueprint.OpenApi/OpenApiDocumentBuilder.cs:96) and made a
        // placeholder property a path parameter, required (:145-152).
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var paths = document.RootElement.GetProperty("paths");

        var identityParameters = Parameters(paths, "/route-placeholders/identities/{Id}/auth-enforcement-overview", "get")
            .Where(static p => string.Equals(p.GetProperty("name").GetString(), "Id", StringComparison.OrdinalIgnoreCase))
            .ToList();

        identityParameters.Should().ContainSingle();
        identityParameters[0].GetProperty("in").GetString().Should().Be("path");
        identityParameters[0].GetProperty("required").GetBoolean().Should().BeTrue();

        var tenantUserParameters = Parameters(paths, "/route-placeholders/tenant-users/{UserTenantId}/{Id}", "get")
            .Where(static p => string.Equals(p.GetProperty("name").GetString(), "UserTenantId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        tenantUserParameters.Should().ContainSingle();
        tenantUserParameters[0].GetProperty("in").GetString().Should().Be("path");
    }

    [Fact]
    public async Task A_root_links_placeholder_spelled_in_another_case_binds_the_property_from_the_path()
    {
        // Signature365 Analytics' IngestCampaignEventCommand: [RootLink("/track/{type}")] on a property named Type.
        // Legacy mapped api/track/{Type} (ApiOperationLink.cs:59-61, :76) and bound Type from that route value.
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendAsync(
            HttpMethod.Post,
            "case-placeholders/track/signup",
            """{"payload":"from-body"}""");

        status.Should().Be(200);
        body.Should().Be("\"type=signup;payload=from-body\"");
    }

    [Fact]
    public async Task A_self_links_placeholder_spelled_in_another_case_binds_the_property_from_the_path()
    {
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendAsync(HttpMethod.Get, "case-placeholders/items/item-1?id=item-in-query");

        status.Should().Be(200);
        body.Should().Be("\"id=item-1\"");
    }

    [Fact]
    public async Task A_query_sent_by_the_compiled_client_puts_the_property_in_the_path_of_a_placeholder_spelled_in_another_case()
    {
        await using var host = await RoutePlaceholderHost.StartAsync();

        var (status, body) = await host.SendWithCompiledClientAsync(new GetCasePlaceholderItemQuery { Id = "item 2" });

        status.Should().Be(200);
        body.Should().Be("\"id=item 2\"");
    }

    [Fact]
    public void The_registered_urls_and_links_use_the_propertys_name_for_a_placeholder_spelled_in_another_case()
    {
        var app = new IntegrationTestBlueprintApp();

        var track = app.FindOperation(typeof(TrackCasePlaceholderCommand))!;

        track.Urls.Select(static u => u.UrlFormat).Should().Equal("/case-placeholders/track/{Type}");

        var item = app.FindOperation(typeof(GetCasePlaceholderItemQuery))!;

        item.Urls.Select(static u => u.UrlFormat).Should().Equal("/case-placeholders/items/{Id}");
        item.Links.Should().ContainSingle()
            .Which.CreateRelativeUrl(new CasePlaceholderApiResource { Id = "item-3" })
            .Should().Be("/case-placeholders/items/item-3");
    }

    [Fact]
    public void The_document_publishes_a_placeholder_spelled_in_another_case_under_the_propertys_name_as_a_path_parameter()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var paths = document.RootElement.GetProperty("paths");

        paths.EnumerateObject().Select(static p => p.Name).Should().NotContain(static name => name.Contains("{type}") || name.Contains("{id}"));

        var trackParameters = Parameters(paths, "/case-placeholders/track/{Type}", "post")
            .Where(static p => p.GetProperty("name").GetString() == "Type")
            .ToList();

        trackParameters.Should().ContainSingle();
        trackParameters[0].GetProperty("in").GetString().Should().Be("path");
        trackParameters[0].GetProperty("required").GetBoolean().Should().BeTrue();

        var itemParameters = Parameters(paths, "/case-placeholders/items/{Id}", "get")
            .Where(static p => p.GetProperty("name").GetString() == "Id")
            .ToList();

        itemParameters.Should().ContainSingle();
        itemParameters[0].GetProperty("in").GetString().Should().Be("path");
    }

    private static IEnumerable<JsonElement> Parameters(JsonElement paths, string path, string method)
    {
        return paths.GetProperty(path).GetProperty(method).TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().ToList()
            : [];
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class RoutePlaceholderHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private RoutePlaceholderHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<RoutePlaceholderHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new RoutePlaceholderHost(app, client);
        }

        public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string url, string? json = null)
        {
            using var request = new HttpRequestMessage(method, url);

            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await this.Client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Builds the request as the generated client does: the URL and query string from the operation's compiled request
        /// feature, which <c>BlueprintHttpClient</c> uses whenever a descriptor has one.
        /// </summary>
        public Task<(int Status, string Body)> SendWithCompiledClientAsync(IOperation operation)
        {
            var descriptor = new IntegrationTestBlueprintApp().FindOperation(operation.GetType())!;
            var compiled = descriptor.GetFeatureData<CompiledHttpRequestFeatureData>()!;
            var url = compiled.BuildUrl(operation)!;

            // As BlueprintHttpClient does, which appends a query string only when the operation has one to build.
            if (compiled.AppendQueryString is not null)
            {
                url = compiled.AppendQueryString(operation, url);
            }

            return this.SendAsync(HttpMethod.Get, url.TrimStart('/'));
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>Describes the route values a legacy-placeholder operation's handler was given.</summary>
internal static class RoutePlaceholderHandlers
{
    public static string Describe(string? userTenantId, string? id)
    {
        return $"userTenantId={userTenantId ?? "(unset)"};id={id ?? "(unset)"}";
    }
}

/// <summary>
/// Shaped as Signature365 Admin Portal's <c>AdminPortalGetIdentityAuthEnforcementOverviewQuery</c>: a self link whose one
/// placeholder names an alternate resource property.
/// </summary>
[AllowAnonymous]
[SelfLink(typeof(RoutePlaceholderIdentityApiResource), "/route-placeholders/identities/{Id:IdentityId}/auth-enforcement-overview")]
public partial class GetRoutePlaceholderIdentityQuery : IQuery<string>
{
    public string Id { get; set; } = default!;

    public static string Handle(GetRoutePlaceholderIdentityQuery query)
    {
        return "id=" + (query.Id ?? "(unset)");
    }
}

/// <summary>The resource <see cref="GetRoutePlaceholderIdentityQuery"/> self-links, by its <c>IdentityId</c>.</summary>
public partial class RoutePlaceholderIdentityApiResource : ApiResource
{
    public string IdentityId { get; set; } = null!;
}

/// <summary>
/// Shaped as Signature365 Portal's <c>PortalGetTenantUserQuery</c>: a self link with an alternate-named placeholder and a
/// plain one.
/// </summary>
[AllowAnonymous]
[SelfLink(typeof(RoutePlaceholderTenantUserApiResource), "/route-placeholders/tenant-users/{UserTenantId:TenantId}/{Id}")]
public partial class GetRoutePlaceholderTenantUserQuery : IQuery<string>
{
    public string UserTenantId { get; set; } = default!;

    public string Id { get; set; } = default!;

    public static string Handle(GetRoutePlaceholderTenantUserQuery query)
    {
        return RoutePlaceholderHandlers.Describe(query.UserTenantId, query.Id);
    }
}

/// <summary>Shaped as Signature365 Portal's <c>PortalMakeTenantOwnerCommand</c>: a command linked with the same placeholders.</summary>
[AllowAnonymous]
[Link(typeof(RoutePlaceholderTenantUserApiResource), "/route-placeholders/tenant-users/{UserTenantId:TenantId}/{Id}/make-owner", Rel = "makeOwner")]
public partial class RoutePlaceholderMakeTenantOwnerCommand : ICommand<string>
{
    public string UserTenantId { get; set; } = default!;

    public string Id { get; set; } = default!;

    public static string Handle(RoutePlaceholderMakeTenantOwnerCommand command)
    {
        return RoutePlaceholderHandlers.Describe(command.UserTenantId, command.Id);
    }
}

/// <summary>
/// Shaped as Signature365 Analytics' <c>IngestCampaignEventCommand</c>: a root link whose placeholder is spelled in another
/// case than its property.
/// </summary>
[AllowAnonymous]
[RootLink("/case-placeholders/track/{type}")]
public partial class TrackCasePlaceholderCommand : ICommand<string>
{
    public string Type { get; set; } = default!;

    public string? Payload { get; set; }

    public static string Handle(TrackCasePlaceholderCommand command)
    {
        return $"type={command.Type ?? "(unset)"};payload={command.Payload ?? "(unset)"}";
    }
}

/// <summary>A self link whose placeholder is spelled in another case than the operation's and the resource's property.</summary>
[AllowAnonymous]
[SelfLink(typeof(CasePlaceholderApiResource), "/case-placeholders/items/{id}")]
public partial class GetCasePlaceholderItemQuery : IQuery<string>
{
    public string Id { get; set; } = default!;

    public static string Handle(GetCasePlaceholderItemQuery query)
    {
        return "id=" + (query.Id ?? "(unset)");
    }
}

/// <summary>The resource <see cref="GetCasePlaceholderItemQuery"/> self-links, by its <c>Id</c>.</summary>
public partial class CasePlaceholderApiResource : ApiResource
{
    public string Id { get; set; } = null!;
}

/// <summary>The tenant user resource, linked by its <c>TenantId</c> and <c>Id</c>.</summary>
public partial class RoutePlaceholderTenantUserApiResource : ApiResource
{
    public string TenantId { get; set; } = null!;

    public string Id { get; set; } = null!;
}
