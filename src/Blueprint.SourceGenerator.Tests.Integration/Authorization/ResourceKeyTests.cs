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
}
