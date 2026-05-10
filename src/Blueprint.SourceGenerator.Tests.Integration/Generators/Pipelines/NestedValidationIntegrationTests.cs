using System.ComponentModel.DataAnnotations;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the generated pipeline calls RecursiveDataAnnotationValidator for operations
/// containing complex child properties whose types carry validation attributes.
/// </summary>
public class NestedValidationIntegrationTests
{
    [Fact]
    public async Task When_child_object_property_has_invalid_nested_fields_then_pipeline_returns_validation_failure()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NestedChildObjectValidationOperation));
        var context = CreateContext(app, NestedChildObjectValidationOperation.Configure());

        ((NestedChildObjectValidationOperation)context.Operation).Name = "Valid";
        ((NestedChildObjectValidationOperation)context.Operation).Address = new ChildAddress { Street = null!, City = null! };

        NestedChildObjectValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NestedChildObjectValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResults = ((ValidationFailedResult)result).ValidationResults;
        var memberNames = validationResults.SelectMany(static r => r.MemberNames).ToList();
        memberNames.Should().Contain("address.street");
        memberNames.Should().Contain("address.city");
    }

    [Fact]
    public async Task When_child_object_property_is_valid_then_pipeline_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NestedChildObjectValidationOperation));
        var context = CreateContext(app, NestedChildObjectValidationOperation.Configure());

        ((NestedChildObjectValidationOperation)context.Operation).Name = "Valid";
        ((NestedChildObjectValidationOperation)context.Operation).Address = new ChildAddress { Street = "123 Main", City = "Springfield" };

        NestedChildObjectValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NestedChildObjectValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_child_object_property_is_null_then_pipeline_skips_nested_validation_and_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NestedChildObjectValidationOperation));
        var context = CreateContext(app, NestedChildObjectValidationOperation.Configure());

        ((NestedChildObjectValidationOperation)context.Operation).Name = "Valid";
        ((NestedChildObjectValidationOperation)context.Operation).Address = null;

        NestedChildObjectValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NestedChildObjectValidationOperation.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_child_list_property_has_invalid_items_then_pipeline_returns_validation_failure_with_indexed_paths()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NestedChildListValidationOperation));
        var context = CreateContext(app, NestedChildListValidationOperation.Configure());

        ((NestedChildListValidationOperation)context.Operation).Name = "Valid";
        ((NestedChildListValidationOperation)context.Operation).Addresses =
        [
            new ChildAddress { Street = "Valid", City = "Valid" },
            new ChildAddress { Street = null!, City = null! },
        ];

        NestedChildListValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        NestedChildListValidationOperation.HandlerCallCount.Should().Be(0);
        var validationResults = ((ValidationFailedResult)result).ValidationResults;
        var memberNames = validationResults.SelectMany(static r => r.MemberNames).ToList();
        memberNames.Should().Contain("addresses[1].street");
        memberNames.Should().Contain("addresses[1].city");
    }

    [Fact]
    public async Task When_child_list_property_is_null_then_pipeline_skips_nested_validation_and_runs_handler()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(NestedChildListValidationOperation));
        var context = CreateContext(app, NestedChildListValidationOperation.Configure());

        ((NestedChildListValidationOperation)context.Operation).Name = "Valid";
        ((NestedChildListValidationOperation)context.Operation).Addresses = null;

        NestedChildListValidationOperation.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        NestedChildListValidationOperation.HandlerCallCount.Should().Be(1);
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

/// <summary>
/// A child type with its own validation attributes to be used in nested validation tests.
/// </summary>
public sealed class ChildAddress
{
    [Required]
    public string Street { get; set; } = string.Empty;

    [Required]
    public string City { get; set; } = string.Empty;
}

public partial class NestedChildObjectValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? Name { get; set; }

    public ChildAddress? Address { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NestedChildObjectValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class NestedChildListValidationOperation : ICommand<string>
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? Name { get; set; }

    public IReadOnlyList<ChildAddress>? Addresses { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(NestedChildListValidationOperation operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}
