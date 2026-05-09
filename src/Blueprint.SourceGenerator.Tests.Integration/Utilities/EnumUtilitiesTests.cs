using System.ComponentModel;

using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

public class EnumUtilitiesTests
{
    private enum TestEnum
    {
        [Description("First value")]
        One,

        [Description("Second value")]
        Two,

        NoDescription,
    }

    [Fact]
    public void GetDescription_Returns_Attribute_Value()
    {
        TestEnum.One.GetDescription().Should().Be("First value");
    }

    [Fact]
    public void GetDescription_Falls_Back_To_ToString()
    {
        TestEnum.NoDescription.GetDescription().Should().Be("NoDescription");
    }

    [Fact]
    public void GetAllItems_Returns_All_Values()
    {
        EnumUtilities.GetAllItems<TestEnum>().Should().HaveCount(3);
    }

    [Fact]
    public void FromDescription_Finds_By_Description()
    {
        EnumUtilities.FromDescription<TestEnum>("First value").Should().Be(TestEnum.One);
    }

    [Fact]
    public void FromDescription_Finds_By_Name()
    {
        EnumUtilities.FromDescription<TestEnum>("NoDescription").Should().Be(TestEnum.NoDescription);
    }

    [Fact]
    public void FromDescription_Throws_When_Not_Found()
    {
        var act = () => EnumUtilities.FromDescription<TestEnum>("invalid");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFromDescription_Returns_False_When_Not_Found()
    {
        EnumUtilities.TryFromDescription<TestEnum>("invalid", out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromDescription_Returns_True_When_Found()
    {
        EnumUtilities.TryFromDescription<TestEnum>("Second value", out var value).Should().BeTrue();
        value.Should().Be(TestEnum.Two);
    }
}
