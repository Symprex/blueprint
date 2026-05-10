using System.ComponentModel.DataAnnotations;
using Blueprint.Http;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
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

    [Fact]
    public void ShouldBeResourceEvent_typed_returns_event_for_correct_type()
    {
        var resource = new TemplateApiResource("t1", "test-template", "<html/>", "text");
        var result = new ResourceEvent<TemplateApiResource>(
            ResourceEventChangeType.Updated,
            "template.updated",
            resource);

        var resourceEvent = ((OperationResult)result).ShouldBeResourceEvent<TemplateApiResource>();
        resourceEvent.Data.Should().BeSameAs(resource);
        resourceEvent.EventId.Should().Be("template.updated");
    }

    [Fact]
    public void ShouldBeResourceEvent_typed_throws_for_validation_failure_with_error_details()
    {
        var result = new ValidationFailedResult(new[]
        {
            new ValidationResult("Name is required", ["Name"]),
        });

        var act = () => ((OperationResult)result).ShouldBeResourceEvent<TemplateApiResource>();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*ResourceEvent<TemplateApiResource>*ValidationFailedResult*Name is required*");
    }

    [Fact]
    public void ShouldBeResourceEvent_typed_throws_for_wrong_result_type()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldBeResourceEvent<TemplateApiResource>();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected ResourceEvent<TemplateApiResource>*OkResult*");
    }

    [Fact]
    public void ShouldBeResourceEvent_untyped_returns_event()
    {
        var resource = new TemplateApiResource("t2", "test-template", "<html/>", "text");
        var result = new ResourceEvent<TemplateApiResource>(
            ResourceEventChangeType.Created,
            "template.created",
            resource);

        var resourceEvent = ((OperationResult)result).ShouldBeResourceEvent();
        resourceEvent.EventId.Should().Be("template.created");
    }

    [Fact]
    public void ShouldBeResourceEvent_untyped_throws_for_ok_result()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldBeResourceEvent();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected ResourceEvent*OkResult*");
    }

    [Fact]
    public void ShouldBeStatusCode_returns_result_for_matching_code()
    {
        OperationResult result = StatusCodeResult.Accepted.Instance;

        var statusResult = result.ShouldBeStatusCode(202);
        statusResult.StatusCode.Should().Be(202);
    }

    [Fact]
    public void ShouldBeStatusCode_throws_for_wrong_status_code()
    {
        OperationResult result = StatusCodeResult.NoContent.Instance;

        var act = () => result.ShouldBeStatusCode(202);

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected status code 202 but got 204*");
    }

    [Fact]
    public void ShouldBeStatusCode_throws_for_non_status_code_result()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldBeStatusCode(200);

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*Expected StatusCodeResult(200)*OkResult*");
    }

    [Fact]
    public void ShouldNotBeSuccessful_passes_for_validation_failure()
    {
        var result = new ValidationFailedResult(Array.Empty<ValidationResult>());
        var returned = ((OperationResult)result).ShouldNotBeSuccessful();
        returned.Should().BeSameAs(result);
    }

    [Fact]
    public void ShouldNotBeSuccessful_throws_for_ok_result()
    {
        var result = new OkResult<string>("hello");

        var act = () => ((OperationResult)result).ShouldNotBeSuccessful();

        act.Should().Throw<BlueprintTestExecutionException>()
            .WithMessage("*WasSuccessful=true*");
    }
}
