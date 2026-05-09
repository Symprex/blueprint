using Blueprint.Utilities;

namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// Validates the pre-compiled regular expressions in <see cref="CommonRegularExpressions"/>
/// against known valid and invalid inputs.
/// </summary>
public class CommonRegularExpressionsTests
{
    public class EmailOnlyTests
    {
        [Theory]
        [InlineData("user@example.com")]
        [InlineData("user.name@domain.co.uk")]
        [InlineData("first+last@domain.com")]
        [InlineData("  user@example.com  ")]
        public void Matches_valid_email(string input)
        {
            Assert.Matches(CommonRegularExpressions.EmailOnly, input);
        }

        [Theory]
        [InlineData("not-an-email")]
        [InlineData("@missing-local.com")]
        [InlineData("missing-domain@")]
        [InlineData("two@@ats.com")]
        public void Rejects_invalid_email(string input)
        {
            Assert.DoesNotMatch(CommonRegularExpressions.EmailOnly, input);
        }
    }

    public class EmailTests
    {
        [Theory]
        [InlineData("Contact us at user@example.com for info")]
        [InlineData("Email: admin@domain.org")]
        public void Finds_email_within_text(string input)
        {
            Assert.Matches(CommonRegularExpressions.Email, input);
        }

        [Theory]
        [InlineData("No email here")]
        [InlineData("just @ symbol")]
        public void No_match_when_no_email_present(string input)
        {
            Assert.DoesNotMatch(CommonRegularExpressions.Email, input);
        }
    }

    public class UrlOnlyTests
    {
        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com/path")]
        [InlineData("example.com")]
        [InlineData("  https://example.com  ")]
        public void Matches_valid_url(string input)
        {
            Assert.Matches(CommonRegularExpressions.UrlOnly, input);
        }

        [Theory]
        [InlineData("not a url")]
        [InlineData("")]
        public void Rejects_invalid_url(string input)
        {
            Assert.DoesNotMatch(CommonRegularExpressions.UrlOnly, input);
        }
    }

    public class UrlWithProtocolTests
    {
        [Theory]
        [InlineData("http://example.com")]
        [InlineData("https://example.com/path?q=1")]
        [InlineData("ftp://files.example.com")]
        public void Matches_url_with_protocol(string input)
        {
            Assert.Matches(CommonRegularExpressions.UrlWithProtocol, input);
        }

        [Theory]
        [InlineData("example.com")]
        [InlineData("www.example.com")]
        public void Rejects_url_without_protocol(string input)
        {
            Assert.DoesNotMatch(CommonRegularExpressions.UrlWithProtocol, input);
        }
    }

    public class PhoneNumberOnlyTests
    {
        [Theory]
        [InlineData("01234 5678")]
        [InlineData("+44 20 7946 0958")]
        [InlineData("(123) 456-7890")]
        public void Matches_valid_phone_number(string input)
        {
            Assert.Matches(CommonRegularExpressions.PhoneNumberOnly, input);
        }
    }

    public class UkPostcodeOnlyTests
    {
        [Theory]
        [InlineData("SW1A 1AA")]
        [InlineData("EC1A 1BB")]
        [InlineData("W1A 0AX")]
        [InlineData("GIR 0AA")]
        public void Matches_valid_uk_postcode(string input)
        {
            Assert.Matches(CommonRegularExpressions.UkPostcodeOnly, input);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("not a postcode")]
        public void Rejects_invalid_postcode(string input)
        {
            Assert.DoesNotMatch(CommonRegularExpressions.UkPostcodeOnly, input);
        }
    }
}
