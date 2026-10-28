using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves how <see cref="ResourceKeyAuthorizationProvider"/> and the default <see cref="IResourceAccessChecker"/> decide a
/// scope pair by the caller's grants on its resource key: matching, partial-key expansion, a linked resource's own keys,
/// combined grant sources, and who is refused before any grant is read (Symprex/blueprint#80).
/// </summary>
public sealed class ResourceKeyAuthorizationProviderTests
{
    private static readonly IntegrationTestBlueprintApp App = new();
    private static readonly ApiOperationDescriptor Rename = App.FindOperation(typeof(RenameGrantFolderCommand))!;
    private static readonly ApiOperationDescriptor ListFolders = App.FindOperation(typeof(ListGrantFoldersQuery))!;

    // ─── Matching ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("/T/ten_1/Folders/f1", true)]
    [InlineData("/T/ten_1/Folders/f1/Items/i1", false)]
    [InlineData("/T/ten_1/Folders", false)]
    [InlineData("/T/ten_1/Folders/F1", false)]
    public async Task An_exact_grant_covers_its_own_key_alone(string key, bool allowed)
    {
        var result = await AuthorizeAsync(Execute(key), b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/Folders/f1")));

        result.IsAllowed.Should().Be(allowed);
    }

    [Theory]
    [InlineData("/T/ten_1", true)]
    [InlineData("/T/ten_1/Folders/f1", true)]
    [InlineData("/T/ten_1/Folders/f1/Items/i1", true)]
    [InlineData("/T", false)]
    [InlineData("/T/ten_2/Folders/f1", false)]
    public async Task A_descendants_grant_covers_its_base_and_every_key_beneath_it(string key, bool allowed)
    {
        var result = await AuthorizeAsync(Execute(key), b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().Be(allowed);
    }

    [Theory]
    [InlineData("/T/ten_12")]
    [InlineData("/T/ten_12/Folders/f1")]
    [InlineData("/T/ten_1x")]
    public async Task A_sibling_that_only_shares_a_prefix_with_a_descendants_grant_is_denied(string key)
    {
        var result = await AuthorizeAsync(Execute(key), b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().BeFalse("the wildcard stops at a segment boundary");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Message.Should().Be(ResourceKeyAuthorizationProvider.DeniedMessage);
        result.Reason.Should().Contain(key);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/T/ten_1")]
    [InlineData("/Anything/at/all")]
    public async Task The_root_grant_covers_every_absolute_key(string key)
    {
        var result = await AuthorizeAsync(Execute(key), b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        result.IsAllowed.Should().BeTrue();
    }

    // ─── Partial keys and expanders ───────────────────────────────────

    [Fact]
    public async Task An_absolute_key_is_matched_as_it_is_without_asking_the_expanders()
    {
        var expander = Expanding("/T/ten_2/Folders/f1");

        var result = await AuthorizeAsync(
            Execute("/T/ten_1/Folders/f1"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")).Services.AddScoped<IResourceKeyExpander>(_ => expander));

        result.IsAllowed.Should().BeTrue();
        expander.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_partial_key_is_decided_by_the_keys_its_expander_gives()
    {
        var declining = Declining();
        var expanding = Expanding("/T/ten_2/Folders/f1", "/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            Execute("Folders/f1"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")).Services
                .AddScoped<IResourceKeyExpander>(_ => declining)
                .AddScoped<IResourceKeyExpander>(_ => expanding));

        result.IsAllowed.Should().BeTrue("a grant covers one of the keys the partial key expands to");
        declining.LastKey.Should().Be(new ResourceKey("Folders/f1"));
        expanding.LastKey.Should().Be(new ResourceKey("Folders/f1"));
    }

    [Fact]
    public async Task Expanders_are_asked_in_registration_order_and_the_first_answer_wins_even_when_a_later_one_would_allow()
    {
        var declining = Declining();
        var first = Expanding("/T/ten_2/Folders/f1");
        var later = Expanding("/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            Execute("Folders/f1"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")).Services
                .AddScoped<IResourceKeyExpander>(_ => declining)
                .AddScoped<IResourceKeyExpander>(_ => first)
                .AddScoped<IResourceKeyExpander>(_ => later));

        result.IsAllowed.Should().BeFalse("the first expander to answer places the folder under ten_2, which is not granted");
        declining.Calls.Should().Be(1);
        first.Calls.Should().Be(1);
        later.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_expander_answering_with_no_keys_has_handled_the_key_so_it_is_denied()
    {
        var empty = Expanding();
        var later = Expanding("/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            Execute("Folders/f9"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/*")).Services
                .AddScoped<IResourceKeyExpander>(_ => empty)
                .AddScoped<IResourceKeyExpander>(_ => later));

        result.IsAllowed.Should().BeFalse("an empty expansion leaves no key for even the root grant to cover");
        later.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_partial_key_no_expander_handles_is_denied_with_a_reason_that_names_it()
    {
        var declining = Declining();

        var result = await AuthorizeAsync(
            Execute("Folders/f9"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/*")).Services.AddScoped<IResourceKeyExpander>(_ => declining));

        result.IsAllowed.Should().BeFalse("a grant only ever covers an absolute key, the root grant included");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Message.Should().Be(ResourceKeyAuthorizationProvider.DeniedMessage);
        result.Reason.Should().Contain("'Folders/f9'").And.Contain("no IResourceKeyExpander handled");
        declining.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_partial_key_is_denied_when_no_expander_is_registered()
    {
        var result = await AuthorizeAsync(Execute("Folders/f9"), b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("'Folders/f9'").And.Contain("no IResourceKeyExpander handled");
    }

    [Fact]
    public async Task A_key_that_resolved_empty_is_denied_without_asking_the_expanders()
    {
        var expander = Expanding("/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            Execute(string.Empty),
            b => b.AddResourceGrantSource(new FixedGrantSource("/*")).Services.AddScoped<IResourceKeyExpander>(_ => expander));

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        expander.Calls.Should().Be(0);
    }

    // ─── Grant sources ────────────────────────────────────────────────

    [Fact]
    public async Task The_grants_of_every_source_are_combined()
    {
        var first = new FixedGrantSource("/T/ten_1/*");
        var second = new FixedGrantSource("/T/ten_2/*");

        var result = await AuthorizeAsync(Execute("/T/ten_2/Folders/f1"), b => b.AddResourceGrantSource(first).AddResourceGrantSource(second));

        result.IsAllowed.Should().BeTrue("the second source's grant covers the key");
        second.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_key_no_source_covers_is_denied_after_every_source_is_asked()
    {
        var first = new FixedGrantSource("/T/ten_1/*");
        var second = new FixedGrantSource("/T/ten_2/*");

        var result = await AuthorizeAsync(Execute("/T/ten_3/Folders/f1"), b => b.AddResourceGrantSource(first).AddResourceGrantSource(second));

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("/T/ten_3/Folders/f1");
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Every_key_is_denied_when_no_grant_source_is_registered()
    {
        var result = await AuthorizeAsync(Execute("/T/ten_1"), static _ => { });

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("IResourceGrantSource");
    }

    [Fact]
    public async Task A_source_is_given_the_operation_and_the_scope_pair_being_authorised()
    {
        var source = new FixedGrantSource("/T/ten_1/*");
        var pair = new ResolvedScopePair("grantFolder", "rename", "/T/{TenantId}", "/T/ten_1");

        await AuthorizeAsync(ResourceAuthorizationRequest.Execute(Rename, [pair]), b => b.AddResourceGrantSource(source));

        source.LastOperation.Should().BeSameAs(Rename);
        source.LastPair.Should().Be(pair);
    }

    // ─── Links ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_link_is_decided_for_the_linked_operation_not_the_operation_whose_response_carries_it()
    {
        // The context is the list query's, as link population's is; only the linked rename holds the grant
        var request = ResourceAuthorizationRequest.Link(Rename, [Pair("/T/ten_1/Folders/f1")], new KeyedResource());

        var result = await AuthorizeAsync(
            request,
            b => b.AddResourceGrantSource(new OperationGrantSource(typeof(RenameGrantFolderCommand), "/T/ten_1/*")),
            contextDescriptor: ListFolders);

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("Folders/f1")]
    [InlineData("folders/F1")]
    public async Task A_partial_link_key_is_replaced_by_the_resource_key_that_ends_with_it_compared_case_insensitively(string partialKey)
    {
        var expander = Expanding("/T/ten_2/Folders/f1");
        var resource = new KeyedResource("/T/ten_1/Groups/g1", "/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Link(Rename, [Pair(partialKey)], resource),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/Folders/f1")).Services.AddScoped<IResourceKeyExpander>(_ => expander));

        result.IsAllowed.Should().BeTrue("the resource's own key, in its own case, is matched exactly");
        expander.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("/Granted/Folders/f1", "/Other/Folders/f1", false)]
    [InlineData("/Other/Folders/f1", "/Granted/Folders/f1", true)]
    public async Task When_several_resource_keys_end_with_the_partial_key_the_last_one_wins(string firstKey, string lastKey, bool allowed)
    {
        // Signature 365's parity (D10): its loop has no break, so each match replaces the one before
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Link(Rename, [Pair("Folders/f1")], new KeyedResource(firstKey, lastKey)),
            b => b.AddResourceGrantSource(new FixedGrantSource("/Granted/*")));

        result.IsAllowed.Should().Be(allowed);
    }

    [Fact]
    public async Task A_partial_link_key_that_ends_none_of_the_resource_keys_is_expanded()
    {
        var expander = Expanding("/T/ten_1/Folders/f1");

        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Link(Rename, [Pair("Folders/f1")], new KeyedResource("/T/ten_2/Folders/f2")),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")).Services.AddScoped<IResourceKeyExpander>(_ => expander));

        result.IsAllowed.Should().BeTrue();
        expander.LastKey.Should().Be(new ResourceKey("Folders/f1"));
    }

    [Theory]
    [InlineData("/Folders/*", true)]
    [InlineData("/Granted/*", false)]
    public async Task An_absolute_link_key_is_used_as_it_is_even_when_a_resource_key_ends_with_it(string grant, bool allowed)
    {
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Link(Rename, [Pair("/Folders/f1")], new KeyedResource("/Granted/Folders/f1")),
            b => b.AddResourceGrantSource(new FixedGrantSource(grant)));

        result.IsAllowed.Should().Be(allowed);
    }

    [Fact]
    public async Task Resource_keys_that_are_null_or_empty_are_skipped()
    {
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Link(Rename, [Pair("Folders/f1")], new KeyedResource("/T/ten_1/Folders/f1", null!, string.Empty)),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().BeTrue();
    }

    // ─── Who is refused before any grant is read ──────────────────────

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_with_a_401_before_any_grant_is_read()
    {
        var source = new FixedGrantSource("/*");

        var result = await AuthorizeAsync(Execute("/T/ten_1"), b => b.AddResourceGrantSource(source), principal: new ClaimsPrincipal(new ClaimsIdentity()));

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
        result.Message.Should().Be("Please log in");
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_anonymous_user_context_is_refused_with_a_401()
    {
        var source = new FixedGrantSource("/*");

        var result = await AuthorizeAsync(Execute("/T/ten_1"), b => b.AddResourceGrantSource(source), user: AnonymousUserAuthorisationContext.Instance);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_inactive_user_is_refused_as_Signature_365_refuses_one()
    {
        var source = new FixedGrantSource("/*");

        var result = await AuthorizeAsync(Execute("/T/ten_1"), b => b.AddResourceGrantSource(source), user: new UserContext(isActive: false));

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
        result.Reason.Should().Be("Operation does not have AllowAnonymous attribute. User is inactive");
        result.Message.Should().Be("Your account has been deactivated");
        source.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_active_user_is_judged_by_grants()
    {
        var result = await AuthorizeAsync(Execute("/T/ten_1"), b => b.AddResourceGrantSource(new FixedGrantSource("/*")), user: new UserContext(isActive: true));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task An_authenticated_caller_without_a_user_context_is_judged_by_grants()
    {
        // An app with no user context factory never has one, so its absence is not taken as unauthenticated
        var result = await AuthorizeAsync(Execute("/T/ten_1"), b => b.AddResourceGrantSource(new FixedGrantSource("/*")), user: null);

        result.IsAllowed.Should().BeTrue();
    }

    // ─── Scope pairs and modes ────────────────────────────────────────

    [Fact]
    public async Task A_pair_without_a_resource_key_cannot_be_judged_and_is_denied()
    {
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Execute(Rename, [new ResolvedScopePair("grantFolder", "rename", null, null)]),
            b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        result.IsAllowed.Should().BeFalse("even the root grant cannot cover a pair that has no key");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Reason.Should().Contain("grantFolder").And.Contain("rename").And.Contain("no resource key");
    }

    [Fact]
    public async Task Any_satisfied_pair_allows_the_request()
    {
        var request = ResourceAuthorizationRequest.Execute(
            Rename,
            [new ResolvedScopePair("grantFolder", "rename", null, null), Pair("/T/ten_2/Folders/f1"), Pair("/T/ten_1/Folders/f1")]);

        var result = await AuthorizeAsync(request, b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task When_every_pair_is_denied_the_reason_names_each_of_them()
    {
        var request = ResourceAuthorizationRequest.Execute(Rename, [Pair("/T/ten_2/Folders/f1"), Pair("/T/ten_3/Folders/f1")]);

        var result = await AuthorizeAsync(request, b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Message.Should().Be(ResourceKeyAuthorizationProvider.DeniedMessage);
        result.Reason.Should().Contain("/T/ten_2/Folders/f1").And.Contain("/T/ten_3/Folders/f1");
    }

    [Fact]
    public async Task A_request_with_no_scope_pairs_is_denied()
    {
        var result = await AuthorizeAsync(ResourceAuthorizationRequest.Execute(Rename, []), b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        result.IsAllowed.Should().BeFalse();
    }

    // ─── Listing (#81) ────────────────────────────────────────────────

    // In order: the listed resource itself, an ancestor of it, the root; a grant beneath the prefix; the two shapes a listing
    // accepts but execution never honours (an exact ancestor, and a grant beneath the prefix that diverges from the rest of
    // the template); then a sibling that only shares a prefix, another hierarchy, and another case
    [Theory]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/T/ten_1/Users/*", true)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/T/ten_1/*", true)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/*", true)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/T/ten_1/Users/usr_1", true)]
    [InlineData("/P/{Auth.AccountId}/Users/{Id}", "/P/p/Users/", "/P/p", true)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/", "/T/ten_1/Groups/*", true)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_12/", "/T/ten_1/*", false)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/I/ident_1/*", false)]
    [InlineData("/T/{Auth.AccountId}/Users/{Id}", "/T/ten_1/Users/", "/t/ten_1/*", false)]
    public async Task A_listing_cut_at_a_property_token_matches_grants_prefix_compatibly(string template, string prefix, string grant, bool allowed)
    {
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Listing(Rename, [new ResolvedScopePair("grantFolder", "rename", template, prefix)]),
            b => b.AddResourceGrantSource(new FixedGrantSource(grant)));

        result.IsAllowed.Should().Be(allowed);
    }

    // With no property token the key is complete, so it is judged as execution judges it: an exact ancestor that a cut
    // prefix would accept does not cover it
    [Theory]
    [InlineData("/Accounts/Users", "/Accounts", false)]
    [InlineData("/Accounts/Users", "/Accounts/*", true)]
    [InlineData("/T/{Auth.AccountId}/Users", "/T/ten_1", false)]
    [InlineData("/T/{Auth.AccountId}/Users", "/T/ten_1/*", true)]
    public async Task A_listing_key_with_no_property_token_is_judged_as_execution_judges_it(string template, string grant, bool allowed)
    {
        var key = template.Replace("{Auth.AccountId}", "ten_1", StringComparison.Ordinal);

        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Listing(Rename, [new ResolvedScopePair("grantFolder", "rename", template, key)]),
            b => b.AddResourceGrantSource(new FixedGrantSource(grant)));

        result.IsAllowed.Should().Be(allowed);
    }

    [Fact]
    public async Task A_listing_of_a_scope_pair_with_no_key_is_denied()
    {
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Listing(Rename, [new ResolvedScopePair("grantFolder", "rename", null, null)]),
            b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task A_listing_asks_the_grant_sources_for_the_listed_operation()
    {
        // The grants are keyed by operation, as Signature 365's are, so the wrong operation would find none
        var result = await AuthorizeAsync(
            ResourceAuthorizationRequest.Listing(Rename, [new ResolvedScopePair("grantFolder", "rename", "/T/{Id}", "/T/")]),
            b => b.AddResourceGrantSource(new OperationGrantSource(typeof(RenameGrantFolderCommand), "/T/ten_1/*")),
            contextDescriptor: ListFolders);

        result.IsAllowed.Should().BeTrue();
    }

    // ─── The checker on its own ───────────────────────────────────────

    [Theory]
    [InlineData("/T/ten_1/Folders/f1", true)]
    [InlineData("/T/ten_2/Folders/f1", false)]
    [InlineData("Folders/f1", true)]
    public async Task The_access_checker_decides_a_demand_outside_the_provider(string key, bool allowed)
    {
        var result = await CheckAsync(
            new ResourceDemand(Rename, new ResourceKey(key)),
            Pair(key),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")).AddResourceKeyExpander<TenantOneFolderExpander>());

        result.IsAllowed.Should().Be(allowed);
    }

    [Fact]
    public async Task The_access_checker_decides_the_demanded_key_not_the_scope_pair_key()
    {
        var result = await CheckAsync(
            new ResourceDemand(Rename, new ResourceKey("/T/ten_2/Folders/f1")),
            Pair("/T/ten_1/Folders/f1"),
            b => b.AddResourceGrantSource(new FixedGrantSource("/T/ten_1/*")));

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task The_access_checker_refuses_a_demand_that_names_no_operation()
    {
        var act = async () => await CheckAsync(default, Pair("/T/ten_1"), b => b.AddResourceGrantSource(new FixedGrantSource("/*")));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── Registration ─────────────────────────────────────────────────

    [Fact]
    public void Resource_key_authorization_registers_one_singleton_provider_and_one_scoped_checker_however_often_it_is_added()
    {
        var services = new ServiceCollection();

        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(static b => b
            .AddResourceKeyAuthorization()
            .AddResourceKeyAuthorization());

        services.Where(static d => d.ServiceType == typeof(IResourceAuthorizationProvider)).Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType == typeof(ResourceKeyAuthorizationProvider));
        services.Where(static d => d.ServiceType == typeof(IResourceAccessChecker)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void An_access_checker_registered_first_is_kept()
    {
        var services = new ServiceCollection();
        services.AddScoped<IResourceAccessChecker, TestAccessChecker>();

        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(static b => b.AddResourceKeyAuthorization());

        services.Where(static d => d.ServiceType == typeof(IResourceAccessChecker)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(TestAccessChecker));
    }

    [Fact]
    public void Expanders_and_grant_sources_are_registered_scoped_in_the_order_they_are_added()
    {
        var services = new ServiceCollection();
        var instance = new FixedGrantSource("/*");

        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(b => b
            .AddResourceKeyExpander<TenantOneFolderExpander>()
            .AddResourceKeyExpander<GrantFolderKeyExpander>()
            .AddResourceGrantSource<ClaimlessGrantSource>()
            .AddResourceGrantSource(instance));

        services.Where(static d => d.ServiceType == typeof(IResourceKeyExpander))
            .Select(static d => (d.ImplementationType, d.Lifetime))
            .Should().Equal((typeof(TenantOneFolderExpander), ServiceLifetime.Scoped), (typeof(GrantFolderKeyExpander), ServiceLifetime.Scoped));

        var sources = services.Where(static d => d.ServiceType == typeof(IResourceGrantSource)).ToArray();
        sources.Should().HaveCount(2);
        sources[0].Should().Match<ServiceDescriptor>(d => d.ImplementationType == typeof(ClaimlessGrantSource) && d.Lifetime == ServiceLifetime.Scoped);
        sources[1].Should().Match<ServiceDescriptor>(d => d.ImplementationInstance == instance && d.Lifetime == ServiceLifetime.Singleton);
    }

    // ─── Harness ──────────────────────────────────────────────────────

    private static ResourceAuthorizationRequest Execute(string key)
    {
        return ResourceAuthorizationRequest.Execute(Rename, [Pair(key)]);
    }

    private static ResolvedScopePair Pair(string key)
    {
        return new ResolvedScopePair("grantFolder", "rename", key, key);
    }

    private static ClaimsPrincipal Authenticated()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "Test"));
    }

    private static FixedExpander Declining()
    {
        return new FixedExpander(null);
    }

    private static FixedExpander Expanding(params string[] keys)
    {
        return new FixedExpander(keys.Select(static k => new ResourceKey(k)).ToArray());
    }

    private static async Task<ExecutionAllowed> AuthorizeAsync(
        ResourceAuthorizationRequest request,
        Action<BlueprintServiceBuilder> configure,
        ClaimsPrincipal? principal = null,
        IUserAuthorisationContext? user = null,
        ApiOperationDescriptor? contextDescriptor = null)
    {
        await using var serviceProvider = BuildServices(configure);
        await using var scope = serviceProvider.CreateAsyncScope();

        principal ??= Authenticated();

        var provider = scope.ServiceProvider.GetServices<IResourceAuthorizationProvider>().Should().ContainSingle().Which;
        var context = new ApiOperationContext(scope.ServiceProvider, App, contextDescriptor ?? request.Descriptor, CancellationToken.None)
        {
            UserPrincipal = principal,
            UserAuthorisationContext = user,
        };

        return await provider.AuthorizeAsync(context, principal, request, CancellationToken.None);
    }

    private static async Task<ExecutionAllowed> CheckAsync(ResourceDemand demand, ResolvedScopePair pair, Action<BlueprintServiceBuilder> configure)
    {
        await using var serviceProvider = BuildServices(configure);
        await using var scope = serviceProvider.CreateAsyncScope();

        var principal = Authenticated();
        var checker = scope.ServiceProvider.GetRequiredService<IResourceAccessChecker>();
        var context = new ApiOperationContext(scope.ServiceProvider, App, ListFolders, CancellationToken.None) { UserPrincipal = principal };

        return await checker.HasAccessAsync(context, principal, demand, pair, CancellationToken.None);
    }

    private static ServiceProvider BuildServices(Action<BlueprintServiceBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GrantFolderDirectory>();
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(b =>
        {
            b.AddResourceKeyAuthorization();
            configure(b);
        });

        return services.BuildServiceProvider();
    }

    /// <summary>A resource with the given keys, implemented explicitly as an app's resource implements them.</summary>
    private sealed class KeyedResource(params string[] keys) : IHaveResourceKeys
    {
        IEnumerable<string> IHaveResourceKeys.ResourceKeys => keys;
    }

    /// <summary>A user's context that is active or not.</summary>
    private sealed class UserContext(bool isActive) : IUserAuthorisationContext
    {
        public bool IsActive => isActive;

        public bool IsAnonymous => false;

        public string Id => "user-1";

        public string? IdentityId => null;

        public string AccountId => "acc_1";

        public string? Email => null;

        public string? Name => null;

        public void PopulateMetadata(Action<string, object> add)
        {
        }
    }

    /// <summary>Holds the given grants for every operation and scope pair, recording what it is asked.</summary>
    private sealed class FixedGrantSource(params string[] grants) : IResourceGrantSource
    {
        public int Calls { get; private set; }

        public ApiOperationDescriptor? LastOperation { get; private set; }

        public ResolvedScopePair LastPair { get; private set; }

        public ValueTask<ResourceGrants> GetGrantsAsync(
            ApiOperationContext context,
            ClaimsPrincipal principal,
            ApiOperationDescriptor operation,
            ResolvedScopePair scopePair,
            CancellationToken cancellationToken)
        {
            this.Calls++;
            this.LastOperation = operation;
            this.LastPair = scopePair;

            return new ValueTask<ResourceGrants>(new ResourceGrants(grants.Select(ResourceGrant.Parse)));
        }
    }

    /// <summary>Holds the given grants for one operation type alone, as a source keyed by operation does.</summary>
    private sealed class OperationGrantSource(Type operationType, params string[] grants) : IResourceGrantSource
    {
        public ValueTask<ResourceGrants> GetGrantsAsync(
            ApiOperationContext context,
            ClaimsPrincipal principal,
            ApiOperationDescriptor operation,
            ResolvedScopePair scopePair,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ResourceGrants>(
                operation.OperationType == operationType ? new ResourceGrants(grants.Select(ResourceGrant.Parse)) : ResourceGrants.Empty);
        }
    }

    /// <summary>A grant source with nothing to give, registered by type.</summary>
    private sealed class ClaimlessGrantSource : IResourceGrantSource
    {
        public ValueTask<ResourceGrants> GetGrantsAsync(
            ApiOperationContext context,
            ClaimsPrincipal principal,
            ApiOperationDescriptor operation,
            ResolvedScopePair scopePair,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ResourceGrants>(ResourceGrants.Empty);
        }
    }

    /// <summary>Answers every partial key with the given keys, or declines every one when given none, recording what it is asked.</summary>
    private sealed class FixedExpander(IReadOnlyList<ResourceKey>? keys) : IResourceKeyExpander
    {
        public int Calls { get; private set; }

        public ResourceKey? LastKey { get; private set; }

        public ValueTask<IReadOnlyList<ResourceKey>?> ExpandAsync(ResourceKey partialKey, ApiOperationContext context, CancellationToken cancellationToken)
        {
            this.Calls++;
            this.LastKey = partialKey;

            return new ValueTask<IReadOnlyList<ResourceKey>?>(keys);
        }
    }

    /// <summary>Places every <c>Folders/{id}</c> under <c>ten_1</c>, registered by type.</summary>
    private sealed class TenantOneFolderExpander : IResourceKeyExpander
    {
        public ValueTask<IReadOnlyList<ResourceKey>?> ExpandAsync(ResourceKey partialKey, ApiOperationContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<IReadOnlyList<ResourceKey>?>(
                partialKey.Value.StartsWith("Folders/", StringComparison.Ordinal) ? [new ResourceKey("/T/ten_1/" + partialKey.Value)] : null);
        }
    }

    /// <summary>An app's own access checker.</summary>
    private sealed class TestAccessChecker : IResourceAccessChecker
    {
        public ValueTask<ExecutionAllowed> HasAccessAsync(
            ApiOperationContext context,
            ClaimsPrincipal principal,
            ResourceDemand demand,
            ResolvedScopePair scopePair,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes);
        }
    }
}
