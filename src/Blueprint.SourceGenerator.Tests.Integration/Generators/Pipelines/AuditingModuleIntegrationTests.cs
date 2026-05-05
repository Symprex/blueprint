using System.Diagnostics;
using Blueprint.Auditing;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the <c>AuditingModule</c> correctly invokes <see cref="IAuditSink"/> through
/// the generated pipeline: called on handler success, not called on validation failure,
/// and best-effort (sink exceptions do not break the pipeline).
/// </summary>
public class AuditingModuleIntegrationTests
{
    [Fact]
    public async Task When_handler_succeeds_then_audit_sink_is_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new TestAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();
        auditSink.RecordCallCount.Should().Be(1, "audit sink should be called exactly once after successful handler execution");
        auditSink.LastContext.Should().BeSameAs(context);
        auditSink.LastResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task When_handler_fails_validation_then_audit_sink_is_not_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new TestAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(AuditableCommand));
        var context = new ApiOperationContext(serviceProvider, app, AuditableCommand.Configure(), CancellationToken.None);

        // Leave RequiredField null to trigger validation failure
        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        auditSink.RecordCallCount.Should().Be(0, "audit sink should NOT be called when validation fails");
    }

    [Fact]
    public async Task When_no_audit_sink_registered_then_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new AuditTestServiceProvider(auditSink: null);

        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task When_audit_sink_throws_then_pipeline_still_returns_success()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new ThrowingAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        // Best-effort: even though the sink threw, the pipeline returns the handler result
        result.Should().BeOfType<OkResult<string>>();
    }

    [Fact]
    public async Task When_operation_has_DoNotAudit_then_audit_sink_is_not_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new TestAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(NonAuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, NonAuditableQuery.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
        auditSink.RecordCallCount.Should().Be(0, "audit sink should NOT be called for [DoNotAudit] operations");
    }

    [Fact]
    public async Task When_handler_succeeds_with_activity_then_audit_events_include_operation_type_and_trace_context()
    {
        using var activity = new Activity("audit-correlation-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new TestAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None)
        {
            Activity = activity,
        };

        await pipeline.ExecuteAsync(context);

        // The pipeline should emit blueprint.audit.started and blueprint.audit.succeeded events
        var events = activity.Events.ToList();
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.started",
            "audit started event should be emitted before calling the sink");
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.succeeded",
            "audit succeeded event should be emitted after successful sink call");

        // The started event should include the operation type tag
        var startedEvent = events.First(static e => e.Name == "blueprint.audit.started");
        startedEvent.Tags.Should().Contain(
            tag => tag.Key == "blueprint.operation.type" && (string)tag.Value! == "AuditableQuery",
            "audit started event should include the operation type name");
    }

    [Fact]
    public async Task When_audit_sink_throws_with_activity_then_failed_event_includes_exception_and_operation_type()
    {
        using var activity = new Activity("audit-failure-correlation-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var auditSink = new ThrowingAuditSink();
        var serviceProvider = new AuditTestServiceProvider(auditSink);

        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None)
        {
            Activity = activity,
        };

        await pipeline.ExecuteAsync(context);

        var events = activity.Events.ToList();

        // Should have started event and failed event (no succeeded)
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.started",
            "audit started event should still be emitted even when the sink will fail");
        events.Should().NotContain(
            static e => e.Name == "blueprint.audit.succeeded",
            "audit succeeded event should NOT be emitted when the sink throws");
        events.Should().Contain(
            static e => e.Name == "blueprint.audit.failed",
            "audit failed event should be emitted when the sink throws");

        // The failed event should include exception details and operation type
        var failedEvent = events.First(static e => e.Name == "blueprint.audit.failed");
        failedEvent.Tags.Should().Contain(
            tag => tag.Key == "blueprint.operation.type" && (string)tag.Value! == "AuditableQuery",
            "audit failed event should include the operation type name");
        failedEvent.Tags.Should().Contain(
            tag => tag.Key == "exception.type" && ((string)tag.Value!).Contains("InvalidOperationException"),
            "audit failed event should include the exception type");
        failedEvent.Tags.Should().Contain(
            tag => tag.Key == "exception.message" && (string)tag.Value! == "Simulated audit sink failure",
            "audit failed event should include the exception message");
    }

    [Fact]
    public async Task When_operation_has_DoNotRecordPerformanceMetrics_then_no_activity_is_created()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var serviceProvider = new AuditTestServiceProvider(null);

        var pipeline = executor.GetPipeline(typeof(NoMetricsQuery));
        var context = new ApiOperationContext(serviceProvider, app, NoMetricsQuery.Configure(), CancellationToken.None);

        // Capture the current activity before pipeline execution
        var activityBefore = Activity.Current;

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("no-metrics");

        // The pipeline should NOT have started its own pipeline activity
        // (Activity.Current should either be null or whatever it was before,
        // not a new "NoMetricsQueryPipeline" activity)
        Activity.Current.Should().Be(activityBefore,
            "pipeline should not create a trace activity for [DoNotRecordPerformanceMetrics] operations");
    }

    private sealed class AuditTestServiceProvider : IServiceProvider
    {
        private readonly IAuditSink? _auditSink;

        public AuditTestServiceProvider(IAuditSink? auditSink)
        {
            this._auditSink = auditSink;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAuditSink))
            {
                return this._auditSink;
            }

            return null;
        }
    }
}

/// <summary>
/// Test audit sink that records calls for assertion.
/// </summary>
public sealed class TestAuditSink : IAuditSink
{
    /// <summary>Gets the number of times RecordAsync was called.</summary>
    public int RecordCallCount { get; private set; }

    /// <summary>Gets the last context passed to RecordAsync.</summary>
    public ApiOperationContext? LastContext { get; private set; }

    /// <summary>Gets the last result passed to RecordAsync.</summary>
    public OperationResult? LastResult { get; private set; }

    /// <inheritdoc />
    public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
    {
        this.RecordCallCount++;
        this.LastContext = context;
        this.LastResult = result;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Audit sink that throws to prove best-effort semantics.
/// </summary>
public sealed class ThrowingAuditSink : IAuditSink
{
    /// <inheritdoc />
    public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated audit sink failure");
    }
}

/// <summary>
/// A query operation that participates in auditing.
/// </summary>
public partial class AuditableQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>An auditable result.</returns>
    public static Task<string> HandleAsync(AuditableQuery operation)
    {
        return Task.FromResult("auditable-result");
    }
}

/// <summary>
/// A command operation that participates in auditing but can fail validation.
/// </summary>
public partial class AuditableCommand : ICommand
{
    /// <summary>Gets or sets the required field (triggers validation when null).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? RequiredField { get; set; }

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(AuditableCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A query operation decorated with <c>[DoNotAudit]</c> that should be excluded from audit recording.
/// </summary>
[DoNotAudit]
public partial class NonAuditableQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>A non-audited result.</returns>
    public static Task<string> HandleAsync(NonAuditableQuery operation)
    {
        return Task.FromResult("not-audited");
    }
}

/// <summary>
/// A query operation decorated with <c>[DoNotRecordPerformanceMetrics]</c> that should suppress Activity creation.
/// </summary>
[DoNotRecordPerformanceMetrics]
public partial class NoMetricsQuery : IQuery<string>
{
    /// <summary>Handles the query.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>A result without tracing overhead.</returns>
    public static Task<string> HandleAsync(NoMetricsQuery operation)
    {
        return Task.FromResult("no-metrics");
    }
}
