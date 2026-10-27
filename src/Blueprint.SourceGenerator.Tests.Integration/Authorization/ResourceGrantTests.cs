using Blueprint.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves <see cref="ResourceGrant"/> matches demanded keys as Signature 365's claim inspector does, so that moving the
/// rules into Blueprint changes no outcome: an exact grant covers its own key, and a trailing <c>/*</c> covers its base
/// and everything beneath it at a segment boundary.
/// </summary>
public class ResourceGrantTests
{
    [Theory]
    [InlineData("/Tenants/tenant-1/Users/user-1", "/Tenants/tenant-1/Users/user-1", true)]
    [InlineData("/Tenants/tenant-1/Users/user-1", "/Tenants/tenant-1/Users/user-2", false)]
    [InlineData("/Tenants/tenant-1", "/Tenants/tenant-1/Users/user-1", false)]
    [InlineData("/Tenants/tenant-1/*", "/Tenants/tenant-1", true)]
    [InlineData("/Tenants/tenant-1/*", "/Tenants/tenant-1/Users/user-1", true)]
    [InlineData("/*", "/Tenants/tenant-1", true)]
    [InlineData("/*", "/", true)]
    [InlineData("/Tenants/tenant-1/*", "/Tenants/tenant-12", false)]
    [InlineData("/Tenants/tenant-1/*", "/Tenants", false)]
    [InlineData("/Tenants/tenant-1*", "/Tenants/tenant-2", false)]
    [InlineData("/Tenants/tenant-1*", "/Tenants/tenant-1", false)]
    [InlineData("/Tenants/tenant-1*", "/Tenants/tenant-1*", true)]
    [InlineData("/Tenants/tenant-1/*", "/tenants/tenant-1/Users/user-1", false)]
    [InlineData("/Tenants/tenant-1/Users/user-1", "/tenants/tenant-1/Users/user-1", false)]
    public void Covers_matches_exactly_or_at_a_segment_boundary_below_a_wildcard(string grant, string demandedKey, bool expected)
    {
        ResourceGrant.Parse(grant).Covers(new ResourceKey(demandedKey)).Should().Be(expected);
    }

    [Theory]
    [InlineData("/*")]
    [InlineData("/Tenants/tenant-1/*")]
    [InlineData("/Tenants/tenant-1/Users/user-1")]
    public void A_partial_key_is_never_covered(string grant)
    {
        ResourceGrant.Parse(grant).Covers(new ResourceKey("Users/user-1")).Should().BeFalse();
    }

    [Fact]
    public void A_default_key_is_never_covered()
    {
        ResourceGrant.Parse("/*").Covers(default).Should().BeFalse();
    }

    [Theory]
    [InlineData("/Tenants/tenant-1/*", true)]
    [InlineData("/*", true)]
    [InlineData("/Tenants/tenant-1", false)]
    [InlineData("/Tenants/tenant-1*", false)]
    public void IncludesDescendants_is_true_only_for_a_trailing_slash_wildcard(string grant, bool expected)
    {
        var parsed = ResourceGrant.Parse(grant);

        parsed.IncludesDescendants.Should().Be(expected);
        parsed.Value.Should().Be(grant);
        parsed.ToString().Should().Be(grant);
    }

    [Theory]
    [InlineData("Users/user-1")]
    [InlineData("*Global*")]
    [InlineData("*")]
    [InlineData("")]
    public void A_grant_that_is_not_absolute_is_refused(string grant)
    {
        var parse = () => ResourceGrant.Parse(grant);

        parse.Should().Throw<FormatException>().WithMessage($"*'{grant}'*");
        ResourceGrant.TryParse(grant, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_refuses_null()
    {
        ResourceGrant.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_returns_the_grant_for_an_absolute_key()
    {
        ResourceGrant.TryParse("/Tenants/tenant-1/*", out var grant).Should().BeTrue();

        grant.Should().Be(ResourceGrant.Parse("/Tenants/tenant-1/*"));
    }

    [Fact]
    public void Equality_is_ordinal()
    {
        ResourceGrant.Parse("/Tenants/tenant-1/*").Should().Be(ResourceGrant.Parse("/Tenants/tenant-1/*"));
        ResourceGrant.Parse("/Tenants/tenant-1/*").Should().NotBe(ResourceGrant.Parse("/tenants/tenant-1/*"));
        (ResourceGrant.Parse("/a") == ResourceGrant.Parse("/a")).Should().BeTrue();
        (ResourceGrant.Parse("/a") != ResourceGrant.Parse("/b")).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Tenants/ten_1/Users/*", "/Tenants/ten_1/Users/")]
    [InlineData("/Tenants/ten_1/*", "/Tenants/ten_1/Users/")]
    [InlineData("/*", "/Tenants/ten_1/Users/")]
    [InlineData("/*", "/")]
    [InlineData("/Partners/part_1/Tenants/ten_1", "/Partners/part_1/Tenants/ten_1")]
    [InlineData("/Partners/part_1/*", "/Partners/part_1/Tenants/ten_1")]
    public void IsPrefixCompatibleWith_accepts_the_demanded_resource_itself_or_an_ancestor_of_it(string grant, string prefix)
    {
        ResourceGrant.Parse(grant).IsPrefixCompatibleWith(prefix).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Tenants/ten_1/Users/usr_1", "/Tenants/ten_1/Users/")]
    [InlineData("/Tenants/ten_1/CustomRoles/role_1/*", "/Tenants/ten_1/")]
    public void IsPrefixCompatibleWith_accepts_a_grant_beneath_the_prefix(string grant, string prefix)
    {
        ResourceGrant.Parse(grant).IsPrefixCompatibleWith(prefix).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Partners/p", "/Partners/p/Users/")]
    [InlineData("/Tenants/ten_1/Groups/*", "/Tenants/ten_1/")]
    public void IsPrefixCompatibleWith_accepts_shapes_execution_never_honours_so_a_listing_is_never_stricter(string grant, string prefix)
    {
        ResourceGrant.Parse(grant).IsPrefixCompatibleWith(prefix).Should().BeTrue();
    }

    [Theory]
    [InlineData("/Tenants/ten_1/*", "/Tenants/ten_12/")]
    [InlineData("/Tenants/ten_1/CustomRolesArchive/*", "/Tenants/ten_1/CustomRoles/")]
    [InlineData("/Partners/part_1/Tenants/ten_12", "/Partners/part_1/Tenants/ten_1")]
    [InlineData("/Identities/ident_1/*", "/Tenants/")]
    [InlineData("/tenants/ten_1/*", "/Tenants/ten_1/")]
    public void IsPrefixCompatibleWith_rejects_a_sibling_another_hierarchy_or_another_case(string grant, string prefix)
    {
        ResourceGrant.Parse(grant).IsPrefixCompatibleWith(prefix).Should().BeFalse();
    }
}
