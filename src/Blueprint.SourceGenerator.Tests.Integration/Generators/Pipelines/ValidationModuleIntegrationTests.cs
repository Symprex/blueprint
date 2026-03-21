using System.ComponentModel.DataAnnotations;
using Blueprint.Middleware;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class ValidationModuleIntegrationTests
{
    [Fact]
    public async Task When_required_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredValidationOperation.Name));
    }

    [Fact]
    public async Task When_required_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());
        ((RequiredValidationOperation)context.Operation).Name = "Blueprint";

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("Blueprint");
    }

    [Fact]
    public async Task When_required_validation_fails_for_whitespace_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredValidationOperation));
        var context = CreateContext(app, RequiredValidationOperation.Configure());
        ((RequiredValidationOperation)context.Operation).Name = "   ";

        RequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        RequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredValidationOperation.Name));
    }

    [Fact]
    public async Task When_multi_property_required_validation_fails_then_generated_pipeline_returns_all_failures_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(MultiRequiredValidationOperation));
        var context = CreateContext(app, MultiRequiredValidationOperation.Configure());

        MultiRequiredValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        MultiRequiredValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResults = ((ValidationFailedOperationResult)result).ValidationResults;
        validationResults.Should().HaveCount(2);
        validationResults.SelectMany(static validationResult => validationResult.MemberNames).Should().Contain([nameof(MultiRequiredValidationOperation.Name), nameof(MultiRequiredValidationOperation.Age)]);
    }

    [Fact]
    public async Task When_required_and_max_length_validation_fails_for_whitespace_then_generated_pipeline_returns_required_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "    ";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredMaxLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The Name field is required.");
    }

    [Fact]
    public async Task When_required_and_max_length_validation_fails_for_too_long_value_then_generated_pipeline_returns_max_length_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "abcd";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredMaxLengthValidationOperation.Name));
        validationResult.ErrorMessage.Should().Be("The field Name must be a string or array type with a maximum length of '3'.");
    }

    [Fact]
    public async Task When_required_and_max_length_validation_passes_then_generated_pipeline_runs_handler_and_returns_ok()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext(app, RequiredMaxLengthValidationOperation.Configure());
        ((RequiredMaxLengthValidationOperation)context.Operation).Name = "abc";

        RequiredMaxLengthValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult>();
        RequiredMaxLengthValidationOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be("abc");
    }

    [Fact]
    public async Task When_required_allow_empty_strings_is_used_then_generated_pipeline_allows_empty_string_and_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredAllowEmptyStringsOperation));
        var context = CreateContext(app, RequiredAllowEmptyStringsOperation.Configure());
        ((RequiredAllowEmptyStringsOperation)context.Operation).Name = string.Empty;

        RequiredAllowEmptyStringsOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult>();
        RequiredAllowEmptyStringsOperation.HandlerCallCount.Should().Be(1);
        ((OkResult)result).Unwrap<string>().Should().Be(string.Empty);
    }

    [Fact]
    public async Task When_required_validation_uses_custom_error_message_then_generated_pipeline_preserves_message_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(RequiredCustomMessageOperation));
        var context = CreateContext(app, RequiredCustomMessageOperation.Configure());

        RequiredCustomMessageOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        RequiredCustomMessageOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(RequiredCustomMessageOperation.Name));
        validationResult.ErrorMessage.Should().Be("Please provide a name.");
    }

    [Fact]
    public async Task When_IValidatableObject_validation_fails_then_generated_pipeline_returns_validation_failure_and_skips_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ValidatableOperation));
        var context = CreateContext(app, ValidatableOperation.Configure());

        ValidatableOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedOperationResult>();
        ValidatableOperation.HandlerCallCount.Should().Be(0);
        var validationResult = ((ValidationFailedOperationResult)result).ValidationResults.Should().ContainSingle().Subject;
        validationResult.MemberNames.Should().Contain(nameof(ValidatableOperation.Code));
        validationResult.ErrorMessage.Should().Be("Code is required.");
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor)
    {
        return new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
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
