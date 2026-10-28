using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves how <see cref="IApiAuthoriserAggregator.CanShowLinkAsync"/> asks the resource authorization providers: all-of,
/// only for an operation that declares scope pairs and only with a resource, and refusing when nothing can judge the link.
/// </summary>
public sealed class ResourceProviderLinkVisibilityTests
{
    private static readonly LinkAuthTenantApiResource GrantedTenant = new() { Id = "a", TenantId = "ten_1" };

    [Fact]
    public async Task Every_provider_must_allow_the_link()
    {
        var first = new KeyProvider("/LinkAuth/Tenants/ten_1");
        var second = new KeyProvider();

        var result = await CanShowAsync(GrantedTenant, [first, second]);

        result.IsAllowed.Should().BeFalse();
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(1);
    }

    [Fact]
    public async Task The_first_denial_is_the_answer_and_later_providers_are_not_asked()
    {
        var first = new KeyProvider();
        var second = new KeyProvider("/LinkAuth/Tenants/ten_1");

        var result = await CanShowAsync(GrantedTenant, [first, second]);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("not granted");
        second.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_permissioned_link_is_hidden_when_no_provider_is_registered()
    {
        var result = await CanShowAsync(GrantedTenant, []);

        result.IsAllowed.Should().BeFalse("with nothing to judge the operation's scope pairs, the link is refused, as its execution is");
    }

    [Fact]
    public async Task A_link_on_a_resource_type_the_operation_does_not_link_from_is_hidden()
    {
        var provider = new KeyProvider("/LinkAuth/Tenants/ten_1");

        var result = await CanShowAsync(new LinkTestApiResource { Id = "x", Name = "x" }, [provider]);

        result.IsAllowed.Should().BeFalse("no key can be resolved against a resource the operation does not link from");
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Providers_are_not_asked_without_a_resource()
    {
        // A link with no resource is listing mode's to decide (#81), which this aggregator does not ask yet.
        var provider = new KeyProvider();

        var result = await CanShowAsync(null, [provider]);

        result.IsAllowed.Should().BeTrue();
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Providers_are_not_asked_for_an_operation_without_scope_pairs()
    {
        var provider = new KeyProvider();

        var result = await CanShowAsync(new LinkTestApiResource { Id = "x", Name = "x" }, [provider], GetLinkTestResourceQuery.Configure());

        result.IsAllowed.Should().BeTrue();
        provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_principal_on_the_context_the_provider_is_given_an_unauthenticated_one()
    {
        var provider = new KeyProvider("/LinkAuth/Tenants/ten_1");

        await CanShowAsync(GrantedTenant, [provider], principal: null);

        provider.LastPrincipal.Should().NotBeNull();
        provider.LastPrincipal!.Identity?.IsAuthenticated.Should().NotBe(true);
    }

    [Theory]
    [InlineData(null, "/LinkAuth/Accounts/acc_ctx/Departments/dept_1/Regions/ten_9/Seq/7")]
    [InlineData("eu", "/LinkAuth/Accounts/acc_ctx/Departments/dept_1/Regions/eu/Seq/7")]
    public async Task Caller_tokens_resolve_as_execution_resolves_them_and_value_types_are_written(string? region, string expectedKey)
    {
        // AccountId comes from the user's context before the claim of that name; department and region from claims;
        // region falls back to the resource's TenantId when the caller has none; Sequence is an int.
        var claims = new List<Claim> { new("AccountId", "acc_claim"), new("department", "dept_1") };

        if (region is not null)
        {
            claims.Add(new Claim("region", region));
        }

        var provider = new KeyProvider(expectedKey);
        var app = new IntegrationTestBlueprintApp();

        var result = await CanShowAsync(
            new LinkAuthAccountApiResource { Id = "x", TenantId = "ten_9", Sequence = 7 },
            [provider],
            app.FindOperation(typeof(RenameLinkAuthAccountCommand))!,
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            new FixedUserAuthorisationContext("acc_ctx"));

        provider.LastKey.Should().Be(expectedKey);
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Each_token_resolves_by_its_full_property_path_against_the_resource()
    {
        // Tenant.Id is a struct's int on the resource, so it is written without null-conditional access. The resource's
        // Code has no Value, so the whole path is missing and the CodeId fallback is used. The two region tokens share a
        // primary: the first falls back to the resource's TenantId; the second's OwnerId does not exist on the resource,
        // so it is empty, and does not take the first's fallback away.
        const string expectedKey = "/LinkAuth/Shapes/42/c9/ten_1/";
        var provider = new KeyProvider(expectedKey);
        var app = new IntegrationTestBlueprintApp();

        var result = await CanShowAsync(
            new LinkAuthShapeApiResource
            {
                Id = "x",
                Tenant = new LinkAuthTenantKey(42),
                Code = new LinkAuthCodeName { Name = "unused" },
                CodeId = "c9",
                TenantId = "ten_1",
            },
            [provider],
            app.FindOperation(typeof(TouchLinkAuthShapeCommand))!,
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "Test")));

        provider.LastKey.Should().Be(expectedKey);
        result.IsAllowed.Should().BeTrue();
    }

    private static async Task<ExecutionAllowed> CanShowAsync(
        object? resource,
        IResourceAuthorizationProvider[] providers,
        ApiOperationDescriptor? descriptor = null,
        ClaimsPrincipal? principal = null,
        IUserAuthorisationContext? user = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IApiAuthoriserAggregator, ApiAuthoriserAggregator>();

        foreach (var provider in providers)
        {
            services.AddSingleton<IResourceAuthorizationProvider>(provider);
        }

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        var aggregator = scope.ServiceProvider.GetRequiredService<IApiAuthoriserAggregator>();
        var app = new IntegrationTestBlueprintApp();
        descriptor ??= app.FindOperation(typeof(ArchiveLinkAuthResourceCommand))!;

        // The context is the outer operation's, as link population's is; the linked operation is the descriptor given.
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(ListLinkAuthTenantsQuery))!, CancellationToken.None)
        {
            UserPrincipal = principal,
            UserAuthorisationContext = user,
        };

        return await aggregator.CanShowLinkAsync(context, descriptor, resource);
    }

    /// <summary>A user's context whose account is fixed, as an app's user context factory gives it.</summary>
    private sealed class FixedUserAuthorisationContext(string accountId) : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => "user-1";

        public string? IdentityId => null;

        public string AccountId => accountId;

        public string? Email => null;

        public string? Name => null;

        public void PopulateMetadata(Action<string, object> add)
        {
        }
    }

    /// <summary>Allows a link whose resolved key is one of the given keys.</summary>
    private sealed class KeyProvider(params string[] grantedKeys) : IResourceAuthorizationProvider
    {
        public int Calls { get; private set; }

        public ClaimsPrincipal? LastPrincipal { get; private set; }

        public string? LastKey { get; private set; }

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            this.LastPrincipal = principal;
            this.LastKey = request.ScopePairs.FirstOrDefault().ExpandedResourceKey;

            return new ValueTask<ExecutionAllowed>(
                request.ScopePairs.Any(p => grantedKeys.Contains(p.ExpandedResourceKey))
                    ? ExecutionAllowed.Yes
                    : ExecutionAllowed.No("not granted", "You may not do this", ExecutionAllowedFailureType.Authorisation));
        }
    }
}
