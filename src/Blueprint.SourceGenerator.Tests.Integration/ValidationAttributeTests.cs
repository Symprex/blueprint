using Blueprint.Validation;
using FluentAssertions;
using ValidationContext = System.ComponentModel.DataAnnotations.ValidationContext;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Blueprint.SourceGenerator.Tests.Integration;

/// <summary>
/// Tests for Blueprint custom validation attributes: <see cref="RegexAttribute"/>,
/// <see cref="EmailAddressAttribute"/>, <see cref="UrlAttribute"/>,
/// <see cref="GreaterThanAttribute"/>, <see cref="LessThanAttribute"/>,
/// <see cref="MustBeTrueAttribute"/>, <see cref="NotEmptyListAttribute"/>,
/// <see cref="MaxItemsListAttribute"/>, <see cref="InFutureAttribute"/>,
/// <see cref="InPastAttribute"/>, <see cref="RequiredIfAttribute"/>,
/// <see cref="RequiredIfNotAttribute"/>, <see cref="NotAllowedIfAttribute"/>,
/// <see cref="GreaterThanPropertyAttribute"/>, and <see cref="ValidationFailures"/>.
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

    public class GreaterThanAttributeTests
    {
        [Theory]
        [InlineData(1, 0)]
        [InlineData(100, 99)]
        [InlineData(0, -1)]
        public void Value_greater_than_minimum_passes(int value, int minimum)
        {
            new GreaterThanAttribute(minimum).IsValid(value).Should().BeTrue();
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 0)]
        [InlineData(5, 10)]
        public void Value_equal_or_below_minimum_fails(int value, int minimum)
        {
            new GreaterThanAttribute(minimum).IsValid(value).Should().BeFalse();
        }

        [Fact]
        public void Null_value_passes()
        {
            new GreaterThanAttribute(0).IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Decimal_value_is_parsed_correctly()
        {
            new GreaterThanAttribute(5).IsValid(5.5m).Should().BeTrue();
            new GreaterThanAttribute(5).IsValid(4.9m).Should().BeFalse();
        }

        [Fact]
        public void Non_numeric_value_fails()
        {
            new GreaterThanAttribute(0).IsValid("not-a-number").Should().BeFalse();
        }

        [Fact]
        public void Error_message_includes_field_name_and_minimum()
        {
            var msg = new GreaterThanAttribute(10).FormatErrorMessage("Age");
            msg.Should().Contain("Age").And.Contain("10");
        }
    }

    public class LessThanAttributeTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(-1, 0)]
        [InlineData(99, 100)]
        public void Value_less_than_maximum_passes(int value, int maximum)
        {
            new LessThanAttribute(maximum).IsValid(value).Should().BeTrue();
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(10, 5)]
        public void Value_equal_or_above_maximum_fails(int value, int maximum)
        {
            new LessThanAttribute(maximum).IsValid(value).Should().BeFalse();
        }

        [Fact]
        public void Null_value_passes()
        {
            new LessThanAttribute(100).IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Error_message_includes_field_name_and_maximum()
        {
            var msg = new LessThanAttribute(50).FormatErrorMessage("Count");
            msg.Should().Contain("Count").And.Contain("50");
        }
    }

    public class MustBeTrueAttributeTests
    {
        [Fact]
        public void True_passes()
        {
            new MustBeTrueAttribute().IsValid(true).Should().BeTrue();
        }

        [Fact]
        public void False_fails()
        {
            new MustBeTrueAttribute().IsValid(false).Should().BeFalse();
        }

        [Fact]
        public void Null_fails()
        {
            new MustBeTrueAttribute().IsValid(null).Should().BeFalse();
        }

        [Fact]
        public void Non_bool_fails()
        {
            new MustBeTrueAttribute().IsValid("true").Should().BeFalse();
            new MustBeTrueAttribute().IsValid(1).Should().BeFalse();
        }
    }

    public class NotEmptyListAttributeTests
    {
        [Fact]
        public void Non_empty_list_passes()
        {
            new NotEmptyListAttribute().IsValid(new List<int> { 1 }).Should().BeTrue();
        }

        [Fact]
        public void Empty_list_fails()
        {
            new NotEmptyListAttribute().IsValid(new List<int>()).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new NotEmptyListAttribute().IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Non_collection_non_enumerable_fails()
        {
            new NotEmptyListAttribute().IsValid(42).Should().BeFalse();
        }

        [Fact]
        public void Array_with_items_passes()
        {
            new NotEmptyListAttribute().IsValid(new[] { "a" }).Should().BeTrue();
        }

        [Fact]
        public void Empty_array_fails()
        {
            new NotEmptyListAttribute().IsValid(Array.Empty<string>()).Should().BeFalse();
        }
    }

    public class MaxItemsListAttributeTests
    {
        [Fact]
        public void Within_limit_passes()
        {
            new MaxItemsListAttribute(3).IsValid(new List<int> { 1, 2 }).Should().BeTrue();
        }

        [Fact]
        public void At_limit_passes()
        {
            new MaxItemsListAttribute(2).IsValid(new List<int> { 1, 2 }).Should().BeTrue();
        }

        [Fact]
        public void Over_limit_fails()
        {
            new MaxItemsListAttribute(2).IsValid(new List<int> { 1, 2, 3 }).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new MaxItemsListAttribute(5).IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Non_collection_non_enumerable_fails()
        {
            new MaxItemsListAttribute(5).IsValid(42).Should().BeFalse();
        }

        [Fact]
        public void Error_message_includes_field_name_and_limit()
        {
            var msg = new MaxItemsListAttribute(10).FormatErrorMessage("Tags");
            msg.Should().Contain("Tags").And.Contain("10");
        }
    }

    public class InFutureAttributeTests
    {
        [Fact]
        public void Future_datetime_passes()
        {
            new InFutureAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
        }

        [Fact]
        public void Past_datetime_fails()
        {
            new InFutureAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
        }

        [Fact]
        public void Future_datetimeoffset_passes()
        {
            new InFutureAttribute().IsValid(DateTimeOffset.UtcNow.AddHours(1)).Should().BeTrue();
        }

        [Fact]
        public void Past_datetimeoffset_fails()
        {
            new InFutureAttribute().IsValid(DateTimeOffset.UtcNow.AddHours(-1)).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new InFutureAttribute().IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Non_date_type_fails()
        {
            new InFutureAttribute().IsValid("2099-01-01").Should().BeFalse();
        }
    }

    public class InPastAttributeTests
    {
        [Fact]
        public void Past_datetime_passes()
        {
            new InPastAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
        }

        [Fact]
        public void Future_datetime_fails()
        {
            new InPastAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new InPastAttribute().IsValid(null).Should().BeTrue();
        }
    }

    public class RequiredIfAttributeTests
    {
        private class TestModel
        {
            public string? Status { get; set; }

            [RequiredIf(nameof(Status), "Active")]
            public string? Reason { get; set; }
        }

        [Fact]
        public void When_dependent_matches_and_value_missing_then_fails()
        {
            var model = new TestModel { Status = "Active", Reason = null };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Reason) };
            var attr = new RequiredIfAttribute(nameof(TestModel.Status), "Active");

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_and_value_present_then_passes()
        {
            var model = new TestModel { Status = "Active", Reason = "Because" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Reason) };
            var attr = new RequiredIfAttribute(nameof(TestModel.Status), "Active");

            var result = attr.GetValidationResult("Because", context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_does_not_match_then_passes()
        {
            var model = new TestModel { Status = "Inactive", Reason = null };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Reason) };
            var attr = new RequiredIfAttribute(nameof(TestModel.Status), "Active");

            var result = attr.GetValidationResult(null, context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_property_missing_then_returns_error()
        {
            var model = new TestModel();
            var context = new ValidationContext(model) { MemberName = "Reason" };
            var attr = new RequiredIfAttribute("NonExistentProperty", "x");

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
            result!.ErrorMessage.Should().Contain("NonExistentProperty");
        }

        [Fact]
        public void When_dependent_value_is_array_then_matches_any()
        {
            var model = new TestModel { Status = "Pending" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Reason) };
            var attr = new RequiredIfAttribute(nameof(TestModel.Status), new[] { "Active", "Pending" });

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
        }
    }

    public class RequiredIfNotAttributeTests
    {
        private class TestModel
        {
            public string? Type { get; set; }

            public string? Details { get; set; }
        }

        [Fact]
        public void When_dependent_does_not_match_and_value_missing_then_fails()
        {
            var model = new TestModel { Type = "Custom" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Details) };
            var attr = new RequiredIfNotAttribute(nameof(TestModel.Type), "Standard");

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_then_passes_regardless()
        {
            var model = new TestModel { Type = "Standard" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Details) };
            var attr = new RequiredIfNotAttribute(nameof(TestModel.Type), "Standard");

            var result = attr.GetValidationResult(null, context);

            result.Should().Be(ValidationResult.Success);
        }
    }

    public class RequiredItemsIfAttributeTests
    {
        private class TestModel
        {
            public bool RestrictFormats { get; set; }

            [RequiredItemsIf(nameof(RestrictFormats), true)]
            public List<string>? Formats { get; set; }
        }

        [Fact]
        public void When_dependent_matches_and_collection_empty_then_fails()
        {
            var model = new TestModel { RestrictFormats = true, Formats = new List<string>() };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Formats) };
            var attr = new RequiredItemsIfAttribute(nameof(TestModel.RestrictFormats), true);

            var result = attr.GetValidationResult(model.Formats, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_and_collection_null_then_fails()
        {
            var model = new TestModel { RestrictFormats = true, Formats = null };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Formats) };
            var attr = new RequiredItemsIfAttribute(nameof(TestModel.RestrictFormats), true);

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_and_collection_has_items_then_passes()
        {
            var model = new TestModel { RestrictFormats = true, Formats = new List<string> { "pdf" } };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Formats) };
            var attr = new RequiredItemsIfAttribute(nameof(TestModel.RestrictFormats), true);

            var result = attr.GetValidationResult(model.Formats, context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_does_not_match_then_empty_collection_passes()
        {
            var model = new TestModel { RestrictFormats = false, Formats = new List<string>() };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Formats) };
            var attr = new RequiredItemsIfAttribute(nameof(TestModel.RestrictFormats), true);

            var result = attr.GetValidationResult(model.Formats, context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_property_missing_then_returns_error()
        {
            var model = new TestModel();
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Formats) };
            var attr = new RequiredItemsIfAttribute("NonExistentProperty", true);

            var result = attr.GetValidationResult(null, context);

            result.Should().NotBe(ValidationResult.Success);
            result!.ErrorMessage.Should().Contain("NonExistentProperty");
        }

        [Fact]
        public void Error_message_is_legacys()
        {
            // Signature365 8b40ecc961, Blueprint/Validation/RequiredItemsIfAttribute.cs:24, :34, formatted with the display name
            // by ValidationAttribute.FormatErrorMessage. Blank items fail too (:98), hence "non-empty".
            var msg = new RequiredItemsIfAttribute(nameof(TestModel.RestrictFormats), true).FormatErrorMessage("Formats");
            msg.Should().Be("The Formats field must contain at least one non-empty item if RestrictFormats field is True.");
        }
    }

    public class RequiredItemsIfNotAttributeTests
    {
        private class TestModel
        {
            public string? Mode { get; set; }

            [RequiredItemsIfNot(nameof(Mode), "All")]
            public List<string>? Selected { get; set; }
        }

        [Fact]
        public void When_dependent_does_not_match_and_collection_empty_then_fails()
        {
            var model = new TestModel { Mode = "Some", Selected = new List<string>() };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Selected) };
            var attr = new RequiredItemsIfNotAttribute(nameof(TestModel.Mode), "All");

            var result = attr.GetValidationResult(model.Selected, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_then_empty_collection_passes()
        {
            var model = new TestModel { Mode = "All", Selected = new List<string>() };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Selected) };
            var attr = new RequiredItemsIfNotAttribute(nameof(TestModel.Mode), "All");

            var result = attr.GetValidationResult(model.Selected, context);

            result.Should().Be(ValidationResult.Success);
        }
    }

    public class NotAllowedIfAttributeTests
    {
        private class TestModel
        {
            public string? Mode { get; set; }

            public string? Override { get; set; }
        }

        [Fact]
        public void When_dependent_matches_and_value_present_then_fails()
        {
            var model = new TestModel { Mode = "Auto", Override = "yes" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Override) };
            var attr = new NotAllowedIfAttribute(nameof(TestModel.Mode), "Auto");

            var result = attr.GetValidationResult("yes", context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_matches_and_value_null_then_passes()
        {
            var model = new TestModel { Mode = "Auto" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Override) };
            var attr = new NotAllowedIfAttribute(nameof(TestModel.Mode), "Auto");

            var result = attr.GetValidationResult(null, context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_dependent_does_not_match_then_passes()
        {
            var model = new TestModel { Mode = "Manual", Override = "yes" };
            var context = new ValidationContext(model) { MemberName = nameof(TestModel.Override) };
            var attr = new NotAllowedIfAttribute(nameof(TestModel.Mode), "Auto");

            var result = attr.GetValidationResult("yes", context);

            result.Should().Be(ValidationResult.Success);
        }
    }

    public class GreaterThanPropertyAttributeTests
    {
        private class DateRangeModel
        {
            public DateTime Start { get; set; }

            public DateTime End { get; set; }
        }

        [Fact]
        public void When_value_greater_than_other_property_then_passes()
        {
            var model = new DateRangeModel
            {
                Start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            };
            var context = new ValidationContext(model) { MemberName = nameof(DateRangeModel.End) };
            var attr = new GreaterThanPropertyAttribute(nameof(DateRangeModel.Start));

            var result = attr.GetValidationResult(model.End, context);

            result.Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_value_less_than_other_property_then_fails()
        {
            var model = new DateRangeModel
            {
                Start = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            var context = new ValidationContext(model) { MemberName = nameof(DateRangeModel.End) };
            var attr = new GreaterThanPropertyAttribute(nameof(DateRangeModel.Start));

            var result = attr.GetValidationResult(model.End, context);

            result.Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_either_value_null_then_passes()
        {
            var model = new DateRangeModel();
            var context = new ValidationContext(model) { MemberName = "End" };
            var attr = new GreaterThanPropertyAttribute("Start");

            var result = attr.GetValidationResult(null, context);

            result.Should().Be(ValidationResult.Success);
        }
    }

    public class ValidationFailuresTests
    {
        [Fact]
        public void Empty_failures_has_no_failures()
        {
            var failures = new ValidationFailures();

            failures.HasFailures.Should().BeFalse();
            failures.Count.Should().Be(0);
        }

        [Fact]
        public void Add_failure_records_property_and_message()
        {
            var failures = new ValidationFailures();
            failures.AddFailure("Name", "Name is required.");

            failures.HasFailures.Should().BeTrue();
            failures.Count.Should().Be(1);

            var dict = failures.AsDictionary();
            dict.Should().ContainKey("Name");
            dict["Name"].Should().Contain("Name is required.");
        }

        [Fact]
        public void Multiple_failures_for_same_property_are_grouped()
        {
            var failures = new ValidationFailures();
            failures.AddFailure("Email", "Required.");
            failures.AddFailure("Email", "Invalid format.");

            failures.Count.Should().Be(1);
            failures.AsDictionary()["Email"].Should().HaveCount(2);
        }

        [Fact]
        public void AddFailure_with_ValidationResult_uses_member_names()
        {
            var failures = new ValidationFailures();
            failures.AddFailure(new ValidationResult("Bad value", ["FieldA"]));

            failures.AsDictionary().Should().ContainKey("FieldA");
        }

        [Fact]
        public void AddFailure_with_no_member_names_uses_form_level()
        {
            var failures = new ValidationFailures();
            failures.AddFailure(new ValidationResult("Form error"));

            failures.AsDictionary().Should().ContainKey(ValidationFailures.FormLevelPropertyName);
        }

        [Fact]
        public void ThrowIfFailed_throws_when_failures_exist()
        {
            var failures = new ValidationFailures();
            failures.AddFailure("X", "Bad");

            var act = () => failures.ThrowIfFailed();
            act.Should().Throw<Blueprint.Validation.ValidationException>();
        }

        [Fact]
        public void ThrowIfFailed_does_not_throw_when_empty()
        {
            var failures = new ValidationFailures();

            var act = () => failures.ThrowIfFailed();
            act.Should().NotThrow();
        }

        [Fact]
        public void ToException_returns_null_when_empty()
        {
            new ValidationFailures().ToException().Should().BeNull();
        }

        [Fact]
        public void ToException_returns_exception_when_has_failures()
        {
            var failures = new ValidationFailures();
            failures.AddFailure("X", "Bad");

            failures.ToException().Should().NotBeNull();
        }

        [Fact]
        public void None_instance_has_no_failures()
        {
            ValidationFailures.None.HasFailures.Should().BeFalse();
            ValidationFailures.None.Count.Should().Be(0);
        }

        [Fact]
        public void AsDictionary_returns_shared_empty_when_no_failures()
        {
            var dict1 = new ValidationFailures().AsDictionary();
            var dict2 = new ValidationFailures().AsDictionary();
            dict1.Should().BeSameAs(dict2);
        }
    }

    public class GreaterThanOrEqualToAttributeTests
    {
        [Theory]
        [InlineData(5, 5)]
        [InlineData(6, 5)]
        public void Value_at_or_above_minimum_passes(int value, int minimum)
        {
            new GreaterThanOrEqualToAttribute(minimum).IsValid(value).Should().BeTrue();
        }

        [Fact]
        public void Value_below_minimum_fails()
        {
            new GreaterThanOrEqualToAttribute(5).IsValid(4).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new GreaterThanOrEqualToAttribute(0).IsValid(null).Should().BeTrue();
        }
    }

    public class LessThanOrEqualToAttributeTests
    {
        [Theory]
        [InlineData(5, 5)]
        [InlineData(4, 5)]
        public void Value_at_or_below_maximum_passes(int value, int maximum)
        {
            new LessThanOrEqualToAttribute(maximum).IsValid(value).Should().BeTrue();
        }

        [Fact]
        public void Value_above_maximum_fails()
        {
            new LessThanOrEqualToAttribute(5).IsValid(6).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new LessThanOrEqualToAttribute(100).IsValid(null).Should().BeTrue();
        }
    }

    public class NotInFutureAttributeTests
    {
        [Fact]
        public void Past_datetime_passes()
        {
            new NotInFutureAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
        }

        [Fact]
        public void Future_datetime_fails()
        {
            new NotInFutureAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new NotInFutureAttribute().IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Future_datetimeoffset_fails()
        {
            new NotInFutureAttribute().IsValid(DateTimeOffset.UtcNow.AddHours(1)).Should().BeFalse();
        }
    }

    public class NotInPastAttributeTests
    {
        [Fact]
        public void Future_datetime_passes()
        {
            new NotInPastAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
        }

        [Fact]
        public void Past_datetime_fails()
        {
            new NotInPastAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new NotInPastAttribute().IsValid(null).Should().BeTrue();
        }
    }

    public class LessThanPropertyAttributeTests
    {
        private class RangeModel
        {
            public int Min { get; set; }

            public int Max { get; set; }
        }

        [Fact]
        public void When_value_less_than_other_then_passes()
        {
            var model = new RangeModel { Min = 1, Max = 10 };
            var context = new ValidationContext(model) { MemberName = nameof(RangeModel.Min) };
            var attr = new LessThanPropertyAttribute(nameof(RangeModel.Max));

            attr.GetValidationResult(model.Min, context).Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_value_greater_than_other_then_fails()
        {
            var model = new RangeModel { Min = 10, Max = 1 };
            var context = new ValidationContext(model) { MemberName = nameof(RangeModel.Min) };
            var attr = new LessThanPropertyAttribute(nameof(RangeModel.Max));

            attr.GetValidationResult(model.Min, context).Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_either_null_then_passes()
        {
            var model = new RangeModel();
            var context = new ValidationContext(model) { MemberName = "Min" };
            var attr = new LessThanPropertyAttribute("Max");

            attr.GetValidationResult(null, context).Should().Be(ValidationResult.Success);
        }
    }

    public class MustBeGreaterThanOrEqualAttributeTests
    {
        private class CompareModel
        {
            public int Baseline { get; set; }

            public int Value { get; set; }
        }

        [Fact]
        public void When_value_greater_than_other_then_passes()
        {
            var model = new CompareModel { Baseline = 5, Value = 10 };
            var context = new ValidationContext(model) { MemberName = nameof(CompareModel.Value) };
            var attr = new MustBeGreaterThanOrEqualAttribute(nameof(CompareModel.Baseline));

            attr.GetValidationResult(model.Value, context).Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_value_equal_to_other_then_passes()
        {
            var model = new CompareModel { Baseline = 5, Value = 5 };
            var context = new ValidationContext(model) { MemberName = nameof(CompareModel.Value) };
            var attr = new MustBeGreaterThanOrEqualAttribute(nameof(CompareModel.Baseline));

            attr.GetValidationResult(model.Value, context).Should().Be(ValidationResult.Success);
        }

        [Fact]
        public void When_value_less_than_other_then_fails()
        {
            var model = new CompareModel { Baseline = 10, Value = 5 };
            var context = new ValidationContext(model) { MemberName = nameof(CompareModel.Value) };
            var attr = new MustBeGreaterThanOrEqualAttribute(nameof(CompareModel.Baseline));

            attr.GetValidationResult(model.Value, context).Should().NotBe(ValidationResult.Success);
        }

        [Fact]
        public void When_either_null_then_passes()
        {
            var model = new CompareModel();
            var context = new ValidationContext(model) { MemberName = "Value" };
            var attr = new MustBeGreaterThanOrEqualAttribute("Baseline");

            attr.GetValidationResult(null, context).Should().Be(ValidationResult.Success);
        }
    }

    public class PasswordAttributeTests
    {
        [Theory]
        [InlineData("Passw0rd!")]
        [InlineData("Str0ng!Pass")]
        [InlineData("C0mpl3x#Password")]
        public void When_strong_password_then_passes(string password)
        {
            new PasswordAttribute().IsValid(password).Should().BeTrue();
        }

        [Theory]
        [InlineData("short1!")]
        [InlineData("nouppercase1!")]
        [InlineData("NOLOWERCASE1!")]
        [InlineData("NoDigits!!")]
        [InlineData("NoSpecial1a")]
        public void When_weak_password_then_fails(string password)
        {
            new PasswordAttribute().IsValid(password).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new PasswordAttribute().IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void Empty_passes()
        {
            new PasswordAttribute().IsValid(string.Empty).Should().BeTrue();
        }
    }

    public class PhoneNumberAttributeTests
    {
        [Theory]
        [InlineData("+447911123456")]
        [InlineData("07911123456")]
        [InlineData("+1 555 1234567")]
        public void When_valid_phone_then_passes(string phone)
        {
            new PhoneNumberAttribute().IsValid(phone).Should().BeTrue();
        }

        [Theory]
        [InlineData("not-a-phone")]
        [InlineData("abc")]
        public void When_invalid_phone_then_fails(string phone)
        {
            new PhoneNumberAttribute().IsValid(phone).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new PhoneNumberAttribute().IsValid(null).Should().BeTrue();
        }
    }

    public class UkPostcodeAttributeTests
    {
        [Theory]
        [InlineData("SW1A 1AA")]
        [InlineData("EC2A 3AR")]
        [InlineData("M1 1AA")]
        [InlineData("B33 8TH")]
        public void When_valid_uk_postcode_then_passes(string postcode)
        {
            new UkPostcodeAttribute().IsValid(postcode).Should().BeTrue();
        }

        [Theory]
        [InlineData("INVALID")]
        [InlineData("12345")]
        [InlineData("ABC DEF")]
        public void When_invalid_uk_postcode_then_fails(string postcode)
        {
            new UkPostcodeAttribute().IsValid(postcode).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new UkPostcodeAttribute().IsValid(null).Should().BeTrue();
        }
    }

    public class ResourceKeyAttributeTests
    {
        [Theory]
        [InlineData("Tenant/5C65D124-F856-46A4-BB3F-19764429BA1F")]
        [InlineData("user/00000000-0000-0000-0000-000000000000")]
        [InlineData("template123/a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
        public void When_valid_resource_key_then_passes(string key)
        {
            new ResourceKeyAttribute().IsValid(key).Should().BeTrue();
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("/missing-prefix")]
        [InlineData("Tenant/not-a-guid")]
        [InlineData("1starts-with-digit/5C65D124-F856-46A4-BB3F-19764429BA1F")]
        public void When_invalid_resource_key_then_fails(string key)
        {
            new ResourceKeyAttribute().IsValid(key).Should().BeFalse();
        }

        [Fact]
        public void Null_passes()
        {
            new ResourceKeyAttribute().IsValid(null).Should().BeTrue();
        }
    }
}
