using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class DictionaryExtensionsTests
{
    [Fact]
    public void GetOrAdd_Returns_Existing_Value()
    {
        var dict = new Dictionary<string, int> { ["key"] = 42 };

        dict.GetOrAdd("key", static _ => 99).Should().Be(42);
    }

    [Fact]
    public void GetOrAdd_Creates_Value_When_Missing()
    {
        var dict = new Dictionary<string, int>();

        dict.GetOrAdd("key", static _ => 99).Should().Be(99);
        dict["key"].Should().Be(99);
    }
}
