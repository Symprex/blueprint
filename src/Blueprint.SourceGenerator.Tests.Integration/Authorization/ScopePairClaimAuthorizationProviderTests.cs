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
        ScopePair[] scopePairs = [new ScopePair("User", "Read")];

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
                new ScopePair[] { new("Template", verb) },
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
        ScopePair[] scopePairs = [new ScopePair("User", "Manage")];

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
        ScopePair[] scopePairs = [new ScopePair("User", "Manage")];

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
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

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
        ScopePair[] scopePairs = [new ScopePair("User", "Read")];

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
            new ScopePair[] { new("User", "Create") },
            CancellationToken.None);

        result.Should().BeTrue("Admin includes Create");

        var result2 = await provider.IsAuthorizedAsync(
            null!,
            principal,
            new ScopePair[] { new("User", "Manage") },
            CancellationToken.None);

        result2.Should().BeTrue("Admin includes Manage");
    }

    // ─── Core matching tests ──────────────────────────────────────────

    [Fact]
    public async Task When_direct_scope_match_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template.Read");
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task When_scope_match_is_case_insensitive_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("template.read");
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("matching should be case-insensitive");
    }

    [Fact]
    public async Task When_no_required_scope_pairs_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template.Read");

        var result = await provider.IsAuthorizedAsync(
            null!, principal, Array.Empty<ScopePair>(), CancellationToken.None);

        result.Should().BeTrue("empty requirements should always pass");
    }

    [Fact]
    public async Task When_unauthenticated_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("unauthenticated should always be denied");
    }

    [Fact]
    public async Task When_scope_mismatch_then_denied()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template.Read");
        ScopePair[] scopePairs = [new ScopePair("Template", "Delete")];

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
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

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
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeFalse("default no-scopes behavior is deny");
    }

    [Fact]
    public async Task When_no_scope_claims_and_permissive_override_then_authorized()
    {
        var provider = new PermissiveNoScopesProvider("scope", ".");
        var principal = CreatePrincipalWithoutScopes();
        ScopePair[] scopePairs = [new ScopePair("Template", "Read")];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("AllowWhenNoScopeClaims overridden to return true");
    }

    // ─── Any-of semantics tests ───────────────────────────────────────

    [Fact]
    public async Task When_any_scope_pair_matches_then_authorized()
    {
        var provider = new ScopePairClaimAuthorizationProvider();
        var principal = CreatePrincipal("Template.Delete");
        ScopePair[] scopePairs =
        [
            new ScopePair("Template", "Read"),
            new ScopePair("Template", "Delete"),
        ];

        var result = await provider.IsAuthorizedAsync(
            null!, principal, scopePairs, CancellationToken.None);

        result.Should().BeTrue("any-of semantics: Delete matches second pair");
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static ClaimsPrincipal CreatePrincipal(params string[] scopes)
    {
        var claims = scopes.Select(static s => new Claim("scope", s)).ToList();
        claims.Add(new Claim(ClaimTypes.Name, "test-user"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal CreatePrincipalWithoutScopes()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "test-user")],
            "TestAuth"));
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
}
