using System.ComponentModel.DataAnnotations;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Validation;

/// <summary>
/// Tests for the additional Blueprint validation attributes ported from legacy Blueprint:
/// <see cref="MustBeTrueAttribute"/>, <see cref="NotEmptyListAttribute"/>,
/// <see cref="MaxItemsListAttribute"/>, date-temporal validators, comparison validators,
/// <see cref="PhoneNumberAttribute"/>, <see cref="UkPostcodeAttribute"/>,
/// <see cref="PasswordAttribute"/>, and <see cref="ResourceKeyAttribute"/>.
/// </summary>
public class AdditionalValidationAttributeTests
{
    // --- MustBeTrueAttribute ---

    [Fact]
    public void MustBeTrue_when_true_then_valid()
    {
        new MustBeTrueAttribute().IsValid(true).Should().BeTrue();
    }

    [Fact]
    public void MustBeTrue_when_false_then_invalid()
    {
        new MustBeTrueAttribute().IsValid(false).Should().BeFalse();
    }

    [Fact]
    public void MustBeTrue_when_null_then_invalid()
    {
        new MustBeTrueAttribute().IsValid(null).Should().BeFalse();
    }

    [Fact]
    public void MustBeTrue_when_non_bool_then_invalid()
    {
        new MustBeTrueAttribute().IsValid("yes").Should().BeFalse();
    }

    // --- NotEmptyListAttribute ---

    [Fact]
    public void NotEmptyList_when_list_has_items_then_valid()
    {
        new NotEmptyListAttribute().IsValid(new[] { 1, 2, 3 }).Should().BeTrue();
    }

    [Fact]
    public void NotEmptyList_when_empty_list_then_invalid()
    {
        new NotEmptyListAttribute().IsValid(Array.Empty<int>()).Should().BeFalse();
    }

    [Fact]
    public void NotEmptyList_when_null_then_valid()
    {
        new NotEmptyListAttribute().IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void NotEmptyList_when_list_type_then_valid()
    {
        new NotEmptyListAttribute().IsValid(new List<string> { "a" }).Should().BeTrue();
    }

    [Fact]
    public void NotEmptyList_when_empty_list_type_then_invalid()
    {
        new NotEmptyListAttribute().IsValid(new List<string>()).Should().BeFalse();
    }

    // --- MaxItemsListAttribute ---

    [Fact]
    public void MaxItemsList_when_within_limit_then_valid()
    {
        new MaxItemsListAttribute(3).IsValid(new[] { 1, 2 }).Should().BeTrue();
    }

    [Fact]
    public void MaxItemsList_when_at_limit_then_valid()
    {
        new MaxItemsListAttribute(3).IsValid(new[] { 1, 2, 3 }).Should().BeTrue();
    }

    [Fact]
    public void MaxItemsList_when_exceeds_limit_then_invalid()
    {
        new MaxItemsListAttribute(2).IsValid(new[] { 1, 2, 3 }).Should().BeFalse();
    }

    [Fact]
    public void MaxItemsList_when_null_then_valid()
    {
        new MaxItemsListAttribute(5).IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void MaxItemsList_formats_error_with_limit()
    {
        new MaxItemsListAttribute(10).FormatErrorMessage("Items")
            .Should().Contain("10");
    }

    // --- InFutureAttribute ---

    [Fact]
    public void InFuture_when_future_date_then_valid()
    {
        new InFutureAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void InFuture_when_past_date_then_invalid()
    {
        new InFutureAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void InFuture_when_null_then_valid()
    {
        new InFutureAttribute().IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void InFuture_when_datetimeoffset_future_then_valid()
    {
        new InFutureAttribute().IsValid(DateTimeOffset.UtcNow.AddHours(1)).Should().BeTrue();
    }

    // --- InPastAttribute ---

    [Fact]
    public void InPast_when_past_date_then_valid()
    {
        new InPastAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void InPast_when_future_date_then_invalid()
    {
        new InPastAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void InPast_when_null_then_valid()
    {
        new InPastAttribute().IsValid(null).Should().BeTrue();
    }

    // --- NotInFutureAttribute ---

    [Fact]
    public void NotInFuture_when_past_date_then_valid()
    {
        new NotInFutureAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void NotInFuture_when_future_date_then_invalid()
    {
        new NotInFutureAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void NotInFuture_when_null_then_valid()
    {
        new NotInFutureAttribute().IsValid(null).Should().BeTrue();
    }

    // --- NotInPastAttribute ---

    [Fact]
    public void NotInPast_when_future_date_then_valid()
    {
        new NotInPastAttribute().IsValid(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void NotInPast_when_past_date_then_invalid()
    {
        new NotInPastAttribute().IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void NotInPast_when_null_then_valid()
    {
        new NotInPastAttribute().IsValid(null).Should().BeTrue();
    }

    // --- GreaterThanOrEqualToAttribute ---

    [Fact]
    public void GreaterThanOrEqualTo_when_above_then_valid()
    {
        new GreaterThanOrEqualToAttribute(5).IsValid(10).Should().BeTrue();
    }

    [Fact]
    public void GreaterThanOrEqualTo_when_equal_then_valid()
    {
        new GreaterThanOrEqualToAttribute(5).IsValid(5).Should().BeTrue();
    }

    [Fact]
    public void GreaterThanOrEqualTo_when_below_then_invalid()
    {
        new GreaterThanOrEqualToAttribute(5).IsValid(4).Should().BeFalse();
    }

    [Fact]
    public void GreaterThanOrEqualTo_when_null_then_valid()
    {
        new GreaterThanOrEqualToAttribute(0).IsValid(null).Should().BeTrue();
    }

    // --- LessThanOrEqualToAttribute ---

    [Fact]
    public void LessThanOrEqualTo_when_below_then_valid()
    {
        new LessThanOrEqualToAttribute(10).IsValid(5).Should().BeTrue();
    }

    [Fact]
    public void LessThanOrEqualTo_when_equal_then_valid()
    {
        new LessThanOrEqualToAttribute(10).IsValid(10).Should().BeTrue();
    }

    [Fact]
    public void LessThanOrEqualTo_when_above_then_invalid()
    {
        new LessThanOrEqualToAttribute(10).IsValid(11).Should().BeFalse();
    }

    [Fact]
    public void LessThanOrEqualTo_when_null_then_valid()
    {
        new LessThanOrEqualToAttribute(10).IsValid(null).Should().BeTrue();
    }

    // --- PhoneNumberAttribute ---

    [Theory]
    [InlineData("+44 1234 567890")]
    [InlineData("(020) 7946-0958")]
    [InlineData("555.123.4567")]
    [InlineData("555-1234 x100")]
    public void PhoneNumber_when_valid_format_then_valid(string phone)
    {
        new PhoneNumberAttribute().IsValid(phone).Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-phone!!!")]
    [InlineData("abc@def.com")]
    public void PhoneNumber_when_invalid_format_then_invalid(string phone)
    {
        new PhoneNumberAttribute().IsValid(phone).Should().BeFalse();
    }

    [Fact]
    public void PhoneNumber_when_null_then_valid()
    {
        new PhoneNumberAttribute().IsValid(null).Should().BeTrue();
    }

    // --- UkPostcodeAttribute ---

    [Theory]
    [InlineData("SW1A 1AA")]
    [InlineData("EC1A 1BB")]
    [InlineData("M1 1AE")]
    [InlineData("B33 8TH")]
    [InlineData("GIR 0AA")]
    public void UkPostcode_when_valid_then_valid(string postcode)
    {
        new UkPostcodeAttribute().IsValid(postcode).Should().BeTrue();
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("12345")]
    public void UkPostcode_when_invalid_then_invalid(string postcode)
    {
        new UkPostcodeAttribute().IsValid(postcode).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UkPostcode_when_null_or_empty_then_valid(string? postcode)
    {
        new UkPostcodeAttribute().IsValid(postcode).Should().BeTrue();
    }

    // --- PasswordAttribute ---

    [Fact]
    public void Password_when_strong_then_valid()
    {
        new PasswordAttribute().IsValid("MyP@ssw0rd!").Should().BeTrue();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoSpecialChar1")]
    [InlineData("NoDigit!Abc")]
    public void Password_when_weak_then_invalid(string password)
    {
        new PasswordAttribute().IsValid(password).Should().BeFalse();
    }

    [Fact]
    public void Password_when_null_then_valid()
    {
        new PasswordAttribute().IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void Password_with_appended_description_includes_rules()
    {
        var attr = new PasswordAttribute("Custom message.", true);
        var formatted = attr.FormatErrorMessage("Password");
        formatted.Should().Contain("Custom message.");
        formatted.Should().Contain("lower case letter");
    }

    // --- ResourceKeyAttribute ---

    [Theory]
    [InlineData("tenant/5C65D124-F856-46A4-BB3F-19764429BA1F")]
    [InlineData("user/a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    public void ResourceKey_when_valid_format_then_valid(string key)
    {
        new ResourceKeyAttribute().IsValid(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("missing-guid/")]
    [InlineData("/no-type")]
    [InlineData("123/5C65D124-F856-46A4-BB3F-19764429BA1F")]
    public void ResourceKey_when_invalid_format_then_invalid(string key)
    {
        new ResourceKeyAttribute().IsValid(key).Should().BeFalse();
    }

    [Fact]
    public void ResourceKey_when_null_then_valid()
    {
        new ResourceKeyAttribute().IsValid(null).Should().BeTrue();
    }

    // --- MustBeGreaterThanOrEqualAttribute (cross-property) ---

    [Fact]
    public void MustBeGreaterThanOrEqual_when_value_exceeds_other_then_valid()
    {
        var model = new DateRangeModel { StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 12, 31) };
        var context = new ValidationContext(model) { MemberName = nameof(DateRangeModel.EndDate) };
        var attr = new MustBeGreaterThanOrEqualAttribute(nameof(DateRangeModel.StartDate));

        var result = attr.GetValidationResult(model.EndDate, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void MustBeGreaterThanOrEqual_when_value_equals_other_then_valid()
    {
        var date = new DateTime(2024, 6, 15);
        var model = new DateRangeModel { StartDate = date, EndDate = date };
        var context = new ValidationContext(model) { MemberName = nameof(DateRangeModel.EndDate) };
        var attr = new MustBeGreaterThanOrEqualAttribute(nameof(DateRangeModel.StartDate));

        var result = attr.GetValidationResult(model.EndDate, context);

        result.Should().Be(ValidationResult.Success);
    }

    [Fact]
    public void MustBeGreaterThanOrEqual_when_value_less_than_other_then_invalid()
    {
        var model = new DateRangeModel { StartDate = new DateTime(2024, 12, 31), EndDate = new DateTime(2024, 1, 1) };
        var context = new ValidationContext(model) { MemberName = nameof(DateRangeModel.EndDate) };
        var attr = new MustBeGreaterThanOrEqualAttribute(nameof(DateRangeModel.StartDate));

        var result = attr.GetValidationResult(model.EndDate, context);

        result.Should().NotBe(ValidationResult.Success);
    }

    private sealed class DateRangeModel
    {
        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }
    }
}
