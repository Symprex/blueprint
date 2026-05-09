using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class TypeExtensionsTests
{
    [Fact]
    public void GetTypeHierarchy_Returns_Self_Interfaces_And_BaseTypes()
    {
        var hierarchy = typeof(List<string>).GetTypeHierarchy().ToList();

        hierarchy.Should().Contain(typeof(List<string>));
        hierarchy.Should().Contain(typeof(IList<string>));
        hierarchy.Should().Contain(typeof(IEnumerable<string>));
        hierarchy.Should().Contain(typeof(object));
    }

    [Fact]
    public void IsGenericTypeWithOneArgument_Matches_Correct_Type()
    {
        typeof(List<string>).IsGenericTypeWithOneArgument(typeof(List<>), out var arg)
            .Should().BeTrue();

        arg.Should().Be(typeof(string));
    }

    [Fact]
    public void IsGenericTypeWithOneArgument_Returns_False_For_Non_Generic()
    {
        typeof(string).IsGenericTypeWithOneArgument(typeof(List<>), out var arg)
            .Should().BeFalse();

        arg.Should().BeNull();
    }

    [Fact]
    public void IsOfGenericType_Returns_True_For_Implemented_Interface()
    {
        typeof(List<int>).IsOfGenericType(typeof(IEnumerable<>)).Should().BeTrue();
    }

    [Fact]
    public void IsOfGenericType_Returns_False_For_Unrelated_Type()
    {
        typeof(string).IsOfGenericType(typeof(IDictionary<,>)).Should().BeFalse();
    }

    [Fact]
    public void IsOfGenericType_Returns_Concrete_Type()
    {
        typeof(List<int>).IsOfGenericType(typeof(IList<>), out var concrete).Should().BeTrue();
        concrete.Should().Be(typeof(IList<int>));
    }
}
