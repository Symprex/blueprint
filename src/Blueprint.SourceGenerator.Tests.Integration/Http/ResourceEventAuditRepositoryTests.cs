using System.Collections.Concurrent;
using System.Text;
using Blueprint.Auditing;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Expansion;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that the <see cref="IResourceEventAuditRepository"/> registered with <c>AddResourceEventAuditRepository</c> is
/// given every legacy resource event a command raises, as legacy Blueprint gave it, stamped and with its href, and never
/// one raised by a <see cref="DoNotAuditAttribute"/> command.
/// </summary>
/// <remarks>
/// <para>
/// Legacy's <c>ResourceEventHandler.HandleAsync</c> (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/Middleware/ResourceEventHandler.cs</c>) stamped the event with the request's correlation id and the
/// operation (<c>:38-39</c>), made its href from its resource type's self link (<c>:54-65</c>), and then, unless the
/// operation's type carried <c>[DoNotAudit]</c>, handed it to <c>IResourceEventAuditRepository.AddAsync</c>
/// (<c>:72-77</c>). Signature365 registers its <c>BusResourceEventAuditRepository</c> this way in Partners, Subscriptions,
/// EventStore, Import, VCard and ExchangeTransportAgent, and publishes every event it is given to its audit trail.
/// </para>
/// <para>
/// Each test runs the app as a consumer runs it, <c>AddBlueprintApi</c> and <c>MapBlueprintApi</c> on Kestrel, because
/// the href is made only under a request's base URI and the correlation id is the request's activity.
/// </para>
/// </remarks>
public sealed class ResourceEventAuditRepositoryTests
{
    // A W3C trace the client sends, so the request's activity, which stamps the event, is known to belong to this request.
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task A_commands_resource_event_reaches_the_repository_stamped_and_with_its_href()
    {
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        await host.PostAsync("api/audited-events/widgets");

        var record = records.Repository.Should().ContainSingle("the command's one resource event is audited once").Subject;
        record.OperationType.Should().Be(typeof(CreateAuditedWidgetCommand), "legacy stamped the event with the operation that raised it");
        record.CorrelationId.Should().StartWith($"00-{TraceId}-", "legacy stamped the event with the request's activity id");
        record.Href.Should().Be(
            new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-7").AbsoluteUri,
            "legacy made the href from the resource type's self link before auditing the event");
        record.DataId.Should().Be("widget-7");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_reaches_the_repository_with_its_data_expanded()
    {
        // Legacy's ResourceExpandMiddleware ran before its ResourceEventHandlerMiddlewareBuilder (Signature365 8b40ecc961,
        // Portal/.../AzureAdOauthReturnCommandExecutorPipeline.cs:142-149), so AddAsync was given the event expanded.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        await host.PostAsync("api/audited-events/employees");

        records.Repository.Should().ContainSingle().Which.DepartmentName.Should().Be(
            "Engineering",
            "the event's [AlwaysExpand] department is expanded before the event is audited");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_do_not_audit_commands_resource_event_does_not_reach_the_repository()
    {
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        var body = await host.PostAsync("api/audited-events/unaudited-widgets");

        body.Should().Contain("widget-8", "the command ran and returned its event");
        records.Repository.Should().BeEmpty("legacy skipped the repository for an operation whose type carries [DoNotAudit]");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resource_event_audit_sink_and_repository_registered_together_are_both_given_the_event()
    {
        // Registering one must not displace the other: the sink is this framework's own seam, the repository legacy's.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        await host.PostAsync("api/audited-events/widgets");

        records.Repository.Should().ContainSingle().Which.DataId.Should().Be("widget-7");
        records.SinkEventIds.Should().ContainSingle();
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_repository_that_throws_does_not_fail_the_command_whose_changes_it_audits()
    {
        // Not legacy: its handler let AddAsync's exception out (ResourceEventHandler.cs:76), failing, after the save, an
        // operation whose changes were already committed. Signature365's repository swallowed every exception itself
        // (EventStore.Contract/Domain/BusResourceEventAuditRepository.cs:86-101), so nothing it raised ever reached the
        // handler. The repository is called as the sink is, best-effort (IResourceEventAuditSink's remarks).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<ThrowingResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        var body = await host.PostAsync("api/audited-events/widgets");

        body.Should().Contain("widget-7", "the command's result is returned although its audit failed");
        records.SinkEventIds.Should().ContainSingle("the sink is still given the event");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_repository_that_cannot_be_constructed_does_not_fail_the_command_whose_changes_it_audits()
    {
        // As a repository whose AddAsync throws (above): its failure is the audit's, not the saved operation's.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<UnconstructableResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        var body = await host.PostAsync("api/audited-events/widgets");

        body.Should().Contain("widget-7", "the command's result is returned although its audit repository could not be created");
        records.SinkEventIds.Should().ContainSingle("the sink is still given the event");
        host.ServerErrors.Should().BeEmpty();
    }

    /// <summary>What the repository and sink were given, copied out when they were called.</summary>
    public sealed class AuditRecords
    {
        public ConcurrentQueue<AuditRecord> Repository { get; } = new();

        public ConcurrentQueue<string> SinkEventIds { get; } = new();
    }

    /// <summary>A resource event as the repository was given it.</summary>
    /// <param name="OperationType">The type of the stamped operation.</param>
    /// <param name="CorrelationId">The stamped correlation id.</param>
    /// <param name="Href">The event's href.</param>
    /// <param name="DataId">The id of the widget the event carries.</param>
    /// <param name="DepartmentName">The name of the department the employee the event carries belongs to, if expanded.</param>
    public sealed record AuditRecord(Type? OperationType, string? CorrelationId, string? Href, string? DataId, string? DepartmentName);

    /// <summary>A repository, registered scoped as <c>AddResourceEventAuditRepository</c> registers it, that records what it is given.</summary>
    public sealed class RecordingResourceEventAuditRepository(AuditRecords records) : IResourceEventAuditRepository
    {
        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            records.Repository.Enqueue(new AuditRecord(
                resourceEvent.Operation?.GetType(),
                resourceEvent.CorrelationId,
                resourceEvent.Href,
                (resourceEvent.Data as HrefWidgetApiResource)?.Id,
                (resourceEvent.Data as AlwaysExpandTestEmployee)?.Department.Value?.Name));

            return Task.CompletedTask;
        }
    }

    /// <summary>A repository whose every write fails.</summary>
    public sealed class ThrowingResourceEventAuditRepository : IResourceEventAuditRepository
    {
        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            throw new InvalidOperationException("The audit store is unavailable.");
        }
    }

    /// <summary>A repository whose construction fails, as one whose store client cannot be created does.</summary>
    public sealed class UnconstructableResourceEventAuditRepository : IResourceEventAuditRepository
    {
        public UnconstructableResourceEventAuditRepository()
        {
            throw new InvalidOperationException("The audit store's client could not be created.");
        }

        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A sink that records the id of every event it is given.</summary>
    public sealed class RecordingResourceEventAuditSink(AuditRecords records) : IResourceEventAuditSink
    {
        public Task RecordAsync(string eventId, string resourceId, OperationResult handlerResult, ApiOperationContext context, CancellationToken cancellationToken)
        {
            records.SinkEventIds.Enqueue(eventId);

            return Task.CompletedTask;
        }
    }

    /// <summary>The integration app registered and mapped as a consumer registers and maps it, served by Kestrel.</summary>
    private sealed class AuditingHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private AuditingHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<Exception> serverErrors)
        {
            this._app = app;
            this.Client = client;
            this.ServerErrors = serverErrors;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<Exception> ServerErrors { get; }

        public static async Task<AuditingHost> StartAsync(AuditRecords records, Action<BlueprintServiceBuilder> configure)
        {
            var serverErrors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Host.UseDefaultServiceProvider(static options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });

            // Logging enabled at Critical is also what makes ASP.NET Core start the request's activity.
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ErrorRecordingLoggerProvider(serverErrors));
            builder.Services.AddSingleton(records);

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(configure);

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new AuditingHost(app, client, serverErrors);
        }

        public async Task<string> PostAsync(string path)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, path)
            {
                Content = new System.Net.Http.StringContent("{}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");

            using var response = await this.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().BeInRange(200, 201, "the server raised: {0}", string.Join(" | ", this.ServerErrors.Select(static e => e.Message)));

            return body;
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    private sealed class ErrorRecordingLoggerProvider(ConcurrentQueue<Exception> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ErrorRecordingLogger(errors);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ErrorRecordingLogger(ConcurrentQueue<Exception> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                errors.Enqueue(exception ?? new InvalidOperationException(formatter(state, exception)));
            }
        }
    }
}

/// <summary>Creates a widget, reporting it with a legacy created event carrying the widget, which is audited.</summary>
[AllowAnonymous]
[RootLink("/audited-events/widgets")]
[HttpPost]
public partial class CreateAuditedWidgetCommand : ICommand<ResourceCreated<HrefWidgetApiResource>>
{
    public static ResourceCreated<HrefWidgetApiResource> Handle(CreateAuditedWidgetCommand command)
    {
        return new ResourceCreated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-7" });
    }
}

/// <summary>Creates an employee, reporting it with a legacy created event whose data has an <c>[AlwaysExpand]</c> department.</summary>
[AllowAnonymous]
[RootLink("/audited-events/employees")]
[HttpPost]
public partial class CreateAuditedEmployeeCommand : ICommand<ResourceCreated<AlwaysExpandTestEmployee>>
{
    public static ResourceCreated<AlwaysExpandTestEmployee> Handle(CreateAuditedEmployeeCommand command)
    {
        return new ResourceCreated<AlwaysExpandTestEmployee>(
            new AlwaysExpandTestEmployee { Department = new Expandable<DepartmentResource>("dept-audited") });
    }
}

/// <summary>Creates a widget, reporting it with a legacy created event carrying the widget, which is not audited.</summary>
[AllowAnonymous]
[RootLink("/audited-events/unaudited-widgets")]
[HttpPost]
[DoNotAudit]
public partial class CreateUnauditedWidgetCommand : ICommand<ResourceCreated<HrefWidgetApiResource>>
{
    public static ResourceCreated<HrefWidgetApiResource> Handle(CreateUnauditedWidgetCommand command)
    {
        return new ResourceCreated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-8" });
    }
}
