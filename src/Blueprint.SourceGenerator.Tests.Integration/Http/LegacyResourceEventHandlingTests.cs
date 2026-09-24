using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a legacy resource event is handled as legacy Blueprint's <c>ResourceEventHandler</c> handled it: a deleted
/// event's self query is not run to load the resource, and an event whose resource type has no self link is logged.
/// </summary>
/// <remarks>
/// Legacy loaded an event's data through its self query only when the event was not a deletion ("cannot, obviously, get
/// for a deleted record"), so a deleted event kept no data and its href came from the self query; and it logged
/// <c>"No self link exists. Href property of the resource event of type {ResourceType} will not be populated"</c> as a
/// warning when the event's resource type had no self link.
/// </remarks>
public sealed class LegacyResourceEventHandlingTests
{
    [Fact]
    public async Task A_deleted_event_keeps_no_data_and_takes_its_href_from_its_self_query()
    {
        await using var host = await KestrelTestHost.StartAsync();

        // The widget still exists, so the self query would load it: legacy did not run it for a deletion.
        using var response = await host.Client.DeleteAsync("api/href-events/legacy/widgets/widget-7");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        using var envelope = JsonDocument.Parse(body);
        Property(envelope.RootElement, "data").ValueKind.Should().Be(JsonValueKind.Null);
        Property(envelope.RootElement, "href").GetString().Should().Be(new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-7").AbsoluteUri);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task An_updated_event_loads_its_data_through_its_self_query()
    {
        await using var host = await KestrelTestHost.StartAsync();

        using var response = await host.Client.PutAsync("api/href-events/legacy/widgets/widget-8", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        using var envelope = JsonDocument.Parse(body);
        var data = Property(envelope.RootElement, "data");
        Property(data, "id").GetString().Should().Be("widget-8");
        Property(data, "name").GetString().Should().Be("fetched", "the self query's handler names the widget it loads so");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_whose_resource_type_has_no_self_link_is_logged_as_a_warning()
    {
        var logger = new RecordingLogger();

        await ExecuteAsync(new ReportHrefUnlinkedChangedCommand(), logger);

        logger.Entries.Should().ContainSingle(static e => e.Level == LogLevel.Warning).Which.Message.Should().Be(
            "No self link exists. Href property of the resource event of type "
            + "Blueprint.SourceGenerator.Tests.Integration.Http.HrefUnlinkedApiResource will not be populated");
    }

    [Fact]
    public async Task An_event_whose_resource_type_has_a_self_link_is_not_logged()
    {
        var logger = new RecordingLogger();

        await ExecuteAsync(new ReportHrefWidgetChangedCommand(), logger);

        logger.Entries.Should().NotContain(static e => e.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(typeof(ReportHrefWidgetChangedCommand))]
    [InlineData(typeof(ReportHrefWidgetDeletedCommand))]
    [InlineData(typeof(ReportHrefUnlinkedChangedCommand))]
    [InlineData(typeof(Generators.Pipelines.SelfQueryDeleteCommand))]
    [InlineData(typeof(ReportHrefWidgetCorrelatedCommand))]
    public async Task Every_event_is_stamped_with_the_request_correlation_id_and_its_operation(Type operationType)
    {
        // Legacy's ResourceEventHandler set both on every event, whatever its change type and whether or not it had data,
        // a self query or a self link; Signature365's audit stores both (ResourceEventCreatedEvent, ApiEvent).
        using var activity = new System.Diagnostics.Activity("resource-event-stamping").Start();
        var operation = Activator.CreateInstance(operationType)!;

        var resourceEvent = await ExecuteAsync(operation, new RecordingLogger(), activity);

        resourceEvent.CorrelationId.Should().Be(activity.Id);
        resourceEvent.Operation.Should().BeSameAs(operation);
    }

    private static async Task<ResourceEvent> ExecuteAsync(object operation, RecordingLogger logger, System.Diagnostics.Activity? activity = null)
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new LoggingServiceProvider(logger);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var descriptor = app.FindOperation(operation.GetType())!;
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            Activity = activity,
        };

        var result = await executor.GetPipeline(operation.GetType()).ExecuteAsync(context);

        return result.Should().BeAssignableTo<ResourceEvent>("the pipeline raised: {0}", (result as UnhandledExceptionResult)?.Exception.ToString()).Subject;
    }

    /// <summary>The named member of <paramref name="element"/>, whatever the serializer's property casing.</summary>
    private static JsonElement Property(JsonElement element, string name)
    {
        return element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private sealed class LoggingServiceProvider(RecordingLogger logger) : IServiceProvider
    {
        public BlueprintAppExecutor? Executor { get; set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(BlueprintAppExecutor))
            {
                return this.Executor;
            }

            if (serviceType == typeof(IEnumerable<IResourceLinkGenerator>))
            {
                return Array.Empty<IResourceLinkGenerator>();
            }

            return serviceType == typeof(ILogger<ResourceEvent>) ? logger : null;
        }
    }

    private sealed class RecordingLogger : ILogger<ResourceEvent>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            this.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Reports a change to a resource no self link is registered for, from outside a request.</summary>
public partial class ReportHrefUnlinkedChangedCommand : ICommand<ResourceUpdated<HrefUnlinkedApiResource>>
{
    public static ResourceUpdated<HrefUnlinkedApiResource> Handle(ReportHrefUnlinkedChangedCommand command)
    {
        return new ResourceUpdated<HrefUnlinkedApiResource>(new HrefUnlinkedApiResource());
    }
}

/// <summary>Reports the deletion of a widget from outside a request, with a legacy deleted event carrying the widget.</summary>
public partial class ReportHrefWidgetDeletedCommand : ICommand<ResourceDeleted<HrefWidgetApiResource>>
{
    public static ResourceDeleted<HrefWidgetApiResource> Handle(ReportHrefWidgetDeletedCommand command)
    {
        return new ResourceDeleted<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-9" });
    }
}

/// <summary>Reports a change to a widget with an event its handler gave a correlation id of its own, which legacy replaced.</summary>
public partial class ReportHrefWidgetCorrelatedCommand : ICommand<ResourceUpdated<HrefWidgetApiResource>>
{
    public static ResourceUpdated<HrefWidgetApiResource> Handle(ReportHrefWidgetCorrelatedCommand command)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-10" }) { CorrelationId = "set-by-handler" };
    }
}