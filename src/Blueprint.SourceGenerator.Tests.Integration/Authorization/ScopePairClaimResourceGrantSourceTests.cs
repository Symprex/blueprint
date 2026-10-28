using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves how <see cref="ScopePairClaimResourceGrantSource"/> reads grants from claims whose value is
/// <c>{resourceKey};{Noun}{separator}{Verb}</c>: by the scope pair being satisfied, and skipping anything malformed rather
/// than throwing (Symprex/blueprint#80).
/// </summary>
public sealed class ScopePairClaimResourceGrantSourceTests
{
    private const string ClaimType = "grant";

    private static readonly IntegrationTestBlueprintApp App = new();
    private static readonly ApiOperationDescriptor Rename = App.FindOperation(typeof(RenameGrantFolderCommand))!;
    private static readonly ResolvedScopePair WidgetRead = new("Widget", "Read", "/T/{TenantId}", "/T/ten_1");

    [Fact]
    public async Task A_claim_for_the_scope_pair_gives_its_key_as_a_grant()
    {
        var grants = await GetGrantsAsync(Authenticated(new Claim(ClaimType, "/T/ten_1/*;Widget::Read")));

        grants.Select(static g => g.Value).Should().Equal("/T/ten_1/*");
    }

    [Theory]
    [InlineData("/T/ten_1;widget::read")]
    [InlineData("/T/ten_1;WIDGET::READ")]
    [InlineData("/T/ten_1;wIdGeT::rEaD")]
    public async Task Noun_and_verb_match_case_insensitively(string value)
    {
        var grants = await GetGrantsAsync(Authenticated(new Claim(ClaimType, value)));

        grants.Select(static g => g.Value).Should().Equal("/T/ten_1");
    }

    [Fact]
    public async Task The_key_keeps_its_case_and_is_matched_ordinally()
    {
        var grants = await GetGrantsAsync(Authenticated(new Claim(ClaimType, "/T/Ten_1;Widget::Read")));

        grants.Covers(new ResourceKey("/T/Ten_1")).Should().BeTrue();
        grants.Covers(new ResourceKey("/T/ten_1")).Should().BeFalse();
    }

    [Theory]
    [InlineData("/T/ten_1;Widget::Update")]
    [InlineData("/T/ten_1;Gadget::Read")]
    [InlineData("/T/ten_1;Widget::ReadAll")]
    [InlineData("/T/ten_1;MyWidget::Read")]
    public async Task A_claim_for_another_scope_pair_gives_no_grant(string value)
    {
        var grants = await GetGrantsAsync(Authenticated(new Claim(ClaimType, value)));

        grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Widget::Read")]
    [InlineData("/T/ten_1;Widget::Read;extra")]
    [InlineData("/T/ten_1;;Widget::Read")]
    [InlineData("T/ten_1;Widget::Read")]
    [InlineData(";Widget::Read")]
    [InlineData("/T/ten_1;")]
    [InlineData("/T/ten_1;Widget:Read")]
    [InlineData("/T/ten_1;Widget::Read ")]
    [InlineData("/T/ten_1;Widget::")]
    [InlineData("")]
    public async Task A_malformed_value_is_skipped_without_throwing_and_the_rest_are_still_read(string malformed)
    {
        var grants = await GetGrantsAsync(Authenticated(
            new Claim(ClaimType, malformed),
            new Claim(ClaimType, "/T/ten_2/*;Widget::Read")));

        grants.Select(static g => g.Value).Should().Equal("/T/ten_2/*");
    }

    [Fact]
    public async Task Only_claims_of_the_configured_type_are_read()
    {
        var grants = await GetGrantsAsync(Authenticated(
            new Claim("scope", "/T/ten_1;Widget::Read"),
            new Claim("GRANT", "/T/ten_2;Widget::Read")));

        grants.Select(static g => g.Value).Should().Equal("/T/ten_2");
    }

    [Fact]
    public async Task The_claims_of_an_unauthenticated_identity_are_ignored()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimType, "/*;Widget::Read")]));
        principal.AddIdentity(new ClaimsIdentity([new Claim(ClaimType, "/T/ten_1;Widget::Read")], "Test"));

        var grants = await GetGrantsAsync(principal);

        grants.Select(static g => g.Value).Should().Equal("/T/ten_1");
    }

    [Fact]
    public async Task Each_matching_claim_gives_a_grant_and_a_repeated_one_is_held_once()
    {
        var grants = await GetGrantsAsync(Authenticated(
            new Claim(ClaimType, "/T/ten_1/*;Widget::Read"),
            new Claim(ClaimType, "/P/par_1/*;Widget::Read"),
            new Claim(ClaimType, "/T/ten_1/*;Widget::Read")));

        grants.Select(static g => g.Value).Should().Equal("/T/ten_1/*", "/P/par_1/*");
    }

    [Fact]
    public async Task The_separator_is_configurable()
    {
        var source = new ScopePairClaimResourceGrantSource(ClaimType, ".");

        var grants = await GetGrantsAsync(
            Authenticated(new Claim(ClaimType, "/T/ten_1;Widget.Read"), new Claim(ClaimType, "/T/ten_2;Widget::Read")),
            source);

        grants.Select(static g => g.Value).Should().Equal("/T/ten_1");
    }

    [Fact]
    public async Task A_caller_with_no_matching_claim_holds_no_grants()
    {
        var grants = await GetGrantsAsync(Authenticated(new Claim("sub", "user-1")));

        grants.Should().BeSameAs(ResourceGrants.Empty);
    }

    [Theory]
    [InlineData(null, "::")]
    [InlineData("", "::")]
    [InlineData(ClaimType, null)]
    [InlineData(ClaimType, "")]
    public void The_claim_type_and_separator_are_required(string? claimType, string? separator)
    {
        var act = () => new ScopePairClaimResourceGrantSource(claimType!, separator!);

        act.Should().Throw<ArgumentException>();
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<ResourceGrants> GetGrantsAsync(ClaimsPrincipal principal, ScopePairClaimResourceGrantSource? source = null)
    {
        source ??= new ScopePairClaimResourceGrantSource(ClaimType, "::");

        var context = new ApiOperationContext(NullServiceProvider.Instance, App, Rename, CancellationToken.None) { UserPrincipal = principal };

        return await source.GetGrantsAsync(context, principal, Rename, WidgetRead, CancellationToken.None);
    }
}
