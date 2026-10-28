using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Unit tests for <see cref="ScopePairClaimAuthorizationProvider"/> covering
/// verb hierarchy expansion, ignored scopes, and no-scopes fallback behavior.
/// </summary>
public class ScopePairClaimAuthorizationProviderTests
{
    // ─── Verb hierarchy tests ─────────────────────────────────────────

    [Fact]
    public async Task When_claim_has_parent_verb_that_includes_required_verb_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: ["openid"],
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Manage");
        var scopePairs = Resolve(new ScopePair("User", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("User.Manage includes User.Read");
    }

    [Fact]
    public async Task When_claim_has_parent_verb_then_matches_all_included_verbs()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("Template.Manage");

        foreach (var verb in new[] { "Create", "Read", "Update", "Delete" })
        {
            var result = await provider.IsAuthorizedAsync(
                null!,
                principal,
                Resolve(new ScopePair("Template", verb)),
                CancellationToken.None);

            result.Should().BeTrue($"Manage should include {verb}");
        }
    }

    [Fact]
    public async Task When_claim_has_child_verb_then_does_not_match_parent_requirement()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Read");
        var scopePairs = Resolve(new ScopePair("User", "Manage"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("having Read does not imply having Manage");
    }

    [Fact]
    public async Task When_parent_verb_matches_directly_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Manage");
        var scopePairs = Resolve(new ScopePair("User", "Manage"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("Manage directly matches Manage requirement");
    }

    [Fact]
    public async Task When_verb_hierarchy_but_wrong_noun_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Manage");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("User.Manage does not satisfy Template.Read");
    }

    [Fact]
    public async Task When_no_verb_inclusions_configured_then_only_direct_match_works()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: null);

        var principal = CreatePrincipal("User.Manage");
        var scopePairs = Resolve(new ScopePair("User", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("without verb inclusions, Manage does not match Read");
    }

    [Fact]
    public async Task When_verb_hierarchy_with_resolved_scope_pairs_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Manage");
        ResolvedScopePair[] resolvedPairs = [new ResolvedScopePair("User", "Create", null, null)];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, resolvedPairs, CancellationToken.None);

        result.Should().BeTrue("User.Manage includes User.Create (resolved overload)");
    }

    [Fact]
    public async Task When_multiple_hierarchy_levels_then_all_resolved()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            ".",
            ignoredScopes: null,
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Admin"] = ["Manage", "Create", "Read", "Update", "Delete"],
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var principal = CreatePrincipal("User.Admin");

        var result = await provider.IsAuthorizedAsync(
            null!,
            principal,
            Resolve(new ScopePair("User", "Create")),
            CancellationToken.None);

        result.Should().BeTrue("Admin includes Create");

        var result2 = await provider.IsAuthorizedAsync(
            null!,
            principal,
            Resolve(new ScopePair("User", "Manage")),
            CancellationToken.None);

        result2.Should().BeTrue("Admin includes Manage");
    }

    // ─── Core matching tests ──────────────────────────────────────────

    [Fact]
    public async Task When_direct_scope_match_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template::Read");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task When_scope_match_is_case_insensitive_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("template::read");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("matching should be case-insensitive");
    }

    [Fact]
    public async Task When_no_required_scope_pairs_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template::Read");

        var result = await provider.IsAuthorizedAsync(
            null!, principal, Array.Empty<ResolvedScopePair>(), CancellationToken.None);

        result.Should().BeTrue("empty requirements should always pass");
    }

    [Fact]
    public async Task When_unauthenticated_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("unauthenticated should always be denied");
    }

    [Fact]
    public async Task When_scope_mismatch_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template::Read");
        var scopePairs = Resolve(new ScopePair("Template", "Delete"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse();
    }

    // ─── Ignored scopes tests ─────────────────────────────────────────

    [Fact]
    public async Task When_only_ignored_scopes_present_then_no_scopes_fallback()
    {
        var provider = new PermissiveNoScopesProvider(
            "scope",
            ".",
            ["openid", "profile"]);

        var principal = CreatePrincipal("openid", "profile");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue(
            "only ignored scopes present → falls through to AllowWhenNoScopeClaims");
    }

    // ─── No-scopes fallback tests ─────────────────────────────────────

    [Fact]
    public async Task When_no_scope_claims_and_default_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipalWithoutScopes();
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("default no-scopes behavior is deny");
    }

    [Fact]
    public async Task When_no_scope_claims_and_permissive_override_then_authorized()
    {
        var provider = new PermissiveNoScopesProvider("scope", "::");
        var principal = CreatePrincipalWithoutScopes();
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("AllowWhenNoScopeClaims overridden to return true");
    }

    // ─── Any-of semantics tests ───────────────────────────────────────

    [Fact]
    public async Task When_any_scope_pair_matches_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template::Delete");
        var scopePairs = Resolve(
            new ScopePair("Template", "Read"),
            new ScopePair("Template", "Delete"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("any-of semantics: Delete matches second pair");
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static ClaimsPrincipal CreatePrincipal(params string[] scopes)
    {
        var claims = scopes.Select(static s => new Claim("scope", s)).ToList();
        claims.Add(new Claim(System.Security.Claims.ClaimTypes.Name, "test-user"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal CreatePrincipalWithoutScopes()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(System.Security.Claims.ClaimTypes.Name, "test-user")],
            "TestAuth"));
    }

    private static ResolvedScopePair[] Resolve(params ScopePair[] scopePairs)
    {
        return scopePairs
            .Select(static scopePair => new ResolvedScopePair(
                scopePair.Noun,
                scopePair.Verb,
                scopePair.ResourceKeyTemplate,
                scopePair.ResourceKeyTemplate))
            .ToArray();
    }

    // ─── Test doubles ─────────────────────────────────────────────────

    private sealed class PermissiveNoScopesProvider : ScopePairClaimAuthorizationProvider
    {
        public PermissiveNoScopesProvider(
            string scopeClaimType,
            string separator,
            IEnumerable<string>? ignoredScopes = null)
            : base(scopeClaimType, separator, ignoredScopes)
        {
        }

        protected override bool AllowWhenNoScopeClaims(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal)
        {
            return true;
        }
    }

    // ─── WithCrudVerbHierarchy factory tests ───────────────────────────

    [Fact]
    public async Task WithCrudVerbHierarchy_manages_scope_satisfies_read()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy();

        var principal = CreatePrincipal("Template::Manage");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("Manage includes Read via standard CRUD hierarchy");
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_manages_scope_satisfies_all_crud_verbs()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy();
        var principal = CreatePrincipal("User::Manage");

        foreach (var verb in new[] { "Create", "Read", "Update", "Delete" })
        {
            var scopePairs = Resolve(new ScopePair("User", verb));

            var result = await provider.IsAuthorizedAsync(
                null!, principal, scopePairs, CancellationToken.None);

            result.Should().BeTrue($"Manage should include {verb}");
        }
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_ignores_standard_oauth_scopes()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("scope", "openid"),
                new Claim("scope", "profile"),
                new Claim("scope", "email"),
                new Claim("scope", "offline_access"),
            ],
            "TestAuth"));

        var scopePairs = Resolve(new ScopePair("User", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("only ignored OAuth scopes present, no API scopes");
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_additional_ignored_scopes_are_skipped()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            "Signature365.ReadWrite.All", "legacy.scope");

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("scope", "Signature365.ReadWrite.All"),
                new Claim("scope", "legacy.scope"),
            ],
            "TestAuth"));

        var scopePairs = Resolve(new ScopePair("User", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("additional ignored scopes should be skipped");
    }

    [Fact]
    public void StandardCrudVerbHierarchy_contains_manage_to_crud_mapping()
    {
        var hierarchy = ScopePairClaimAuthorizationProvider.StandardCrudVerbHierarchy;

        hierarchy.Should().ContainKey("Manage");
        hierarchy["Manage"].Should().BeEquivalentTo(["Create", "Read", "Update", "Delete"]);
    }

    // ─── Legacy scope mapper tests ────────────────────────────────────

    [Fact]
    public async Task WithCrudVerbHierarchy_legacy_mapper_remaps_old_scopes()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: static scope => scope switch
            {
                "CompiledSignature::Read" => ["MyCompiledSignature::Read"],
                _ => null,
            });

        var principal = CreatePrincipal("CompiledSignature::Read");
        var scopePairs = Resolve(new ScopePair("MyCompiledSignature", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("legacy mapper should remap CompiledSignature::Read to MyCompiledSignature::Read");
    }

    [Fact]
    public async Task Legacy_mapper_does_not_interfere_with_direct_matches()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: static _ => null);

        var principal = CreatePrincipal("Template::Read");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("direct match should work even when mapper returns null");
    }

    [Fact]
    public async Task Legacy_mapper_remapped_scope_benefits_from_verb_hierarchy()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: static scope => scope switch
            {
                "UserFieldValue::ManageMine" => ["UserFieldValue::Manage"],
                _ => null,
            });

        var principal = CreatePrincipal("UserFieldValue::ManageMine");
        var scopePairs = Resolve(new ScopePair("UserFieldValue", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("ManageMine remaps to Manage, which includes Read via hierarchy");
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_custom_separator_works()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: ".",
            legacyScopeMapper: null);

        var principal = CreatePrincipal("Template.Read");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("custom separator '.' should work when claim values use '.'");
    }

    // ─── allowWhenNoScopeClaims constructor parameter tests ─────────

    [Fact]
    public async Task Constructor_allowWhenNoScopeClaims_true_grants_access_when_no_scopes()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            "::",
            ignoredScopes: null,
            verbInclusions: null,
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: true);

        var principal = CreatePrincipalWithoutScopes();
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("allowWhenNoScopeClaims=true grants access when no API scopes present");
    }

    [Fact]
    public async Task Constructor_allowWhenNoScopeClaims_false_denies_when_no_scopes()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            "::",
            ignoredScopes: null,
            verbInclusions: null,
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: false);

        var principal = CreatePrincipalWithoutScopes();
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("allowWhenNoScopeClaims=false (default) denies when no scopes");
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_allowWhenNoScopeClaims_true_grants_access()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: true,
            additionalIgnoredScopes: ["Signature365.ReadWrite.All", "Signature365ReadWrite"]);

        // Principal has only ignored scopes — should still be granted access
        var principal = CreatePrincipal("openid", "profile", "Signature365.ReadWrite.All");
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue(
            "all scope claims are ignored → falls through to allowWhenNoScopeClaims=true");
    }

    [Fact]
    public async Task WithCrudVerbHierarchy_allowWhenNoScopeClaims_still_denies_unauthenticated()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy(
            separator: "::",
            legacyScopeMapper: null,
            allowWhenNoScopeClaims: true);

        // Unauthenticated principal — should still be denied even with permissive no-scopes
        var identity = new ClaimsIdentity(); // not authenticated (no authenticationType)
        var principal = new ClaimsPrincipal(identity);
        var scopePairs = Resolve(new ScopePair("Template", "Read"));

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse(
            "unauthenticated principal is always denied regardless of allowWhenNoScopeClaims");
    }

    // ─── Parity settings (Symprex/blueprint#82) ───────────────────────

    [Fact]
    public async Task An_exact_ignored_scope_set_adds_none_of_the_default_ignored_scopes()
    {
        // Signature 365 ignores openid and profile but not offline_access: a token whose only scope is offline_access
        // carries an API scope, matches nothing and is refused, rather than being read as unrestricted.
        var provider = new ScopePairClaimAuthorizationProvider("scope", "::", ignoredScopes: ["openid", "profile"], allowWhenNoScopeClaims: true);

        var result = await provider.IsAuthorizedAsync(
            null!, CreatePrincipal("openid", "offline_access"), Resolve(new ScopePair("Template", "Read")), CancellationToken.None);

        result.Should().BeFalse("offline_access is not in the exact ignored set, so it is an API scope that matches nothing");
    }

    [Fact]
    public async Task An_exact_ignored_scope_set_still_ignores_what_it_names()
    {
        var provider = new ScopePairClaimAuthorizationProvider("scope", "::", ignoredScopes: ["openid", "profile"], allowWhenNoScopeClaims: true);

        var result = await provider.IsAuthorizedAsync(
            null!, CreatePrincipal("openid", "profile"), Resolve(new ScopePair("Template", "Read")), CancellationToken.None);

        result.Should().BeTrue("only ignored scopes are held, so the no-scope-claims rule applies");
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_allowed_when_the_provider_is_told_to_allow_one()
    {
        // Signature 365's scope check abstains for an unauthenticated caller and leaves refusing it to the permission
        // check, so registered beside that one it must not refuse first with its own 403.
        var provider = new ScopePairClaimAuthorizationProvider("scope", "::", allowUnauthenticated: true);

        var result = await provider.IsAuthorizedAsync(
            null!, new ClaimsPrincipal(new ClaimsIdentity()), Resolve(new ScopePair("Template", "Read")), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_by_default()
    {
        var provider = new ScopePairClaimAuthorizationProvider("scope", "::");

        var result = await provider.IsAuthorizedAsync(
            null!, new ClaimsPrincipal(new ClaimsIdentity()), Resolve(new ScopePair("Template", "Read")), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Template::Admin", "Read")]
    [InlineData("Template::Admin", "Manage")]
    [InlineData("Template::Manage", "Read")]
    public async Task A_verb_includes_every_verb_its_included_verbs_include(string held, string requiredVerb)
    {
        // Admin → Manage → Read: Admin must satisfy Read without the map listing Read under Admin.
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            "::",
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Admin"] = ["Manage"],
                ["Manage"] = ["Create", "Read", "Update", "Delete"],
            });

        var result = await provider.IsAuthorizedAsync(
            null!, CreatePrincipal(held), Resolve(new ScopePair("Template", requiredVerb)), CancellationToken.None);

        result.Should().BeTrue($"{held} includes {requiredVerb}, directly or through another verb");
    }

    [Fact]
    public async Task A_verb_never_includes_the_verbs_that_include_it()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            "::",
            verbInclusions: new Dictionary<string, string[]>
            {
                ["Admin"] = ["Manage"],
                ["Manage"] = ["Read"],
            });

        var result = await provider.IsAuthorizedAsync(
            null!, CreatePrincipal("Template::Read"), Resolve(new ScopePair("Template", "Admin")), CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task A_cycle_in_the_verb_inclusions_does_not_hang()
    {
        var provider = new ScopePairClaimAuthorizationProvider(
            "scope",
            "::",
            verbInclusions: new Dictionary<string, string[]>
            {
                ["A"] = ["B"],
                ["B"] = ["A", "C"],
            });

        var result = await provider.IsAuthorizedAsync(
            null!, CreatePrincipal("Template::A"), Resolve(new ScopePair("Template", "C")), CancellationToken.None);

        result.Should().BeTrue("A includes B, which includes C");
    }

    // ─── Decision shape ───────────────────────────────────────────────

    [Fact]
    public async Task A_denial_is_an_authorisation_failure_whose_reason_names_the_required_and_held_scopes()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy();
        var principal = CreatePrincipal("openid", "SentEmail::Read", "Outlook::Read");

        var result = await provider.AuthorizeAsync(
            null!, principal, ResourceAuthorizationRequest.Execute(Resolve(new ScopePair("Template", "Read"), new ScopePair("Template", "Update"))), CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Message.Should().Be(ScopePairClaimAuthorizationProvider.DeniedMessage);
        result.Reason.Should().Be("The caller holds none of the required scopes: Template::Read, Template::Update. Held: SentEmail::Read, Outlook::Read");
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_with_a_403_as_before()
    {
        var provider = ScopePairClaimAuthorizationProvider.WithCrudVerbHierarchy();

        var result = await provider.AuthorizeAsync(
            null!, new ClaimsPrincipal(new ClaimsIdentity()), ResourceAuthorizationRequest.Execute(Resolve(new ScopePair("Template", "Read"))), CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation, "the bool contract's false was always a 403");
        result.Message.Should().Be(ScopePairClaimAuthorizationProvider.DeniedMessage);
    }
}

/// <summary>
/// Reads a provider's decision as the yes or no most of these tests are about.
/// </summary>
internal static class ResourceAuthorizationProviderTestExtensions
{
    public static async Task<bool> IsAuthorizedAsync(
        this IResourceAuthorizationProvider provider,
        ApiOperationContext operationContext,
        ClaimsPrincipal principal,
        IReadOnlyList<ResolvedScopePair> scopePairs,
        CancellationToken cancellationToken)
    {
        var result = await provider.AuthorizeAsync(operationContext, principal, ResourceAuthorizationRequest.Execute(scopePairs), cancellationToken);

        return result.IsAllowed;
    }
}
