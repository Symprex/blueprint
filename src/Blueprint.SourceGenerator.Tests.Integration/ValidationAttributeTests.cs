using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// Tests for <see cref="RegexAttribute"/>, <see cref="EmailAddressAttribute"/>,
/// <see cref="UrlAttribute"/>, and <see cref="UrlProtocol"/>.
/// </summary>
public class ValidationAttributeTests
{
    public class EmailAddressAttributeTests
    {
        [Theory]
        [InlineData("user@example.com")]
        [InlineData("user.name+tag@example.co.uk")]
        [InlineData("a@b.cc")]
        public void Valid_email_addresses_pass(string email)
        {
            var attr = new EmailAddressAttribute();
            attr.IsValid(email).Should().BeTrue();
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("@missing-local.com")]
        [InlineData("user@")]
        [InlineData("user@.com")]
        public void Invalid_email_addresses_fail(string email)
        {
            var attr = new EmailAddressAttribute();
            attr.IsValid(email).Should().BeFalse();
        }

        [Fact]
        public void Null_and_empty_pass_validation()
        {
            var attr = new EmailAddressAttribute();
            attr.IsValid(null).Should().BeTrue();
            attr.IsValid(string.Empty).Should().BeTrue();
        }

        [Fact]
        public void Custom_error_message_is_used()
        {
            var attr = new EmailAddressAttribute("Bad email!");
            attr.FormatErrorMessage("Email").Should().Contain("Bad email!");
        }

        [Fact]
        public void Error_message_accessor_is_used()
        {
            var attr = new EmailAddressAttribute(() => "Accessor message");
            attr.FormatErrorMessage("Email").Should().Contain("Accessor message");
        }
    }

    public class UrlAttributeTests
    {
        [Theory]
        [InlineData("example.com")]
        [InlineData("http://example.com")]
        [InlineData("https://example.com/path?q=1")]
        [InlineData("ftp://files.example.com")]
        public void Valid_urls_pass_without_protocol_requirement(string url)
        {
            var attr = new UrlAttribute(UrlProtocol.NotRequired);
            attr.IsValid(url).Should().BeTrue();
        }

        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com/path")]
        [InlineData("ftp://files.example.com")]
        public void Valid_urls_pass_with_protocol_requirement(string url)
        {
            var attr = new UrlAttribute(UrlProtocol.Required);
            attr.IsValid(url).Should().BeTrue();
        }

        [Theory]
        [InlineData("example.com")]
        [InlineData("www.example.com/path")]
        public void Bare_domain_fails_when_protocol_required(string url)
        {
            var attr = new UrlAttribute(UrlProtocol.Required);
            attr.IsValid(url).Should().BeFalse();
        }

        [Fact]
        public void Null_and_empty_pass_validation()
        {
            var attr = new UrlAttribute();
            attr.IsValid(null).Should().BeTrue();
            attr.IsValid(string.Empty).Should().BeTrue();
        }

        [Fact]
        public void Default_protocol_is_not_required()
        {
            var attr = new UrlAttribute();
            attr.IsValid("example.com").Should().BeTrue();
        }

        [Fact]
        public void Custom_error_message_is_used()
        {
            var attr = new UrlAttribute("Bad URL!", UrlProtocol.Required);
            attr.FormatErrorMessage("Website").Should().Contain("Bad URL!");
        }

        [Fact]
        public void Error_message_accessor_is_used()
        {
            var attr = new UrlAttribute(() => "Accessor url msg");
            attr.FormatErrorMessage("Website").Should().Contain("Accessor url msg");
        }

        [Fact]
        public void Regex_property_is_exposed()
        {
            var attr = new UrlAttribute(UrlProtocol.Required);
            attr.Regex.Should().NotBeNull();
            attr.Regex.Should().BeSameAs(Blueprint.Utilities.CommonRegularExpressions.UrlWithProtocol);
        }
    }
}
