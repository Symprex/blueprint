using System.ComponentModel.DataAnnotations;
using Blueprint.Runtime.Exec;
using FluentAssertions;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that the <see cref="ResultAssertionExtensions"/> provide useful test assertions
/// for common operation result types.
/// </summary>
public class ResultAssertionExtensionsTests
{
    [Fact]
    public void ShouldBeOk_returns_content_for_ok_result()
    {
        var result = new OkResult<string>("hello");
        var content = result.ShouldBeOk<string>();
        content.Should().Be("hello");
    }

    [Fact]
    public void ShouldBeOk_throws_for_validation_failure()
    {
        var result = new ValidationFailedResult(new[]
        {
            new ValidationResult("Name is required", ["Name"]),
        });

        var act = () => ((OperationResult)result).ShouldBeOk<string>();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*ValidationFailedResult*");
    }

    [Fact]
    public void ShouldBeValidationFailed_returns_validation_result()
    {
        var result = new ValidationFailedResult(new[]
        {
            new ValidationResult("Invalid email", ["Email"]),
        });

        var validationResult = ((OperationResult)result).ShouldBeValidationFailed();
        validationResult.ValidationResults.Should().HaveCount(1);
    }

    [Fact]
    public void ShouldBeValidationFailed_throws_for_ok_result()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldBeValidationFailed();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected ValidationFailedResult*OkResult*");
    }

    [Fact]
    public void ShouldBeValidationFailedFor_checks_specific_property()
    {
        var result = new ValidationFailedResult(new[]
        {
            new ValidationResult("Name is required", ["Name"]),
            new ValidationResult("Invalid email", ["Email"]),
        });

        var validationResult = ((OperationResult)result).ShouldBeValidationFailedFor("Name");
        validationResult.Should().NotBeNull();
    }

    [Fact]
    public void ShouldBeValidationFailedFor_throws_when_property_missing()
    {
        var result = new ValidationFailedResult(new[]
        {
            new ValidationResult("Name is required", ["Name"]),
        });

        var act = () => ((OperationResult)result).ShouldBeValidationFailedFor("Email");

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected validation error for property 'Email'*Name*");
    }

    [Fact]
    public void ShouldBeSuccessful_passes_for_ok_result()
    {
        var result = new OkResult<string>("hello");
        var returned = ((OperationResult)result).ShouldBeSuccessful();
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public void ShouldBeSuccessful_throws_for_validation_failure()
    {
        var result = new ValidationFailedResult(Array.Empty<ValidationResult>());

        var act = () => ((OperationResult)result).ShouldBeSuccessful();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*WasSuccessful=false*");
    }

    [Fact]
    public void ShouldBeResultType_returns_typed_result()
    {
        var result = new PlainTextResult("ok");
        var typed = ((OperationResult)result).ShouldBeResultType<PlainTextResult>();
        typed.Content.Should().Be("ok");
    }

    [Fact]
    public void ShouldBeResultType_throws_for_wrong_type()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldBeResultType<PlainTextResult>();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected PlainTextResult*OkResult*");
    }
}
