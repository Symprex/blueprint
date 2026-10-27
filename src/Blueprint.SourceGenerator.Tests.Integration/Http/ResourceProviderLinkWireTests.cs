using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the registered resource authorization providers decide whether a link is written, with the linked
/// operation's resource keys resolved against the resource the link is written on (Symprex/blueprint#79).
/// </summary>
/// <remarks>
/// Each test runs the app as a consumer runs it, <c>AddBlueprintApi</c> and <c>MapBlueprintApi</c> on Kestrel, so the links
/// are made by the real link population with the caller the request authenticated.
/// </remarks>
public sealed class ResourceProviderLinkWireTests
{
    [Fact]
    public async Task Each_item_of_a_list_shows_the_link_only_when_the_provider_grants_its_own_key()
    {
        await using var host = await LinkAuthHost.StartAsync();

        using var body = JsonDocument.Parse(await host.GetAsync("link-auth/tenants"));

        var items = body.RootElement.GetProperty("values").EnumerateArray().ToArray();
        items.Select(static i => i.GetProperty("$links").TryGetProperty("archive", out _))
            .Should().Equal([true, false, true], "only /LinkAuth/Tenants/ten_1 is granted, and each item is decided by its own key");
    }

    [Fact]
    public async Task The_provider_is_asked_in_link_mode_for_the_linked_operation_with_the_key_resolved_against_the_resource()
    {
        await using var host = await LinkAuthHost.StartAsync();

        await host.GetAsync("link-auth/tenants");

        var linkRequests = host.Requests.Where(static r => r.Mode == ResourceAuthorizationMode.Link).ToList();

        linkRequests.Should().HaveCount(3);
        linkRequests.Should().OnlyContain(static r => r.OperationType == typeof(ArchiveLinkAuthResourceCommand));
        linkRequests.Select(static r => r.Key).Should().Equal(
            "/LinkAuth/Tenants/ten_1",
            "/LinkAuth/Tenants/ten_2",
            "/LinkAuth/Tenants/ten_1");
        linkRequests.Should().OnlyContain(static r => r.IsAuthenticated, "the link is decided for the caller the request authenticated");
    }

    [Fact]
    public async Task A_resource_that_has_the_primary_property_never_falls_back_even_when_its_value_is_null()
    {
        // LinkAuthUserApiResource has UserTenantId (null here) and TenantId (granted ten_1). The fallback is chosen by whether
        // the property exists, as Signature 365 chooses it, so the key is /LinkAuth/Tenants/ and the link is hidden.
        await using var host = await LinkAuthHost.StartAsync();

        using var body = JsonDocument.Parse(await host.GetAsync("link-auth/user"));

        body.RootElement.GetProperty("$links").TryGetProperty("archive", out _).Should().BeFalse();
        host.Requests.Where(static r => r.Mode == ResourceAuthorizationMode.Link).Select(static r => r.Key)
            .Should().Equal("/LinkAuth/Tenants/");
    }

    private sealed record RecordedRequest(ResourceAuthorizationMode Mode, Type OperationType, string? Key, bool IsAuthenticated);

    /// <summary>
    /// Grants <c>/LinkAuth/Tenants/ten_1</c> for links and allows every execution, recording each link request.
    /// </summary>
    private sealed class GrantingProvider(ConcurrentQueue<RecordedRequest> requests) : IResourceAuthorizationProvider
    {
        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Mode != ResourceAuthorizationMode.Link)
            {
                return new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes);
            }

            var key = request.ScopePairs.Single().ExpandedResourceKey;
            requests.Enqueue(new RecordedRequest(request.Mode, request.Descriptor.OperationType, key, principal.Identity?.IsAuthenticated == true));

            return new ValueTask<ExecutionAllowed>(
                key == "/LinkAuth/Tenants/ten_1"
                    ? ExecutionAllowed.Yes
                    : ExecutionAllowed.No("not granted", "You may not archive this", ExecutionAllowedFailureType.Authorisation));
        }
    }

    private sealed class LinkAuthHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LinkAuthHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<RecordedRequest> requests)
        {
            this._app = app;
            this.Client = client;
            this.Requests = requests;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<RecordedRequest> Requests { get; }

        public static async Task<LinkAuthHost> StartAsync()
        {
            var requests = new ConcurrentQueue<RecordedRequest>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton<IResourceAuthorizationProvider>(new GrantingProvider(requests));
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(System.Security.Claims.ClaimTypes.Name, "user-1")], "Test"));

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new LinkAuthHost(app, client, requests);
        }

        public async Task<string> GetAsync(string path)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await this.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().Be(200, "the body was {0}", body);

            return body;
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A tenant: it has the fallback <c>TenantId</c> but not the primary <c>UserTenantId</c>.</summary>
public partial class LinkAuthTenantApiResource : ApiResource
{
    public string Id { get; init; } = null!;

    public string TenantId { get; init; } = null!;
}

/// <summary>A user: it has both the primary <c>UserTenantId</c> and the fallback <c>TenantId</c>.</summary>
public partial class LinkAuthUserApiResource : ApiResource
{
    public string Id { get; init; } = null!;

    public string? UserTenantId { get; init; }

    public string TenantId { get; init; } = null!;
}

/// <summary>Lists three tenants, two of them the granted <c>ten_1</c>.</summary>
[AllowAnonymous]
[RootLink("/link-auth/tenants")]
public partial class ListLinkAuthTenantsQuery : IQuery<ListApiResource<LinkAuthTenantApiResource>>
{
    public static ListApiResource<LinkAuthTenantApiResource> Handle(ListLinkAuthTenantsQuery query)
    {
        return new ListApiResource<LinkAuthTenantApiResource>(
        [
            new LinkAuthTenantApiResource { Id = "a", TenantId = "ten_1" },
            new LinkAuthTenantApiResource { Id = "b", TenantId = "ten_2" },
            new LinkAuthTenantApiResource { Id = "c", TenantId = "ten_1" },
        ]);
    }
}

/// <summary>Gets a user with no <c>UserTenantId</c> whose <c>TenantId</c> is the granted <c>ten_1</c>.</summary>
[AllowAnonymous]
[RootLink("/link-auth/user")]
public partial class GetLinkAuthUserQuery : IQuery<LinkAuthUserApiResource>
{
    public static LinkAuthUserApiResource Handle(GetLinkAuthUserQuery query)
    {
        return new LinkAuthUserApiResource { Id = "u", UserTenantId = null, TenantId = "ten_1" };
    }
}

/// <summary>An account, with a value-type property for a key segment.</summary>
public partial class LinkAuthAccountApiResource : ApiResource
{
    public string Id { get; init; } = null!;

    public string TenantId { get; init; } = null!;

    public int Sequence { get; init; }
}

/// <summary>
/// Renames an account. Its key takes the caller's account from the user's context, a claim, a claim falling back to the
/// resource's <c>TenantId</c>, and a value-type property.
/// </summary>
[Resource("linkAuthAccount", "rename", "/LinkAuth/Accounts/{Auth.AccountId}/Departments/{Auth.department}/Regions/{Auth.region:TenantId}/Seq/{Sequence}")]
[Link(typeof(LinkAuthAccountApiResource), "/link-auth/accounts/{Id}/rename", "rename")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class RenameLinkAuthAccountCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public string TenantId { get; set; } = null!;

    public int Sequence { get; set; }

    public static string Handle(RenameLinkAuthAccountCommand command)
    {
        return "renamed";
    }
}

/// <summary>
/// Archives a tenant or a user. Its key's primary, <c>UserTenantId</c>, is its own property; the fallback,
/// <c>TenantId</c>, is not, but a tenant uses it because a tenant lacks the primary, so BPAU010 does not fire.
/// </summary>
[Resource("linkAuthResource", "archive", "/LinkAuth/Tenants/{UserTenantId:TenantId}")]
[Link(typeof(LinkAuthTenantApiResource), "/link-auth/tenants/{Id}/archive", "archive")]
[Link(typeof(LinkAuthUserApiResource), "/link-auth/users/{Id}/archive", "archive")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class ArchiveLinkAuthResourceCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public string? UserTenantId { get; set; }

    public static string Handle(ArchiveLinkAuthResourceCommand command)
    {
        return "archived";
    }
}
