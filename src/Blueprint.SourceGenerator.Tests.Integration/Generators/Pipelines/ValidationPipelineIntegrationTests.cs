using System.ComponentModel.DataAnnotations;
using Blueprint.Middleware;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class ValidationPipelineIntegrationTests
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
