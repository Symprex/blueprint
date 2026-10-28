using Blueprint.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves <see cref="ResourceGrants"/> is a set: it covers a key when any grant in it does, and holds each grant once.
/// </summary>
public class ResourceGrantsTests
{
    [Fact]
    public void Empty_covers_nothing()
    {
        ResourceGrants.Empty.Should().BeEmpty();
        ResourceGrants.Empty.Covers(new ResourceKey("/Tenants/ten_1")).Should().BeFalse();
        ResourceGrants.Empty.IsPrefixCompatibleWith("/").Should().BeFalse();
    }

    [Fact]
    public void Covers_a_key_when_any_grant_covers_it()
    {
        var grants = new ResourceGrants([ResourceGrant.Parse("/Partners/part_1/*"), ResourceGrant.Parse("/Tenants/ten_1/*")]);

        grants.Covers(new ResourceKey("/Tenants/ten_1/Users/usr_1")).Should().BeTrue();
        grants.Covers(new ResourceKey("/Partners/part_1")).Should().BeTrue();
        grants.Covers(new ResourceKey("/Tenants/ten_12")).Should().BeFalse();
    }

    [Fact]
    public void IsPrefixCompatibleWith_is_true_when_any_grant_is()
    {
        var grants = new ResourceGrants([ResourceGrant.Parse("/Identities/ident_1/*"), ResourceGrant.Parse("/Tenants/ten_1/*")]);

        grants.IsPrefixCompatibleWith("/Tenants/ten_1/Users/").Should().BeTrue();
        grants.IsPrefixCompatibleWith("/Partners/").Should().BeFalse();
    }

    [Fact]
    public void Each_grant_is_held_once()
    {
        var grants = new ResourceGrants(
        [
            ResourceGrant.Parse("/Tenants/ten_1/*"),
            ResourceGrant.Parse("/Tenants/ten_1/*"),
            ResourceGrant.Parse("/tenants/ten_1/*"),
        ]);

        grants.Should().HaveCount(2);
        grants.Should().Equal(ResourceGrant.Parse("/Tenants/ten_1/*"), ResourceGrant.Parse("/tenants/ten_1/*"));
    }

    [Fact]
    public void An_empty_sequence_gives_an_empty_set()
    {
        new ResourceGrants([]).Should().BeEmpty();
    }
}
