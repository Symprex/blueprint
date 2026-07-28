using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class ValidationModuleIntegrationTests
{
    [Fact]
    public async Task When_required_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredValidationOperation.Name));
    }

    [Fact]
    public async Task When_required_validation_fails_then_generated_pipeline_marks_activity_ok_and_skips_handler()
    {
        using var activity = new Activity("validation-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());
        context.Activity = activity;

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(0);
        activity.GetTagItem("otel.status_code").Should().Be("OK");
        activity.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task When_handler_throws_legacy_validation_exception_then_generated_pipeline_returns_validation_failure_and_marks_activity_ok()
    {
        using var activity = new Activity("legacy-validation-exception-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LegacyValidationExceptionOperation));
        var context = CreateContext(app, LegacyValidationExceptionOperation.Configure());
        context.Activity = activity;

        LegacyValidationExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        LegacyValidationExceptionOperation.HandlerCallCount.Should().Be(1);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(LegacyValidationExceptionOperation.Name));
        validationResult.ErrorMessage.Should().Be("Legacy validation failure.");
        activity.GetTagItem("otel.status_code").Should().Be("OK");
        activity.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task When_handler_throws_data_annotations_validation_exception_then_generated_pipeline_returns_validation_failure_and_marks_activity_ok()
    {
        using var activity = new Activity("framework-validation-exception-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DataAnnotationsValidationExceptionOperation));
        var context = CreateContext(app, DataAnnotationsValidationExceptionOperation.Configure());
        context.Activity = activity;

        DataAnnotationsValidationExceptionOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        DataAnnotationsValidationExceptionOperation.HandlerCallCount.Should().Be(1);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(DataAnnotationsValidationExceptionOperation.Name));
        validationResult.ErrorMessage.Should().Be("Framework validation failure.");
        activity.GetTagItem("otel.status_code").Should().Be("OK");
        activity.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task When_required_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());
        ((RequiredValidationOperation)context.Operation).Name = "Blueprint";

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("Blueprint");
    }

    [Fact]
    public async Task When_required_validation_fails_for_whitespace_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());
        ((RequiredValidationOperation)context.Operation).Name = "   ";

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredValidationOperation.Name));
    }

    [Fact]
    public async Task When_multi_property_required_validation_fails_then_generated_pipeline_returns_all_failures_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MultiRequiredValidationOperation));
        var context = CreateContext(app, MultiRequiredValidationOperation.Configure());

        MultiRequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        MultiRequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResults = ((ValidationFailedResult)result).ValidationResults;
        validationResults.Should().HaveCount(2);
        validationResults.SelectMany(static validationResult => validationResult.MemberNames).Should().Contain([nameof(MultiRequiredValidationOperation.Name), nameof(MultiRequiredValidationOperation.Age)]);
    }

    [Fact]
    public async Task When_required_and_max_length_validation_fails_for_whitespace_then_generated_pipeline_returns_required_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "    ";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredMaxLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The Name field is required.");
    }

    [Fact]
    public async Task When_required_and_max_length_validation_fails_for_too_long_value_then_generated_pipeline_returns_max_length_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "abcd";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredMaxLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The field Name must be a string or array type with a maximum length of '3'.");
    }

    [Fact]
    public async Task When_required_and_max_length_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "abc";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("abc");
    }

    [Fact]
    public async Task When_required_allow_empty_strings_is_used_then_generated_pipeline_allows_empty_string_and_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredAllowEmptyStringsOperation));
        var context = CreateContext(app, RequiredAllowEmptyStringsOperation.Configure());
        ((RequiredAllowEmptyStringsOperation)context.Operation).Name = string.Empty;

        RequiredAllowEmptyStringsOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredAllowEmptyStringsOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be(string.Empty);
    }

    [Fact]
    public async Task When_required_validation_uses_custom_error_message_then_generated_pipeline_preserves_message_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredCustomMessageOperation));
        var context = CreateContext(app, RequiredCustomMessageOperation.Configure());

        RequiredCustomMessageOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredCustomMessageOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredCustomMessageOperation.Name));
        validationResult.ErrorMessage.Should().Be("Please provide a name.");
    }

    [Fact]
    public void When_descriptor_requests_property_attributes_then_descriptor_returns_declared_property_attributes()
    {
        var descriptor = DisplayNamedRequiredOperation.Configure();

        var propertyAttributes = descriptor.GetPropertyAttributes(nameof(DisplayNamedRequiredOperation.Name));

        propertyAttributes.OfType<DisplayAttribute>().Should().ContainSingle(static attribute => attribute.Name == "Display name");
        propertyAttributes.OfType<RequiredAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void When_validation_runtime_validates_required_and_max_length_property_then_required_short_circuits_other_failures()
    {
        var operation = new RequiredMaxLengthValidationOperation
        {
            Name = "    ",
        };
        var descriptor = RequiredMaxLengthValidationOperation.Configure();
        List<ValidationResult>? validationResults = null;

        ValidationRuntime.ValidateProperty(
            descriptor.GetPropertyAttributes(nameof(RequiredMaxLengthValidationOperation.Name)),
            operation,
            operation.Name,
            nameof(RequiredMaxLengthValidationOperation.Name),
            ref validationResults);

        validationResults.Should().ContainSingle();
        validationResults![0].ErrorMessage.Should().Be("The Name field is required.");
        validationResults[0].MemberNames.Should().Contain(nameof(RequiredMaxLengthValidationOperation.Name));
    }

    [Fact]
    public async Task When_min_length_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MinLengthValidationOperation));
        var context = CreateContext(app, MinLengthValidationOperation.Configure());
        ((MinLengthValidationOperation)context.Operation).Name = "ab";

        MinLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        MinLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(MinLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The field Name must be a string or array type with a minimum length of '3'.");
    }

    [Fact]
    public async Task When_min_length_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MinLengthValidationOperation));
        var context = CreateContext(app, MinLengthValidationOperation.Configure());
        ((MinLengthValidationOperation)context.Operation).Name = "abc";

        MinLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        MinLengthValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("abc");
    }

    [Fact]
    public async Task When_string_length_validation_fails_for_too_short_value_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(StringLengthValidationOperation));
        var context = CreateContext(app, StringLengthValidationOperation.Configure());
        ((StringLengthValidationOperation)context.Operation).Name = "ab";

        StringLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        StringLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(StringLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The field Name must be a string with a minimum length of 3 and a maximum length of 5.");
    }

    [Fact]
    public async Task When_string_length_validation_fails_for_too_long_value_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(StringLengthValidationOperation));
        var context = CreateContext(app, StringLengthValidationOperation.Configure());
        ((StringLengthValidationOperation)context.Operation).Name = "abcdef";

        StringLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        StringLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(StringLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The field Name must be a string with a minimum length of 3 and a maximum length of 5.");
    }

    [Fact]
    public async Task When_string_length_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(StringLengthValidationOperation));
        var context = CreateContext(app, StringLengthValidationOperation.Configure());
        ((StringLengthValidationOperation)context.Operation).Name = "abcd";

        StringLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        StringLengthValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("abcd");
    }

    [Fact]
    public async Task When_email_address_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(EmailAddressValidationOperation));
        var context = CreateContext(app, EmailAddressValidationOperation.Configure());
        ((EmailAddressValidationOperation)context.Operation).Email = "invalid";

        EmailAddressValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        EmailAddressValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(EmailAddressValidationOperation.Email));
        validationResult.ErrorMessage.Should().Be("The Email field is not a valid e-mail address.");
    }

    [Fact]
    public async Task When_email_address_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(EmailAddressValidationOperation));
        var context = CreateContext(app, EmailAddressValidationOperation.Configure());
        ((EmailAddressValidationOperation)context.Operation).Email = "hello@example.com";

        EmailAddressValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        EmailAddressValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("hello@example.com");
    }

    [Fact]
    public async Task When_greater_than_property_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GreaterThanPropertyValidationOperation));
        var context = CreateContext(app, GreaterThanPropertyValidationOperation.Configure());
        var operation = (GreaterThanPropertyValidationOperation)context.Operation;
        operation.Minimum = 10m;
        operation.Maximum = 10m;

        GreaterThanPropertyValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        GreaterThanPropertyValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(GreaterThanPropertyValidationOperation.Maximum));
    }

    [Fact]
    public async Task When_greater_than_property_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GreaterThanPropertyValidationOperation));
        var context = CreateContext(app, GreaterThanPropertyValidationOperation.Configure());
        var operation = (GreaterThanPropertyValidationOperation)context.Operation;
        operation.Minimum = 10m;
        operation.Maximum = 11m;

        GreaterThanPropertyValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        GreaterThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("10:11");
    }

    [Fact]
    public async Task When_greater_than_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GreaterThanValidationOperation));
        var context = CreateContext(app, GreaterThanValidationOperation.Configure());
        var operation = (GreaterThanValidationOperation)context.Operation;
        operation.Count = 10;

        GreaterThanValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        GreaterThanValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(GreaterThanValidationOperation.Count));
        validationResult.ErrorMessage.Should().Be("The field Count must be greater than '10'.");
    }

    [Fact]
    public async Task When_greater_than_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(GreaterThanValidationOperation));
        var context = CreateContext(app, GreaterThanValidationOperation.Configure());
        var operation = (GreaterThanValidationOperation)context.Operation;
        operation.Count = 11;

        GreaterThanValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        GreaterThanValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("11");
    }

    [Fact]
    public async Task When_less_than_property_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LessThanPropertyValidationOperation));
        var context = CreateContext(app, LessThanPropertyValidationOperation.Configure());
        var operation = (LessThanPropertyValidationOperation)context.Operation;
        operation.Start = 10m;
        operation.End = 10m;

        LessThanPropertyValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        LessThanPropertyValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(LessThanPropertyValidationOperation.Start));
    }

    [Fact]
    public async Task When_less_than_property_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LessThanPropertyValidationOperation));
        var context = CreateContext(app, LessThanPropertyValidationOperation.Configure());
        var operation = (LessThanPropertyValidationOperation)context.Operation;
        operation.Start = 9m;
        operation.End = 10m;

        LessThanPropertyValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        LessThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("9:10");
    }

    [Fact]
    public async Task When_less_than_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LessThanValidationOperation));
        var context = CreateContext(app, LessThanValidationOperation.Configure());
        var operation = (LessThanValidationOperation)context.Operation;
        operation.Count = 100;

        LessThanValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        LessThanValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(LessThanValidationOperation.Count));
        validationResult.ErrorMessage.Should().Be("The field Count must be less than '100'.");
    }

    [Fact]
    public async Task When_less_than_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LessThanValidationOperation));
        var context = CreateContext(app, LessThanValidationOperation.Configure());
        var operation = (LessThanValidationOperation)context.Operation;
        operation.Count = 99;

        LessThanValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        LessThanValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("99");
    }

    [Fact]
    public async Task When_compare_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(CompareValidationOperation));
        var context = CreateContext(app, CompareValidationOperation.Configure());
        var operation = (CompareValidationOperation)context.Operation;
        operation.Password = "secret";
        operation.ConfirmPassword = "different";

        CompareValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        CompareValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(CompareValidationOperation.ConfirmPassword));
    }

    [Fact]
    public async Task When_compare_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(CompareValidationOperation));
        var context = CreateContext(app, CompareValidationOperation.Configure());
        var operation = (CompareValidationOperation)context.Operation;
        operation.Password = "secret";
        operation.ConfirmPassword = "secret";

        CompareValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        CompareValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_compare_validation_both_null_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(CompareValidationOperation));
        var context = CreateContext(app, CompareValidationOperation.Configure());

        CompareValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        CompareValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_required_if_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredIfValidationOperation));
        var context = CreateContext(app, RequiredIfValidationOperation.Configure());
        var operation = (RequiredIfValidationOperation)context.Operation;
        operation.Enabled = true;

        RequiredIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredIfValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredIfValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The Name field is required if Enabled field is True.");
    }

    [Fact]
    public async Task When_required_if_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredIfValidationOperation));
        var context = CreateContext(app, RequiredIfValidationOperation.Configure());
        var operation = (RequiredIfValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Name = "Blueprint";

        RequiredIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredIfValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("Blueprint");
    }

    [Fact]
    public async Task When_required_if_not_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredIfNotValidationOperation));
        var context = CreateContext(app, RequiredIfNotValidationOperation.Configure());
        var operation = (RequiredIfNotValidationOperation)context.Operation;
        operation.TokenId = "token";

        RequiredIfNotValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredIfNotValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredIfNotValidationOperation.PaymentIntentId));
        validationResult.ErrorMessage.Should().Be("The PaymentIntentId field is required if TokenId field is not null.");
    }

    [Fact]
    public async Task When_required_if_not_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredIfNotValidationOperation));
        var context = CreateContext(app, RequiredIfNotValidationOperation.Configure());

        RequiredIfNotValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredIfNotValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be(string.Empty);
    }

    [Fact]
    public async Task When_required_items_if_validation_fails_on_empty_collection_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfValidationOperation));
        var context = CreateContext(app, RequiredItemsIfValidationOperation.Configure());
        var operation = (RequiredItemsIfValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Roles = new System.Collections.Generic.List<string>();

        RequiredItemsIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredItemsIfValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredItemsIfValidationOperation.Roles));
        validationResult.ErrorMessage.Should().Be("The Roles field must contain at least one item if Enabled field is True.");
    }

    [Fact]
    public async Task When_required_items_if_validation_fails_on_null_collection_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfValidationOperation));
        var context = CreateContext(app, RequiredItemsIfValidationOperation.Configure());
        var operation = (RequiredItemsIfValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Roles = null;

        RequiredItemsIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RequiredItemsIfValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_required_items_if_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfValidationOperation));
        var context = CreateContext(app, RequiredItemsIfValidationOperation.Configure());
        var operation = (RequiredItemsIfValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Roles = new System.Collections.Generic.List<string> { "admin" };

        RequiredItemsIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredItemsIfValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("admin");
    }

    [Fact]
    public async Task When_required_items_if_condition_inactive_then_empty_collection_passes()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RequiredItemsIfValidationOperation));
        var context = CreateContext(app, RequiredItemsIfValidationOperation.Configure());
        var operation = (RequiredItemsIfValidationOperation)context.Operation;
        operation.Enabled = false;
        operation.Roles = new System.Collections.Generic.List<string>();

        RequiredItemsIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RequiredItemsIfValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_not_allowed_if_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NotAllowedIfValidationOperation));
        var context = CreateContext(app, NotAllowedIfValidationOperation.Configure());
        var operation = (NotAllowedIfValidationOperation)context.Operation;
        operation.Enabled = true;
        operation.Name = "Blueprint";

        NotAllowedIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NotAllowedIfValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(NotAllowedIfValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("This field is not allowed to be set if Enabled field is True.");
    }

    [Fact]
    public async Task When_not_allowed_if_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NotAllowedIfValidationOperation));
        var context = CreateContext(app, NotAllowedIfValidationOperation.Configure());
        var operation = (NotAllowedIfValidationOperation)context.Operation;
        operation.Enabled = true;

        NotAllowedIfValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NotAllowedIfValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be(string.Empty);
    }

    [Fact]
    public async Task When_not_allowed_if_validation_uses_null_dependent_value_then_runtime_fallback_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NotAllowedIfNullDependentValueValidationOperation));
        var context = CreateContext(app, NotAllowedIfNullDependentValueValidationOperation.Configure());
        var operation = (NotAllowedIfNullDependentValueValidationOperation)context.Operation;
        operation.Name = "Blueprint";

        NotAllowedIfNullDependentValueValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NotAllowedIfNullDependentValueValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(NotAllowedIfNullDependentValueValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("This field is not allowed to be set if TokenId field is null.");
    }

    [Fact]
    public async Task When_mixed_fast_and_runtime_property_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MixedFastAndRuntimeValidationOperation));
        var context = CreateContext(app, MixedFastAndRuntimeValidationOperation.Configure());
        var operation = (MixedFastAndRuntimeValidationOperation)context.Operation;
        operation.Name = "abc";

        MixedFastAndRuntimeValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        MixedFastAndRuntimeValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(MixedFastAndRuntimeValidationOperation.Code));
        validationResult.ErrorMessage.Should().Be("Please provide a code.");
    }

    [Fact]
    public async Task When_mixed_fast_and_runtime_property_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(MixedFastAndRuntimeValidationOperation));
        var context = CreateContext(app, MixedFastAndRuntimeValidationOperation.Configure());
        var operation = (MixedFastAndRuntimeValidationOperation)context.Operation;
        operation.Name = "abc";
        operation.Code = "token";

        MixedFastAndRuntimeValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        MixedFastAndRuntimeValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("abc:token");
    }

    [Fact]
    public async Task When_IValidatableObject_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(ValidatableOperation));
        var context = CreateContext(app, ValidatableOperation.Configure());

        ValidatableOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        ValidatableOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(ValidatableOperation.Code));
        validationResult.ErrorMessage.Should().Be("Code is required.");
    }

    [Fact]
    public async Task When_range_validation_fails_below_minimum_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RangeValidationOperation));
        var context = CreateContext(app, RangeValidationOperation.Configure());

        RangeValidationOperation.Reset();
        ((RangeValidationOperation)context.Operation).Priority = 0;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RangeValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RangeValidationOperation.Priority));
    }

    [Fact]
    public async Task When_range_validation_fails_above_maximum_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RangeValidationOperation));
        var context = CreateContext(app, RangeValidationOperation.Configure());

        RangeValidationOperation.Reset();
        ((RangeValidationOperation)context.Operation).Priority = 101;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RangeValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RangeValidationOperation.Priority));
    }

    [Fact]
    public async Task When_range_validation_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RangeValidationOperation));
        var context = CreateContext(app, RangeValidationOperation.Configure());

        RangeValidationOperation.Reset();
        ((RangeValidationOperation)context.Operation).Priority = 50;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RangeValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_double_range_validation_fails_below_minimum_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DoubleRangeValidationOperation));
        var context = CreateContext(app, DoubleRangeValidationOperation.Configure());

        DoubleRangeValidationOperation.Reset();
        ((DoubleRangeValidationOperation)context.Operation).Score = 0.1;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        DoubleRangeValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(DoubleRangeValidationOperation.Score));
    }

    [Fact]
    public async Task When_double_range_validation_fails_above_maximum_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DoubleRangeValidationOperation));
        var context = CreateContext(app, DoubleRangeValidationOperation.Configure());

        DoubleRangeValidationOperation.Reset();
        ((DoubleRangeValidationOperation)context.Operation).Score = 100.0;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        DoubleRangeValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(DoubleRangeValidationOperation.Score));
    }

    [Fact]
    public async Task When_double_range_validation_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DoubleRangeValidationOperation));
        var context = CreateContext(app, DoubleRangeValidationOperation.Configure());

        DoubleRangeValidationOperation.Reset();
        ((DoubleRangeValidationOperation)context.Operation).Score = 50.0;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        DoubleRangeValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_range_validation_value_is_null_then_generated_pipeline_skips_validation_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableRangeValidationOperation));
        var context = CreateContext(app, NullableRangeValidationOperation.Configure());

        NullableRangeValidationOperation.Reset();
        ((NullableRangeValidationOperation)context.Operation).Priority = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableRangeValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_range_validation_value_is_out_of_range_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableRangeValidationOperation));
        var context = CreateContext(app, NullableRangeValidationOperation.Configure());

        NullableRangeValidationOperation.Reset();
        ((NullableRangeValidationOperation)context.Operation).Priority = 0;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NullableRangeValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_nullable_range_validation_value_is_in_range_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableRangeValidationOperation));
        var context = CreateContext(app, NullableRangeValidationOperation.Configure());

        NullableRangeValidationOperation.Reset();
        ((NullableRangeValidationOperation)context.Operation).Priority = 50;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableRangeValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_greater_than_validation_value_is_null_then_generated_pipeline_skips_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableGreaterThanValidationOperation));
        var context = CreateContext(app, NullableGreaterThanValidationOperation.Configure());

        NullableGreaterThanValidationOperation.Reset();
        ((NullableGreaterThanValidationOperation)context.Operation).Score = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableGreaterThanValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_greater_than_validation_value_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableGreaterThanValidationOperation));
        var context = CreateContext(app, NullableGreaterThanValidationOperation.Configure());

        NullableGreaterThanValidationOperation.Reset();
        ((NullableGreaterThanValidationOperation)context.Operation).Score = 0;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NullableGreaterThanValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_nullable_less_than_validation_value_is_null_then_generated_pipeline_skips_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableLessThanValidationOperation));
        var context = CreateContext(app, NullableLessThanValidationOperation.Configure());

        NullableLessThanValidationOperation.Reset();
        ((NullableLessThanValidationOperation)context.Operation).Limit = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableLessThanValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_less_than_validation_value_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableLessThanValidationOperation));
        var context = CreateContext(app, NullableLessThanValidationOperation.Configure());

        NullableLessThanValidationOperation.Reset();
        ((NullableLessThanValidationOperation)context.Operation).Limit = 1000;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NullableLessThanValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_nullable_greater_than_property_both_null_then_generated_pipeline_skips_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableGreaterThanPropertyValidationOperation));
        var context = CreateContext(app, NullableGreaterThanPropertyValidationOperation.Configure());

        NullableGreaterThanPropertyValidationOperation.Reset();
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Minimum = null;
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Maximum = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableGreaterThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_greater_than_property_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableGreaterThanPropertyValidationOperation));
        var context = CreateContext(app, NullableGreaterThanPropertyValidationOperation.Configure());

        NullableGreaterThanPropertyValidationOperation.Reset();
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Minimum = 10;
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Maximum = 5;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NullableGreaterThanPropertyValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_nullable_greater_than_property_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableGreaterThanPropertyValidationOperation));
        var context = CreateContext(app, NullableGreaterThanPropertyValidationOperation.Configure());

        NullableGreaterThanPropertyValidationOperation.Reset();
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Minimum = 5;
        ((NullableGreaterThanPropertyValidationOperation)context.Operation).Maximum = 10;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableGreaterThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_less_than_property_both_null_then_generated_pipeline_skips_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableLessThanPropertyValidationOperation));
        var context = CreateContext(app, NullableLessThanPropertyValidationOperation.Configure());

        NullableLessThanPropertyValidationOperation.Reset();
        ((NullableLessThanPropertyValidationOperation)context.Operation).Start = null;
        ((NullableLessThanPropertyValidationOperation)context.Operation).End = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableLessThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_nullable_less_than_property_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableLessThanPropertyValidationOperation));
        var context = CreateContext(app, NullableLessThanPropertyValidationOperation.Configure());

        NullableLessThanPropertyValidationOperation.Reset();
        ((NullableLessThanPropertyValidationOperation)context.Operation).Start = 20;
        ((NullableLessThanPropertyValidationOperation)context.Operation).End = 10;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NullableLessThanPropertyValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_nullable_less_than_property_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NullableLessThanPropertyValidationOperation));
        var context = CreateContext(app, NullableLessThanPropertyValidationOperation.Configure());

        NullableLessThanPropertyValidationOperation.Reset();
        ((NullableLessThanPropertyValidationOperation)context.Operation).Start = 5;
        ((NullableLessThanPropertyValidationOperation)context.Operation).End = 10;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NullableLessThanPropertyValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_regex_validation_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RegularExpressionValidationOperation));
        var context = CreateContext(app, RegularExpressionValidationOperation.Configure());

        RegularExpressionValidationOperation.Reset();
        ((RegularExpressionValidationOperation)context.Operation).Code = "lowercase";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        RegularExpressionValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RegularExpressionValidationOperation.Code));
    }

    [Fact]
    public async Task When_regex_validation_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RegularExpressionValidationOperation));
        var context = CreateContext(app, RegularExpressionValidationOperation.Configure());

        RegularExpressionValidationOperation.Reset();
        ((RegularExpressionValidationOperation)context.Operation).Code = "ABC";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RegularExpressionValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_regex_validation_property_is_null_then_skips_regex_check_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RegularExpressionValidationOperation));
        var context = CreateContext(app, RegularExpressionValidationOperation.Configure());

        RegularExpressionValidationOperation.Reset();
        ((RegularExpressionValidationOperation)context.Operation).Code = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        RegularExpressionValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_phone_validation_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(PhoneValidationOperation));
        var context = CreateContext(app, PhoneValidationOperation.Configure());

        PhoneValidationOperation.Reset();
        ((PhoneValidationOperation)context.Operation).PhoneNumber = "not-a-phone";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        PhoneValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(PhoneValidationOperation.PhoneNumber));
    }

    [Fact]
    public async Task When_phone_validation_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(PhoneValidationOperation));
        var context = CreateContext(app, PhoneValidationOperation.Configure());

        PhoneValidationOperation.Reset();
        ((PhoneValidationOperation)context.Operation).PhoneNumber = "+1-555-123-4567";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        PhoneValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_phone_validation_property_is_null_then_skips_phone_check_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(PhoneValidationOperation));
        var context = CreateContext(app, PhoneValidationOperation.Configure());

        PhoneValidationOperation.Reset();
        ((PhoneValidationOperation)context.Operation).PhoneNumber = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        PhoneValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_url_validation_fails_then_generated_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(UrlValidationOperation));
        var context = CreateContext(app, UrlValidationOperation.Configure());

        UrlValidationOperation.Reset();
        ((UrlValidationOperation)context.Operation).Website = "not-a-url";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        UrlValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(UrlValidationOperation.Website));
    }

    [Fact]
    public async Task When_url_validation_passes_then_generated_pipeline_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(UrlValidationOperation));
        var context = CreateContext(app, UrlValidationOperation.Configure());

        UrlValidationOperation.Reset();
        ((UrlValidationOperation)context.Operation).Website = "https://example.com";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        UrlValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_url_validation_property_is_null_then_skips_url_check_and_executes_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(UrlValidationOperation));
        var context = CreateContext(app, UrlValidationOperation.Configure());

        UrlValidationOperation.Reset();
        ((UrlValidationOperation)context.Operation).Website = null;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        UrlValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_allowed_values_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AllowedValuesValidationOperation));
        var context = CreateContext(app, AllowedValuesValidationOperation.Configure());
        var operation = (AllowedValuesValidationOperation)context.Operation;
        operation.Role = "superadmin";

        AllowedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        AllowedValuesValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_allowed_values_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AllowedValuesValidationOperation));
        var context = CreateContext(app, AllowedValuesValidationOperation.Configure());
        var operation = (AllowedValuesValidationOperation)context.Operation;
        operation.Role = "admin";

        AllowedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        AllowedValuesValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_allowed_values_property_is_null_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AllowedValuesValidationOperation));
        var context = CreateContext(app, AllowedValuesValidationOperation.Configure());

        AllowedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        AllowedValuesValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_denied_values_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DeniedValuesValidationOperation));
        var context = CreateContext(app, DeniedValuesValidationOperation.Configure());
        var operation = (DeniedValuesValidationOperation)context.Operation;
        operation.Username = "root";

        DeniedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        DeniedValuesValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_denied_values_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DeniedValuesValidationOperation));
        var context = CreateContext(app, DeniedValuesValidationOperation.Configure());
        var operation = (DeniedValuesValidationOperation)context.Operation;
        operation.Username = "jsmith";

        DeniedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        DeniedValuesValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_denied_values_property_is_null_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(DeniedValuesValidationOperation));
        var context = CreateContext(app, DeniedValuesValidationOperation.Configure());

        DeniedValuesValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        DeniedValuesValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_length_validation_too_short_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LengthValidationOperation));
        var context = CreateContext(app, LengthValidationOperation.Configure());
        var operation = (LengthValidationOperation)context.Operation;
        operation.Code = "A";

        LengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        LengthValidationOperation.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_length_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(LengthValidationOperation));
        var context = CreateContext(app, LengthValidationOperation.Configure());
        var operation = (LengthValidationOperation)context.Operation;
        operation.Code = "ABC";

        LengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        LengthValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_IValidatableObject_with_property_attributes_and_property_valid_but_ivalidatable_fails_then_returns_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(HybridValidatableOperation));
        var context = CreateContext(app, HybridValidatableOperation.Configure());
        var operation = (HybridValidatableOperation)context.Operation;

        // Property-level checks pass, but IValidatableObject.Validate() fails
        operation.Name = "valid-name";
        operation.TriggerIValidatableFailure = true;

        HybridValidatableOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        HybridValidatableOperation.HandlerCallCount.Should().Be(0,
            "handler should not run when IValidatableObject.Validate() returns errors");
        var failures = ((ValidationFailedResult)result).ValidationResults;
        failures.Should().ContainSingle().Which.ErrorMessage.Should().Contain("custom cross-property check");
    }

    [Fact]
    public async Task When_IValidatableObject_with_property_attributes_and_property_fails_then_returns_property_failure_without_calling_ivalidatable()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(HybridValidatableOperation));
        var context = CreateContext(app, HybridValidatableOperation.Configure());
        var operation = (HybridValidatableOperation)context.Operation;

        // Property-level [Required] fails — IValidatableObject.Validate() should still run since
        // generated code runs property checks then IValidatableObject tail call sequentially
        operation.Name = null;
        operation.TriggerIValidatableFailure = false;

        HybridValidatableOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        HybridValidatableOperation.HandlerCallCount.Should().Be(0);
        var failures = ((ValidationFailedResult)result).ValidationResults;
        failures.Should().ContainSingle().Which.MemberNames.Should().Contain(nameof(HybridValidatableOperation.Name));
    }

    [Fact]
    public async Task When_IValidatableObject_with_property_attributes_and_all_valid_then_handler_executes()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(HybridValidatableOperation));
        var context = CreateContext(app, HybridValidatableOperation.Configure());
        var operation = (HybridValidatableOperation)context.Operation;

        operation.Name = "valid-name";
        operation.TriggerIValidatableFailure = false;

        HybridValidatableOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        HybridValidatableOperation.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
    }

    [Fact]
    public async Task When_additional_blueprint_validation_fast_paths_fail_then_generated_pipeline_returns_all_validation_failures_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AdditionalBlueprintValidationFastPathOperation));
        var context = CreateContext(app, AdditionalBlueprintValidationFastPathOperation.Configure());
        var operation = (AdditionalBlueprintValidationFastPathOperation)context.Operation;
        operation.AcceptedTerms = false;
        operation.StartsAt = DateTime.UtcNow.AddDays(-1);
        operation.CompletedAt = DateTimeOffset.UtcNow.AddDays(1);
        operation.PublishedAt = DateTime.UtcNow.AddDays(1);
        operation.ScheduledAt = DateTimeOffset.UtcNow.AddDays(-1);
        operation.ItemIds = [];
        operation.ScoreIds = [1, 2, 3];
        operation.MinimumAge = 4;
        operation.MaximumAge = 11;
        operation.MinimumValue = 10;
        operation.MaximumValue = 9;
        operation.Password = "weak";
        operation.ResourceKey = "invalid";
        operation.Postcode = "invalid";

        AdditionalBlueprintValidationFastPathOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        AdditionalBlueprintValidationFastPathOperation.HandlerCallCount.Should().Be(0);
        var memberNames = ((ValidationFailedResult)result).ValidationResults
            .SelectMany(static validationResult => validationResult.MemberNames)
            .ToArray();
        memberNames.Should().Contain(
        [
            nameof(AdditionalBlueprintValidationFastPathOperation.AcceptedTerms),
            nameof(AdditionalBlueprintValidationFastPathOperation.StartsAt),
            nameof(AdditionalBlueprintValidationFastPathOperation.CompletedAt),
            nameof(AdditionalBlueprintValidationFastPathOperation.PublishedAt),
            nameof(AdditionalBlueprintValidationFastPathOperation.ScheduledAt),
            nameof(AdditionalBlueprintValidationFastPathOperation.ItemIds),
            nameof(AdditionalBlueprintValidationFastPathOperation.ScoreIds),
            nameof(AdditionalBlueprintValidationFastPathOperation.MinimumAge),
            nameof(AdditionalBlueprintValidationFastPathOperation.MaximumAge),
            nameof(AdditionalBlueprintValidationFastPathOperation.MaximumValue),
            nameof(AdditionalBlueprintValidationFastPathOperation.Password),
            nameof(AdditionalBlueprintValidationFastPathOperation.ResourceKey),
            nameof(AdditionalBlueprintValidationFastPathOperation.Postcode),
        ]);
    }

    [Fact]
    public async Task When_additional_blueprint_validation_fast_paths_pass_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(AdditionalBlueprintValidationFastPathOperation));
        var context = CreateContext(app, AdditionalBlueprintValidationFastPathOperation.Configure());
        var operation = (AdditionalBlueprintValidationFastPathOperation)context.Operation;
        operation.AcceptedTerms = true;
        operation.StartsAt = DateTime.UtcNow.AddDays(1);
        operation.CompletedAt = DateTimeOffset.UtcNow.AddDays(-1);
        operation.PublishedAt = DateTime.UtcNow.AddDays(-1);
        operation.ScheduledAt = DateTimeOffset.UtcNow.AddDays(1);
        operation.ItemIds = [1];
        operation.ScoreIds = [1, 2];
        operation.MinimumAge = 5;
        operation.MaximumAge = 10;
        operation.MinimumValue = 10;
        operation.MaximumValue = 10;
        operation.Password = "StrongP@ss1";
        operation.ResourceKey = "Widget/5C65D124-F856-46A4-BB3F-19764429BA1F";
        operation.Postcode = "SW1A 1AA";

        AdditionalBlueprintValidationFastPathOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        AdditionalBlueprintValidationFastPathOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("ok");
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

public partial class RequiredValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class ValidatableOperation : ICommand<string>, IValidatableObject
{
    public static int HandlerCallCount { get; private set; }

    public string? Code { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(this.Code))
        {
            yield return new ValidationResult("Code is required.", [nameof(this.Code)]);
        }
    }

    public static Task<string> HandleAsync(ValidatableOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Code ?? string.Empty);
    }
}

public partial class MultiRequiredValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? Name { get; set; }

    [Required]
    public int? Age { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(MultiRequiredValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Name}:{operation.Age}");
    }
}

public partial class RequiredMaxLengthValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    [MaxLength(3)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredMaxLengthValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class RequiredAllowEmptyStringsOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required(AllowEmptyStrings = true)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredAllowEmptyStringsOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class RequiredCustomMessageOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required(ErrorMessage = "Please provide a name.")]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredCustomMessageOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class DisplayNamedRequiredOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [Required]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(DisplayNamedRequiredOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class MinLengthValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    [MinLength(3)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(MinLengthValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class StringLengthValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [StringLength(5, MinimumLength = 3)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(StringLengthValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class EmailAddressValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(EmailAddressValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Email ?? string.Empty);
    }
}

public partial class GreaterThanPropertyValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public decimal Minimum { get; set; }

    [GreaterThanProperty(nameof(Minimum))]
    public decimal Maximum { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(GreaterThanPropertyValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
    }
}

public partial class GreaterThanValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [GreaterThan(10)]
    public int Count { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(GreaterThanValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Count.ToString());
    }
}

public partial class RequiredIfValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public bool Enabled { get; set; }

    [RequiredIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredIfValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class RequiredItemsIfValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public bool Enabled { get; set; }

    [RequiredItemsIf(nameof(Enabled), true)]
    public System.Collections.Generic.List<string>? Roles { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredItemsIfValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Roles is null ? string.Empty : string.Join(",", operation.Roles));
    }
}

public partial class RequiredIfNotValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? TokenId { get; set; }

    [RequiredIfNot(nameof(TokenId), null)]
    public string? PaymentIntentId { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RequiredIfNotValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.PaymentIntentId ?? string.Empty);
    }
}

public partial class NotAllowedIfValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public bool Enabled { get; set; }

    [NotAllowedIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NotAllowedIfValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class NotAllowedIfNullDependentValueValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? TokenId { get; set; }

    [NotAllowedIf(nameof(TokenId), null)]
    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NotAllowedIfNullDependentValueValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class LegacyValidationExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(LegacyValidationExceptionOperation operation)
    {
        HandlerCallCount++;
        throw new global::Blueprint.Validation.ValidationException(
        [
            new ValidationResult("Legacy validation failure.", [nameof(Name)]),
        ]);
    }
}

public partial class DataAnnotationsValidationExceptionOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? Name { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(DataAnnotationsValidationExceptionOperation operation)
    {
        HandlerCallCount++;
        throw new global::System.ComponentModel.DataAnnotations.ValidationException(
            new ValidationResult("Framework validation failure.", [nameof(Name)]),
            null,
            operation.Name);
    }
}

public partial class MixedFastAndRuntimeValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    [MaxLength(3)]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Please provide a code.")]
    public string? Code { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(MixedFastAndRuntimeValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Name}:{operation.Code}");
    }
}

public partial class RangeValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Range(1, 100)]
    public int Priority { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RangeValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Priority.ToString());
    }
}

public partial class DoubleRangeValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Range(0.5, 99.9)]
    public double Score { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(DoubleRangeValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Score.ToString());
    }
}

public partial class NullableRangeValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Range(1, 100)]
    public int? Priority { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NullableRangeValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Priority?.ToString() ?? "null");
    }
}

public partial class NullableGreaterThanValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [GreaterThan(0)]
    public int? Score { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NullableGreaterThanValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Score?.ToString() ?? "null");
    }
}

public partial class NullableLessThanValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [LessThan(1000)]
    public int? Limit { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NullableLessThanValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Limit?.ToString() ?? "null");
    }
}

public partial class NullableGreaterThanPropertyValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public int? Minimum { get; set; }

    [GreaterThanProperty(nameof(Minimum))]
    public int? Maximum { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NullableGreaterThanPropertyValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
    }
}

public partial class NullableLessThanPropertyValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [LessThanProperty(nameof(End))]
    public int? Start { get; set; }

    public int? End { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NullableLessThanPropertyValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Start}:{operation.End}");
    }
}

public partial class RegularExpressionValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [RegularExpression(@"^[A-Z]{2,5}$")]
    public string? Code { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(RegularExpressionValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Code ?? string.Empty);
    }
}

public partial class PhoneValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(PhoneValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.PhoneNumber ?? string.Empty);
    }
}

public partial class UrlValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [System.ComponentModel.DataAnnotations.Url]
    public string? Website { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(UrlValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Website ?? string.Empty);
    }
}

public partial class LessThanPropertyValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [LessThanProperty(nameof(End))]
    public decimal Start { get; set; }

    public decimal End { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(LessThanPropertyValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"{operation.Start}:{operation.End}");
    }
}

public partial class LessThanValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [LessThan(100)]
    public int Count { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(LessThanValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Count.ToString());
    }
}

public partial class CompareValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    public string? Password { get; set; }

    [Compare(nameof(Password))]
    public string? ConfirmPassword { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(CompareValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult("ok");
    }
}

public partial class AllowedValuesValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [AllowedValues("admin", "user", "guest")]
    public string? Role { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(AllowedValuesValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Role ?? string.Empty);
    }
}

public partial class DeniedValuesValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [DeniedValues("root", "system")]
    public string? Username { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(DeniedValuesValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Username ?? string.Empty);
    }
}

public partial class LengthValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Length(2, 50)]
    public string? Code { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(LengthValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Code ?? string.Empty);
    }
}

public partial class AdditionalBlueprintValidationFastPathOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [MustBeTrue]
    public bool AcceptedTerms { get; set; }

    [InFuture]
    public DateTime? StartsAt { get; set; }

    [InPast]
    public DateTimeOffset? CompletedAt { get; set; }

    [NotInFuture]
    public DateTime PublishedAt { get; set; }

    [NotInPast]
    public DateTimeOffset ScheduledAt { get; set; }

    [NotEmptyList]
    public int[]? ItemIds { get; set; }

    [MaxItemsList(2)]
    public int[]? ScoreIds { get; set; }

    [GreaterThanOrEqualTo(5)]
    public int? MinimumAge { get; set; }

    [LessThanOrEqualTo(10)]
    public int? MaximumAge { get; set; }

    public int? MinimumValue { get; set; }

    [MustBeGreaterThanOrEqual(nameof(MinimumValue))]
    public int? MaximumValue { get; set; }

    [Password]
    public string? Password { get; set; }

    [ResourceKey]
    public string? ResourceKey { get; set; }

    [UkPostcode]
    public string? Postcode { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(AdditionalBlueprintValidationFastPathOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult("ok");
    }
}

/// <summary>
/// Exercises the hybrid validation path: generated property-level checks plus IValidatableObject.Validate() tail call.
/// </summary>
public partial class HybridValidatableOperation : ICommand<string>, IValidatableObject
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a value that triggers an IValidatableObject failure when true, simulating cross-property logic.
    /// </summary>
    public bool TriggerIValidatableFailure { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.TriggerIValidatableFailure)
        {
            yield return new ValidationResult("Failed custom cross-property check.", [nameof(this.Name)]);
        }
    }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(HybridValidatableOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

