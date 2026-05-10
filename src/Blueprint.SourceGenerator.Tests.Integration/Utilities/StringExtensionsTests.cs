using Blueprint.Utilities;

using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Utilities;

/// <summary>
/// Tests for <see cref="StringExtensions"/> casing transformations and pluralization.
/// Covers the core inflector-style conversions (Pascalize, Camelize), general-purpose
/// casing (ToPascalCase, ToCamelCase), Uncapitalize, and the Pluralize extension that
/// delegates to <see cref="Inflector"/>.
/// </summary>
public class StringExtensionsTests
{
    public class PascalizeTests
    {
        [Theory]
        [InlineData("order_id", "OrderId")]
        [InlineData("first_name", "FirstName")]
        [InlineData("hello_world", "HelloWorld")]
        [InlineData("some_long_name", "SomeLongName")]
        [InlineData("already", "Already")]
        public void Converts_underscore_separated_words(string input, string expected)
        {
            input.Pascalize().Should().Be(expected);
        }
    }

    public class CamelizeTests
    {
        [Theory]
        [InlineData("order_id", "orderId")]
        [InlineData("first_name", "firstName")]
        [InlineData("hello_world", "helloWorld")]
        [InlineData("some_long_name", "someLongName")]
        public void Converts_underscore_separated_to_camelCase(string input, string expected)
        {
            input.Camelize().Should().Be(expected);
        }
    }

    public class ToPascalCaseTests
    {
        [Theory]
        [InlineData("hello world", "HelloWorld")]
        [InlineData("hello-world", "HelloWorld")]
        [InlineData("hello_world", "HelloWorld")]
        [InlineData("some long name", "SomeLongName")]
        [InlineData("with-dashes", "WithDashes")]
        [InlineData("with_underscores", "WithUnderscores")]
        public void Converts_separated_words_to_PascalCase(string input, string expected)
        {
            input.ToPascalCase().Should().Be(expected);
        }

        [Theory]
        [InlineData("item1name", "Item1Name")]
        [InlineData("test2value", "Test2Value")]
        public void Handles_digits_as_word_boundaries(string input, string expected)
        {
            input.ToPascalCase().Should().Be(expected);
        }
    }

    public class ToCamelCaseTests
    {
        [Theory]
        [InlineData("hello world", "helloWorld")]
        [InlineData("hello-world", "helloWorld")]
        [InlineData("hello_world", "helloWorld")]
        [InlineData("FirstName", "firstName")]
        public void Converts_separated_words_to_camelCase(string input, string expected)
        {
            input.ToCamelCase().Should().Be(expected);
        }

        [Fact]
        public void Returns_empty_for_empty_string()
        {
            string.Empty.ToCamelCase().Should().BeEmpty();
        }

        [Fact]
        public void Returns_null_for_null()
        {
            string? value = null;
            value!.ToCamelCase().Should().BeNull();
        }
    }

    public class UncapitalizeTests
    {
        [Theory]
        [InlineData("Hello", "hello")]
        [InlineData("World", "world")]
        [InlineData("ABC", "aBC")]
        [InlineData("already", "already")]
        public void Lowercases_first_character(string input, string expected)
        {
            input.Uncapitalize().Should().Be(expected);
        }

        [Fact]
        public void Returns_empty_for_empty_string()
        {
            string.Empty.Uncapitalize().Should().BeEmpty();
        }

        [Fact]
        public void Returns_null_for_null()
        {
            string? value = null;
            value!.Uncapitalize().Should().BeNull();
        }
    }

    public class PluralizeTests
    {
        [Theory]
        [InlineData("user", "users")]
        [InlineData("query", "queries")]
        [InlineData("status", "statuses")]
        [InlineData("bus", "buses")]
        [InlineData("box", "boxes")]
        [InlineData("church", "churches")]
        [InlineData("wish", "wishes")]
        [InlineData("hive", "hives")]
        [InlineData("wife", "wives")]
        [InlineData("wolf", "wolves")]
        [InlineData("tomato", "tomatoes")]
        public void Handles_regular_plural_rules(string singular, string expected)
        {
            singular.Pluralize().Should().Be(expected);
        }

        [Theory]
        [InlineData("person", "people")]
        [InlineData("man", "men")]
        [InlineData("child", "children")]
        [InlineData("sex", "sexes")]
        [InlineData("move", "moves")]
        public void Handles_irregular_nouns(string singular, string expected)
        {
            singular.Pluralize().Should().Be(expected);
        }

        [Theory]
        [InlineData("equipment")]
        [InlineData("information")]
        [InlineData("rice")]
        [InlineData("money")]
        [InlineData("species")]
        [InlineData("series")]
        [InlineData("fish")]
        [InlineData("sheep")]
        [InlineData("deer")]
        [InlineData("aircraft")]
        public void Returns_uncountable_words_unchanged(string word)
        {
            word.Pluralize().Should().Be(word);
        }
    }
}
