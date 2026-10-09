using System.Diagnostics;
using Blueprint.Auditing;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the <c>ResourceEventAuditModule</c> correctly invokes <see cref="IResourceEventAuditSink"/>
/// through the generated pipeline: called when handler returns a ResourceEventResult, not called for
/// non-resource-event operations, and best-effort (sink exceptions do not break the pipeline).
/// </summary>
public class ResourceEventAuditModuleIntegrationTests
{
    [Fact]
    public async Task When_handler_returns_resource_event_result_then_audit_sink_is_called_with_event_and_resource_ids()
    {
        var app = new IntegrationTestBlueprintApp();
        var auditSink = new TestResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(ResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, ResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<ResourceEventAuditTestResource>>();
        auditSink.RecordCallCount.Should().Be(1, "resource event audit sink should be called exactly once");
        auditSink.LastEventId.Should().Be("test-resource.created");
        auditSink.LastResourceId.Should().Be("res-42");
        auditSink.LastContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task When_no_resource_event_audit_sink_registered_then_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var serviceProvider = new ResourceEventAuditTestServiceProvider(resourceEventAuditSink: null);

        var pipeline = executor.GetPipeline(typeof(ResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, ResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<ResourceEventAuditTestResource>>();
    }

    [Fact]
    public async Task When_resource_event_audit_sink_throws_then_pipeline_still_returns_success()
    {
        var app = new IntegrationTestBlueprintApp();
        var auditSink = new ThrowingResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(ResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, ResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEventResult<ResourceEventAuditTestResource>>();
    }

    [Fact]
    public async Task When_handler_returns_resource_event_with_activity_then_audit_events_include_event_id_and_resource_id()
    {
        using var activity = new Activity("resource-event-audit-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var auditSink = new TestResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(ResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, ResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
            Activity = activity,
        };

        await pipeline.ExecuteAsync(context);

        var events = activity.Events.ToList();
        events.Should().Contain(
            static e => e.Name == "blueprint.resource_event_audit.started",
            "resource event audit started event should be emitted");
        events.Should().Contain(
            static e => e.Name == "blueprint.resource_event_audit.succeeded",
            "resource event audit succeeded event should be emitted");

        var startedEvent = events.First(static e => e.Name == "blueprint.resource_event_audit.started");
        startedEvent.Tags.Should().Contain(
            tag => tag.Key == "blueprint.resource_event.id" && (string)tag.Value! == "test-resource.created",
            "started event should include the event ID");
        startedEvent.Tags.Should().Contain(
            tag => tag.Key == "blueprint.resource_event.resource_id" && (string)tag.Value! == "res-42",
            "started event should include the resource ID");
    }

    [Fact]
    public async Task When_operation_does_not_return_resource_event_then_audit_sink_is_not_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var auditSink = new TestResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        // AuditableQuery returns a plain string, not a ResourceEventResult
        var pipeline = executor.GetPipeline(typeof(AuditableQuery));
        var context = new ApiOperationContext(serviceProvider, app, AuditableQuery.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        await pipeline.ExecuteAsync(context);

        auditSink.RecordCallCount.Should().Be(0, "resource event audit sink should NOT be called for non-resource-event operations");
    }

    [Fact]
    public async Task When_handler_returns_key_only_resource_event_result_then_audit_sink_is_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var auditSink = new TestResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(KeyOnlyResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, KeyOnlyResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<KeyOnlyResourceEventResult>();
        auditSink.RecordCallCount.Should().Be(1, "resource event audit sink should be called for key-only resource events");
        auditSink.LastEventId.Should().Be("test-resource.deleted");
        auditSink.LastResourceId.Should().Be("res-99");
    }

    [Fact]
    public async Task When_handler_returns_legacy_ResourceCreated_then_audit_sink_is_called()
    {
        var app = new IntegrationTestBlueprintApp();
        var auditSink = new TestResourceEventAuditSink();
        var serviceProvider = new ResourceEventAuditTestServiceProvider(auditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(LegacyResourceEventAuditTestCommand));
        var context = new ApiOperationContext(serviceProvider, app, LegacyResourceEventAuditTestCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceCreated<LegacyResourceEventTestApiResource>>();
        auditSink.RecordCallCount.Should().Be(1, "resource event audit sink should be called for legacy ResourceCreated results");
    }

    private sealed class ResourceEventAuditTestServiceProvider : IServiceProvider
    {
        private readonly IResourceEventAuditSink? _resourceEventAuditSink;

        public ResourceEventAuditTestServiceProvider(IResourceEventAuditSink? resourceEventAuditSink)
        {
            this._resourceEventAuditSink = resourceEventAuditSink;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IResourceEventAuditSink))
            {
                return this._resourceEventAuditSink;
            }

            return null;
        }
    }
}

/// <summary>
/// Test resource event audit sink that records calls for assertion.
/// </summary>
public sealed class TestResourceEventAuditSink : IResourceEventAuditSink
{
    /// <summary>Gets the number of times RecordAsync was called.</summary>
    public int RecordCallCount { get; private set; }

    /// <summary>Gets the last event ID passed to RecordAsync.</summary>
    public string? LastEventId { get; private set; }

    /// <summary>Gets the last resource ID passed to RecordAsync.</summary>
    public string? LastResourceId { get; private set; }

    /// <summary>Gets the last context passed to RecordAsync.</summary>
    public ApiOperationContext? LastContext { get; private set; }

    /// <inheritdoc />
    public Task RecordAsync(string eventId, string resourceId, OperationResult handlerResult, ApiOperationContext context, CancellationToken cancellationToken)
    {
        this.RecordCallCount++;
        this.LastEventId = eventId;
        this.LastResourceId = resourceId;
        this.LastContext = context;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Resource event audit sink that throws to prove best-effort semantics.
/// </summary>
public sealed class ThrowingResourceEventAuditSink : IResourceEventAuditSink
{
    /// <inheritdoc />
    public Task RecordAsync(string eventId, string resourceId, OperationResult handlerResult, ApiOperationContext context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated resource event audit sink failure");
    }
}

/// <summary>
/// A simple resource type for testing resource-event audit.
/// </summary>
public class ResourceEventAuditTestResource
{
    /// <summary>Gets or sets the resource identifier.</summary>
    public string Id { get; set; } = "res-42";

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = "test";
}

/// <summary>
/// A command that returns a ResourceEventResult to test resource-event audit module activation.
/// </summary>
public partial class ResourceEventAuditTestCommand : ICommand<ResourceEventResult<ResourceEventAuditTestResource>>
{
    /// <summary>Handles the command by returning a resource event result.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A resource event result with stable event and resource IDs.</returns>
    public static Task<ResourceEventResult<ResourceEventAuditTestResource>> HandleAsync(ResourceEventAuditTestCommand operation)
    {
        var resource = new ResourceEventAuditTestResource();
        return Task.FromResult(new ResourceEventResult<ResourceEventAuditTestResource>(ChangeType.Created, "test-resource.created", "res-42", resource));
    }
}

/// <summary>
/// A command that returns a KeyOnlyResourceEventResult to test compile-time type discrimination.
/// </summary>
public partial class KeyOnlyResourceEventAuditTestCommand : ICommand<KeyOnlyResourceEventResult>
{
    /// <summary>Handles the command by returning a key-only resource event result.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A key-only resource event result with event and resource IDs.</returns>
    public static Task<KeyOnlyResourceEventResult> HandleAsync(KeyOnlyResourceEventAuditTestCommand operation)
    {
        return Task.FromResult(new KeyOnlyResourceEventResult(ChangeType.Deleted, "test-resource.deleted", "res-99"));
    }
}

/// <summary>
/// An API resource used by the legacy ResourceEvent test operation.
/// </summary>
public class LegacyResourceEventTestApiResource : ApiResource
{
    /// <inheritdoc />
    public override string Object => "legacy-test-resource";

    /// <summary>Gets or sets the resource identifier.</summary>
    public string Id { get; set; } = "legacy-42";
}

/// <summary>
/// A command that returns the legacy <see cref="ResourceCreated{TResource}"/> (from Blueprint.Http.ResourceEvent hierarchy)
/// to prove the audit module activates for S365-style resource event handlers.
/// </summary>
public partial class LegacyResourceEventAuditTestCommand : ICommand<ResourceCreated<LegacyResourceEventTestApiResource>>
{
    /// <summary>Handles the command by returning a legacy ResourceCreated result.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A ResourceCreated result matching the S365 handler pattern.</returns>
    public static Task<ResourceCreated<LegacyResourceEventTestApiResource>> HandleAsync(LegacyResourceEventAuditTestCommand operation)
    {
        var resource = new LegacyResourceEventTestApiResource { Id = "legacy-42" };
        return Task.FromResult(new ResourceCreated<LegacyResourceEventTestApiResource>(resource));
    }
}
