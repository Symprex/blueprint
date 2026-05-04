using System.ComponentModel.DataAnnotations;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that background-task operations execute through the generated pipeline without HTTP involvement,
/// satisfying the Phase 3 exit gate requirement that at least one non-HTTP host works through the shared
/// module/contributor model.
/// </summary>
public class BackgroundTaskHostDispatchIntegrationTests
{
    [Fact]
    public async Task When_task_operation_dispatched_then_handler_executes_through_generated_pipeline()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var taskOperations = app.GetOperationsForHost("tasks");
        var descriptor = taskOperations.First(static op => op.OperationType == typeof(DispatchableBackgroundTask));

        var pipeline = executor.GetPipeline(typeof(DispatchableBackgroundTask));
        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
        ((DispatchableBackgroundTask)context.Operation).Payload = "test-payload";

        DispatchableBackgroundTask.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("processed:test-payload");
        DispatchableBackgroundTask.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_task_operation_has_validation_attributes_and_input_is_invalid_then_validation_short_circuits()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var descriptor = app.GetOperationsForHost("tasks")
            .First(static op => op.OperationType == typeof(ValidatedBackgroundTask));

        var pipeline = executor.GetPipeline(typeof(ValidatedBackgroundTask));
        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);

        ValidatedBackgroundTask.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        ValidatedBackgroundTask.HandlerCallCount.Should().Be(0);

        var failures = ((ValidationFailedResult)result).ValidationResults;
        failures.Should().Contain(static vr => vr.MemberNames.Contains(nameof(ValidatedBackgroundTask.RequiredField)));
    }

    [Fact]
    public async Task When_task_operation_has_validation_attributes_and_input_is_valid_then_handler_executes()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);

        var descriptor = app.GetOperationsForHost("tasks")
            .First(static op => op.OperationType == typeof(ValidatedBackgroundTask));

        var pipeline = executor.GetPipeline(typeof(ValidatedBackgroundTask));
        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
        ((ValidatedBackgroundTask)context.Operation).RequiredField = "valid-input";

        ValidatedBackgroundTask.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("task:valid-input");
        ValidatedBackgroundTask.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public void When_querying_tasks_host_then_http_only_operations_are_not_included()
    {
        var app = new IntegrationTestBlueprintApp();

        var taskOperations = app.GetOperationsForHost("tasks");

        taskOperations.Should().NotContain(static op => op.OperationType == typeof(HttpExposedOperationToScan));
        taskOperations.Should().NotContain(static op => op.OperationType == typeof(InternalOnlyOperationToScan));
    }

    [Fact]
    public void When_querying_http_host_then_task_only_operations_are_not_included()
    {
        var app = new IntegrationTestBlueprintApp();

        var httpOperations = app.GetOperationsForHost("http");

        httpOperations.Should().NotContain(static op => op.OperationType == typeof(DispatchableBackgroundTask));
        httpOperations.Should().NotContain(static op => op.OperationType == typeof(ValidatedBackgroundTask));
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
/// A background task operation that can be dispatched through the generated pipeline to prove non-HTTP execution.
/// </summary>
public partial class DispatchableBackgroundTask : IBackgroundTask
{
    public static int HandlerCallCount { get; private set; }

    public string? Payload { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public OkResult<string> Handle()
    {
        HandlerCallCount++;
        return new OkResult<string>($"processed:{this.Payload}");
    }
}

/// <summary>
/// A background task operation with validation attributes, proving that the validation pipeline stage
/// fires correctly for non-HTTP operations.
/// </summary>
public partial class ValidatedBackgroundTask : IBackgroundTask
{
    public static int HandlerCallCount { get; private set; }

    [Required]
    public string? RequiredField { get; set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public OkResult<string> Handle()
    {
        HandlerCallCount++;
        return new OkResult<string>($"task:{this.RequiredField}");
    }
}
