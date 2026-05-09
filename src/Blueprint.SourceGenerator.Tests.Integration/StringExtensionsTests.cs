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

    public class CamelizeMethod
    {
        [Theory]
        [InlineData("order_id", "orderId")]
        [InlineData("first_name", "firstName")]
        [InlineData("already", "already")]
        [InlineData("multi_word_string", "multiWordString")]
        public void Should_convert_underscore_separated_to_camelCase(string input, string expected)
        {
            input.Camelize().Should().Be(expected);
        }
    }

    public class UncapitalizeMethod
    {
        [Theory]
        [InlineData("Hello", "hello")]
        [InlineData("UPPER", "uPPER")]
        [InlineData("already", "already")]
        [InlineData("A", "a")]
        [InlineData("", "")]
        public void Should_lowercase_first_character(string input, string expected)
        {
            input.Uncapitalize().Should().Be(expected);
        }
    }

    public class PluralizeMethod
    {
        [Theory]
        [InlineData("user", "users")]
        [InlineData("query", "queries")]
        [InlineData("status", "statuses")]
        [InlineData("person", "people")]
        [InlineData("child", "children")]
        [InlineData("bus", "buses")]
        [InlineData("fox", "foxes")]
        [InlineData("sheep", "sheep")]
        [InlineData("fish", "fish")]
        [InlineData("quiz", "quizzes")]
        [InlineData("mouse", "mice")]
        [InlineData("ox", "oxen")]
        [InlineData("hive", "hives")]
        [InlineData("tomato", "tomatoes")]
        public void Should_pluralize_english_words(string input, string expected)
        {
            input.Pluralize().Should().Be(expected);
        }
    }
}
