using Blueprint.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves <see cref="ResourceKey"/> holds a demanded key verbatim: absolute when it starts with <c>/</c>, compared
/// ordinally, and with no meaning given to a <c>*</c>.
/// </summary>
public class ResourceKeyTests
{
    [Theory]
    [InlineData("/Tenants/ten_1/Users/usr_1", true)]
    [InlineData("/", true)]
    [InlineData("Users/usr_1", false)]
    [InlineData("Users", false)]
    public void IsAbsolute_is_true_only_for_a_key_starting_with_a_slash(string value, bool expected)
    {
        new ResourceKey(value).IsAbsolute.Should().Be(expected);
    }

    [Fact]
    public void A_wildcard_in_a_key_is_kept_verbatim()
    {
        var key = new ResourceKey("/Tenants/*");

        key.Value.Should().Be("/Tenants/*");
        key.ToString().Should().Be("/Tenants/*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_empty_key_is_refused(string? value)
    {
        var create = () => new ResourceKey(value!);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_is_ordinal()
    {
        new ResourceKey("/Tenants/ten_1").Should().Be(new ResourceKey("/Tenants/ten_1"));
        new ResourceKey("/Tenants/ten_1").Should().NotBe(new ResourceKey("/tenants/ten_1"));
        (new ResourceKey("/Tenants/ten_1") == new ResourceKey("/Tenants/ten_1")).Should().BeTrue();
        (new ResourceKey("/Tenants/ten_1") != new ResourceKey("/tenants/ten_1")).Should().BeTrue();
        new ResourceKey("/Tenants/ten_1").GetHashCode().Should().Be(new ResourceKey("/Tenants/ten_1").GetHashCode());
    }

    [Fact]
    public void A_default_key_is_empty_and_not_absolute()
    {
        var key = default(ResourceKey);

        key.Value.Should().BeEmpty();
        key.IsAbsolute.Should().BeFalse();
    }

    [Theory]
    [InlineData("/Tenants/ten_1", "/Tenants/ten_1", true)]
    [InlineData("/Tenants/ten_1/Users/usr_1", "/Tenants/ten_1", true)]
    [InlineData("/Tenants/ten_1/*", "/Tenants/ten_1", true)]
    [InlineData("/Tenants/ten_12", "/Tenants/ten_1", false)]
    [InlineData("/Tenants/ten_1*", "/Tenants/ten_1", false)]
    [InlineData("/Tenants", "/Tenants/ten_1", false)]
    [InlineData("/Partners/par_1/Tenants/ten_1", "/Tenants/ten_1", false)]
    [InlineData("/tenants/ten_1/Users/usr_1", "/Tenants/ten_1", false)]
    [InlineData("Users/usr_1", "/Tenants/ten_1", false)]
    [InlineData("Tenants/ten_1/Users", "Tenants/ten_1", false)]
    public void IsWithin_is_true_for_the_key_itself_or_a_key_beneath_it_at_a_segment_boundary(string key, string ancestor, bool expected)
    {
        new ResourceKey(key).IsWithin(new ResourceKey(ancestor)).Should().Be(expected);
    }

    [Fact]
    public void A_default_key_is_within_nothing_and_nothing_is_within_it()
    {
        default(ResourceKey).IsWithin(new ResourceKey("/Tenants")).Should().BeFalse();
        new ResourceKey("/Tenants").IsWithin(default).Should().BeFalse();
    }

    [Theory]
    [InlineData("/Tenants/{Id}/Users", "/Tenants/")]
    [InlineData("/Tenants/ten_1", "/Tenants/")]
    [InlineData("/Partners", "/Partners/")]
    [InlineData("/", "/")]
    [InlineData("/*", "/*/")]
    public void TryGetTopLevelPrefix_gives_the_first_segment_between_slashes(string key, string expected)
    {
        new ResourceKey(key).TryGetTopLevelPrefix(out var prefix).Should().BeTrue();

        prefix.Should().Be(expected);
    }

    [Fact]
    public void A_partial_key_has_no_top_level_prefix()
    {
        new ResourceKey("Tenants/ten_1").TryGetTopLevelPrefix(out var prefix).Should().BeFalse();
        default(ResourceKey).TryGetTopLevelPrefix(out _).Should().BeFalse();

        prefix.Should().BeNull();
    }
}
