using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration;

public class StringExtensionsTests
{
    public class PascalizeMethod
    {
        [Theory]
        [InlineData("order_id", "OrderId")]
        [InlineData("first_name", "FirstName")]
        [InlineData("already", "Already")]
        [InlineData("multi_word_string", "MultiWordString")]
        [InlineData("a", "A")]
        public void Should_convert_underscore_separated_to_PascalCase(string input, string expected)
        {
            input.Pascalize().Should().Be(expected);
        }
    }

    public class ToPascalCaseMethod
    {
        [Theory]
        [InlineData("order id", "OrderId")]
        [InlineData("first-name", "FirstName")]
        [InlineData("some_value", "SomeValue")]
        [InlineData("already", "Already")]
        [InlineData("camelCase", "CamelCase")]
        [InlineData("with123number", "With123Number")]
        [InlineData("UPPER", "Upper")]
        public void Should_convert_to_PascalCase(string input, string expected)
        {
            input.ToPascalCase().Should().Be(expected);
        }
    }

    public class ToCamelCaseMethod
    {
        [Theory]
        [InlineData("OrderId", "orderId")]
        [InlineData("first-name", "firstName")]
        [InlineData("some_value", "someValue")]
        [InlineData("Already", "already")]
        [InlineData("", "")]
        public void Should_convert_to_camelCase(string input, string expected)
        {
            input.ToCamelCase().Should().Be(expected);
        }
    }
}
