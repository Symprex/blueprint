using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class ReflectionUtilitiesTests
{
    [Fact]
    public void GetUnderlyingTypeIfNullable_Returns_Underlying_For_Nullable()
    {
        ReflectionUtilities.GetUnderlyingTypeIfNullable(typeof(int?)).Should().Be(typeof(int));
    }

    [Fact]
    public void GetUnderlyingTypeIfNullable_Returns_Same_For_NonNullable()
    {
        ReflectionUtilities.GetUnderlyingTypeIfNullable(typeof(string)).Should().Be(typeof(string));
    }

    [Fact]
    public void PrettyTypeName_Handles_Simple_Type()
    {
        ReflectionUtilities.PrettyTypeName(typeof(string)).Should().Be("String");
    }

    [Fact]
    public void PrettyTypeName_Handles_Generic_Type()
    {
        ReflectionUtilities.PrettyTypeName(typeof(Dictionary<string, int>))
            .Should().Be("Dictionary<String, Int32>");
    }

    [Fact]
    public void PrettyTypeName_Handles_Array()
    {
        ReflectionUtilities.PrettyTypeName(typeof(int[])).Should().Be("Int32[]");
    }
}
