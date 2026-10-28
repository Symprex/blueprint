using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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
/// Proves <see cref="ResourceKeyAuthorizationProvider"/> through the generated pipeline, served by Kestrel as a consumer
/// serves it: executions and links decided by the caller's grants, with partial keys made absolute by an expander or by the
/// linked resource's own keys, and <see cref="IResourceAccessChecker"/> asked directly by a handler (Symprex/blueprint#80).
/// </summary>
/// <remarks>
/// The caller's grants are claims read by the built-in <see cref="ScopePairClaimResourceGrantSource"/>, and
/// <see cref="GrantFolderKeyExpander"/> places folders <c>f1</c> and <c>f3</c> under <c>ten_1</c>, <c>f2</c> under
/// <c>ten_2</c> and <c>f4</c> under <c>ten_12</c>.
/// </remarks>
public sealed class ResourceKeyAuthorizationWireTests
{
    private const string RenameInTenantOne = "/GrantTenants/ten_1/*;grantFolder::rename";

    [Theory]
    [InlineData("f1", RenameInTenantOne, 200)]
    [InlineData("f2", RenameInTenantOne, 403)]
    [InlineData("f1", "/GrantTenants/ten_1/*;grantFolder::archive", 403)]
    [InlineData("f4", RenameInTenantOne, 403)]
    [InlineData("f9", "/*;grantFolder::rename", 403)]
    [InlineData("f1", "", 403)]
    public async Task An_execution_is_allowed_only_when_a_grant_covers_a_key_its_partial_key_expands_to(string folderId, string grants, int expectedStatus)
    {
        // f2 is under ten_2; the archive grant is for another verb; f4 is under ten_12, which only shares a prefix with
        // ten_1; the expander knows no f9, so it has no key for even the root grant to cover
        await using var host = await GrantHost.StartAsync();

        (await host.PostStatusAsync($"resource-grant/folders/{folderId}/rename", grants)).Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Each_item_shows_a_link_only_when_a_grant_covers_its_own_key()
    {
        await using var host = await GrantHost.StartAsync();

        using var body = JsonDocument.Parse(await host.GetAsync(
            "resource-grant/folders",
            RenameInTenantOne,
            "/GrantTenants/ten_1/Folders/f3;grantFolder::archive"));

        var items = body.RootElement.GetProperty("values").EnumerateArray().ToArray();

        items.Select(static i => i.GetProperty("$links").TryGetProperty("rename", out _))
            .Should().Equal([true, false, true], "rename is granted across ten_1, which holds f1 and f3");
        items.Select(static i => i.GetProperty("$links").TryGetProperty("archive", out _))
            .Should().Equal([false, false, true], "archive is granted on f3 alone");
    }

    [Fact]
    public async Task A_link_whose_partial_key_ends_one_of_the_resources_own_keys_is_decided_without_the_expanders()
    {
        // Rename's Folders/{Id} ends each folder's own key, so only archive's Archives/{Id}, which ends none, is expanded
        await using var host = await GrantHost.StartAsync();

        await host.GetAsync("resource-grant/folders", RenameInTenantOne);

        host.Expansions.Should().BeEquivalentTo(["Archives/f1", "Archives/f2", "Archives/f3"]);
    }

    [Theory]
    [InlineData("f1", "yes")]
    [InlineData("f2", "no")]
    public async Task A_handler_asks_the_access_checker_directly(string folderId, string expected)
    {
        await using var host = await GrantHost.StartAsync();

        var body = await host.GetAsync($"resource-grant/folders/{folderId}/can-rename", RenameInTenantOne);

        JsonSerializer.Deserialize<string>(body).Should().Be(expected);
    }

    private sealed class GrantHost : IAsyncDisposable
    {
        private const string GrantsHeader = "X-Test-Grants";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private GrantHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<string> expansions)
        {
            this._app = app;
            this._client = client;
            this.Expansions = expansions;
        }

        public ConcurrentQueue<string> Expansions { get; }

        public static async Task<GrantHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            var directory = new GrantFolderDirectory();
            builder.Services.AddSingleton(directory);

            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint => blueprint
                .AddResourceKeyAuthorization()
                .AddResourceGrantSource(new ScopePairClaimResourceGrantSource("grant", "::"))
                .AddResourceKeyExpander<GrantFolderKeyExpander>());

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                var claims = new List<Claim> { new(System.Security.Claims.ClaimTypes.Name, "user-1") };

                foreach (var grant in httpContext.Request.Headers[GrantsHeader].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    claims.Add(new Claim("grant", grant));
                }

                httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new GrantHost(app, client, directory.Expansions);
        }

        public async Task<string> GetAsync(string path, params string[] grants)
        {
            using var request = CreateRequest(System.Net.Http.HttpMethod.Get, path, grants);
            using var response = await this._client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().Be(200, "the body was {0}", body);

            return body;
        }

        public async Task<int> PostStatusAsync(string path, string grants)
        {
            using var request = CreateRequest(System.Net.Http.HttpMethod.Post, path, [grants]);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await this._client.SendAsync(request);

            return (int)response.StatusCode;
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }

        private static System.Net.Http.HttpRequestMessage CreateRequest(System.Net.Http.HttpMethod method, string path, string[] grants)
        {
            var request = new System.Net.Http.HttpRequestMessage(method, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add(GrantsHeader, string.Join(' ', grants));

            return request;
        }
    }
}

/// <summary>Where each folder lives, and the partial keys <see cref="GrantFolderKeyExpander"/> has been asked to expand.</summary>
public sealed class GrantFolderDirectory
{
    private static readonly Dictionary<string, string> _tenantByFolder = new(StringComparer.Ordinal)
    {
        ["f1"] = "ten_1",
        ["f2"] = "ten_2",
        ["f3"] = "ten_1",
        ["f4"] = "ten_12",
    };

    public ConcurrentQueue<string> Expansions { get; } = new();

    public string? TenantOf(string folderId)
    {
        return _tenantByFolder.GetValueOrDefault(folderId);
    }
}

/// <summary>
/// Expands <c>Folders/{id}</c> and <c>Archives/{id}</c> to the folder's absolute key under its tenant, or to no key for a
/// folder it does not know, and declines every other key.
/// </summary>
public sealed class GrantFolderKeyExpander(GrantFolderDirectory directory) : IResourceKeyExpander
{
    public ValueTask<IReadOnlyList<ResourceKey>?> ExpandAsync(ResourceKey partialKey, ApiOperationContext context, CancellationToken cancellationToken)
    {
        directory.Expansions.Enqueue(partialKey.Value);

        var value = partialKey.Value;
        var folderId = value.StartsWith("Folders/", StringComparison.Ordinal) ? value["Folders/".Length..]
            : value.StartsWith("Archives/", StringComparison.Ordinal) ? value["Archives/".Length..]
            : null;

        if (folderId is null)
        {
            return new ValueTask<IReadOnlyList<ResourceKey>?>(result: null);
        }

        IReadOnlyList<ResourceKey> keys = directory.TenantOf(folderId) is { } tenantId
            ? [new ResourceKey($"/GrantTenants/{tenantId}/Folders/{folderId}")]
            : [];

        return new ValueTask<IReadOnlyList<ResourceKey>?>(keys);
    }
}

/// <summary>A folder, whose own resource key places it under its tenant.</summary>
public partial class GrantFolderApiResource : ApiResource, IHaveResourceKeys
{
    public string Id { get; init; } = null!;

    public string TenantId { get; init; } = null!;

    IEnumerable<string> IHaveResourceKeys.ResourceKeys => [$"/GrantTenants/{this.TenantId}/Folders/{this.Id}"];
}

/// <summary>Lists three folders: <c>f1</c> and <c>f3</c> under <c>ten_1</c>, and <c>f2</c> under <c>ten_2</c>.</summary>
[AllowAnonymous]
[RootLink("/resource-grant/folders")]
public partial class ListGrantFoldersQuery : IQuery<ListApiResource<GrantFolderApiResource>>
{
    public static ListApiResource<GrantFolderApiResource> Handle(ListGrantFoldersQuery query)
    {
        return new ListApiResource<GrantFolderApiResource>(
        [
            new GrantFolderApiResource { Id = "f1", TenantId = "ten_1" },
            new GrantFolderApiResource { Id = "f2", TenantId = "ten_2" },
            new GrantFolderApiResource { Id = "f3", TenantId = "ten_1" },
        ]);
    }
}

/// <summary>Renames a folder. Its key is partial, and ends the folder's own key.</summary>
[Resource("grantFolder", "rename", "Folders/{Id}")]
[Link(typeof(GrantFolderApiResource), "/resource-grant/folders/{Id}/rename", "rename")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class RenameGrantFolderCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(RenameGrantFolderCommand command)
    {
        return "renamed";
    }
}

/// <summary>Archives a folder. Its key is partial, and ends none of the folder's own keys, so only an expander places it.</summary>
[Resource("grantFolder", "archive", "Archives/{Id}")]
[Link(typeof(GrantFolderApiResource), "/resource-grant/folders/{Id}/archive", "archive")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class ArchiveGrantFolderCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(ArchiveGrantFolderCommand command)
    {
        return "archived";
    }
}

/// <summary>
/// Answers whether the caller may rename a folder, by asking <see cref="IResourceAccessChecker"/> itself with the scope pair
/// <see cref="RenameGrantFolderCommand"/> declares, as app code filtering by access does.
/// </summary>
[RootLink("/resource-grant/folders/{FolderId}/can-rename")]
public partial class CanRenameGrantFolderQuery : IQuery<string>
{
    public string FolderId { get; set; } = null!;

    public static async Task<string> HandleAsync(CanRenameGrantFolderQuery query, IResourceAccessChecker checker, ApiOperationContext context)
    {
        var rename = context.App.FindOperation(typeof(RenameGrantFolderCommand))!;
        var declared = rename.GetFeatureData<OperationAuthorizationFeatureData>()!.ScopePairs[0];
        var key = new ResourceKey($"Folders/{query.FolderId}");

        var result = await checker.HasAccessAsync(
            context,
            context.UserPrincipal!,
            new ResourceDemand(rename, key),
            new ResolvedScopePair(declared.Noun, declared.Verb, declared.ResourceKeyTemplate, key.Value),
            context.OperationCancelled);

        return result.IsAllowed ? "yes" : "no";
    }
}
