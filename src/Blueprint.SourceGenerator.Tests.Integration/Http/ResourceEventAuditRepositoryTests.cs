using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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
    public async Task A_repository_that_throws_is_logged_as_an_error_by_the_pipeline_and_does_not_fail_the_command_whose_changes_it_audits()
    {
        // Not legacy: its handler let AddAsync's exception out (ResourceEventHandler.cs:76), failing, after the save, an
        // operation whose changes were already committed. Signature365's repository swallowed every exception itself
        // (EventStore.Contract/Domain/BusResourceEventAuditRepository.cs:86-101), so nothing it raised ever reached the
        // handler. The repository is called as the sink is, best-effort (IResourceEventAuditSink's remarks).
        // As legacy, though, the failure is logged at Error by the operation's pipeline logger: legacy's escaped into the
        // generated pipeline's catch, which logged it through its _logger, category "{Operation}ExecutorPipeline", as
        // _UnhandledException (Signature365 8b40ecc961, AdminPortalCloseTenantCommandExecutorPipeline.cs:23-25, :163-177).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<ThrowingResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        var body = await host.PostAsync("api/audited-events/widgets");

        body.Should().Contain("widget-7", "the command's result is returned although its audit failed");
        records.SinkEventIds.Should().ContainSingle("the sink is still given the event");
        var error = host.ServerErrors.Should().ContainSingle("the audit's failure is logged once").Subject;
        error.Category.Should().Be("CreateAuditedWidgetCommandExecutorPipeline", "legacy logged it through the operation's pipeline logger");
        error.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("The audit store is unavailable.");
    }

    [Fact]
    public async Task A_repository_that_throws_a_client_error_ApiException_is_logged_at_information_as_legacy_logged_a_non_5xx_one()
    {
        // Legacy's pipeline catch logged an escaped ApiException whose HttpStatus was below 500 as _NonException: Information,
        // EventId(4, "Non500Exception"), "A non-5xx ApiException has occurred with message {ExceptionMessage}", and only any
        // other exception as the Error above (Signature365 8b40ecc961, AdminPortalCloseTenantCommandExecutorPipeline.cs:15,
        // :165-176).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<ClientErrorResourceEventAuditRepository>());

        var body = await host.PostAsync("api/audited-events/widgets");

        body.Should().Contain("widget-7");
        host.ServerErrors.Should().BeEmpty("a non-5xx ApiException was not an error to legacy");
        var entry = host.Logged.Should().ContainSingle(static e => e.Category == "CreateAuditedWidgetCommandExecutorPipeline").Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Should().Be(new EventId(4, "Non500Exception"));
        entry.Message.Should().Be("A non-5xx ApiException has occurred with message The event was already recorded.");
        entry.Exception.Should().BeOfType<ApiException>().Which.HttpStatus.Should().Be(409);
    }

    [Fact]
    public async Task A_repository_that_throws_a_server_error_ApiException_is_logged_as_an_error_as_legacy_logged_it()
    {
        // The other branch of the same catch (AdminPortalCloseTenantCommandExecutorPipeline.cs:16, :170-176).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<ServerErrorResourceEventAuditRepository>());

        await host.PostAsync("api/audited-events/widgets");

        var entry = host.Logged.Should().ContainSingle(static e => e.Category == "CreateAuditedWidgetCommandExecutorPipeline").Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.EventId.Should().Be(new EventId(5, "UnhandledException"));
        entry.Exception.Should().BeOfType<ApiException>().Which.HttpStatus.Should().Be(503);
    }

    [Fact]
    public async Task A_repository_that_cannot_be_constructed_is_logged_as_an_error_by_the_pipeline_and_does_not_fail_the_command_whose_changes_it_audits()
    {
        // As a repository whose AddAsync throws (above): its failure is the audit's, not the saved operation's. Legacy
        // resolved the repository in the pipeline's try (GetRequiredService, AdminPortalCloseTenantCommandExecutorPipeline
        // .cs:141), so its construction's failure was logged by the pipeline's catch too.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<UnconstructableResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        var body = await host.PostAsync("api/audited-events/widgets");

        body.Should().Contain("widget-7", "the command's result is returned although its audit repository could not be created");
        records.SinkEventIds.Should().ContainSingle("the sink is still given the event");
        var error = host.ServerErrors.Should().ContainSingle("the audit's failure is logged once").Subject;
        error.Category.Should().Be("CreateAuditedWidgetCommandExecutorPipeline", "legacy logged it through the operation's pipeline logger");
        error.Exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("The audit store's client could not be created.");
    }

    [Fact]
    public async Task A_querys_resource_event_is_neither_stamped_nor_given_an_href_nor_audited()
    {
        // Legacy's ResourceEventHandlerMiddlewareBuilder matched commands only (Signature365 8b40ecc961,
        // Blueprint.Http/Middleware/ResourceEventHandlerMiddlewareBuilder.cs:12-15, IsCommand being
        // typeof(ICommand).IsAssignableFrom(OperationType), Blueprint/ApiOperationDescriptor.cs:124). So a query's event
        // was never stamped (ResourceEventHandler.cs:38-39), given an href (:54-65) or audited (:72-77): AdminPortal's
        // AdminPortalDisableInjectionOverrideCommand is an IQuery<ResourceEvent<...>>, and its generated pipeline has a
        // LinkGeneratorMiddlewareBuilder step and no ResourceEventHandlerMiddlewareBuilder one
        // (AdminPortalDisableInjectionOverrideCommandExecutorPipeline.cs:114-115). It passes on the event Subscriptions
        // returned, with Subscriptions' href and correlation id.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint
            .AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>()
            .AddResourceEventAuditSink<RecordingResourceEventAuditSink>());

        using var body = JsonDocument.Parse(await host.GetAsync("api/audited-events/passed-on-widgets"));

        records.Repository.Should().BeEmpty("legacy handed only a command's resource event to the repository");
        records.SinkEventIds.Should().BeEmpty("a query's resource event is not audited at all");
        body.RootElement.GetProperty("href").GetString().Should().Be(
            PassOnAuditedWidgetQuery.UpstreamHref,
            "legacy made no href for a query's event, so the one its handler returned is written");
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(
            PassOnAuditedWidgetQuery.UpstreamCorrelationId,
            "legacy did not stamp a query's event with the request's correlation id");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_carries_legacys_metadata_in_legacys_order_to_the_repository_and_the_wire()
    {
        // Legacy's ResourceEventHandler.HandleAsync (Signature365 8b40ecc961, Blueprint.Http/Middleware/
        // ResourceEventHandler.cs) filled the event's Metadata, by indexer (:34), from:
        // - the user's authorisation context (:36), as Signature365UserAuthorisationContext.PopulateMetadata adds Id then
        //   TenantId (Common.Core/Authorisation/Signature365UserAuthorisationContext.cs:207-219);
        // - then every IContextMetadataProvider (:41-46), the first of which is HttpContextMetadataProvider, registered by
        //   AddHttp (Blueprint.Http/BlueprintApiBuilderExtensions.cs:80): IpAddress always, UserAgent only when the request
        //   has one (HttpContextMetadataProvider.cs:18-25);
        // - then any provider the app registered.
        // Keys the handler added itself came first, as they were already in the dictionary, and a key the handler had
        // added that the pipeline added again kept its place and took the pipeline's value, the indexer replacing it. The
        // IpAddress is the first public address the request was forwarded for (HttpRequestExtensions.cs:12, :37-44).
        // Signature365 stores the dictionary as JSON with the audited event (EventStore.Contract/Messages/EventMapper.cs:29),
        // in this order.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint =>
        {
            blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>();
            blueprint.AddContextMetadataProvider<AppContextMetadataProvider>();
            blueprint.Services.AddSingleton<Blueprint.Authorization.IUserAuthorisationContextFactory, MetadataUserContextFactory>();
        });

        using var body = JsonDocument.Parse(await host.PostAsync(
            "api/audited-events/annotated-widgets",
            (AuditingHost.TestUserHeader, "user-1"),
            ("User-Agent", "audit-test/1.0"),
            ("X-Forwarded-For", "10.0.0.5, 203.0.113.9")));

        (string Key, string? Value)[] expected =
        [
            ("reason", "created-in-test"),
            ("IpAddress", "203.0.113.9"),
            ("Id", "user-1"),
            ("TenantId", "tenant-1"),
            ("UserAgent", "audit-test/1.0"),
            ("app.key", "app-value"),
        ];

        records.Repository.Should().ContainSingle().Which.Metadata.Should().Equal(expected, "legacy filled metadata in this order before auditing");
        body.RootElement.GetProperty("metadata").EnumerateObject()
            .Select(static p => (p.Name, (string?)p.Value.ToString()))
            .Should().Equal(expected, "legacy wrote the event it had filled, metadata and all");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task An_anonymous_commands_resource_event_carries_only_the_requests_ip_address_as_metadata()
    {
        // Legacy loaded no user context for an anonymous caller, so ResourceEventHandler.cs:36's
        // context.UserAuthorisationContext?.PopulateMetadata added nothing. HttpContextMetadataProvider added IpAddress
        // always and UserAgent only when the request carried one (HttpContextMetadataProvider.cs:18-25); this one does not.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        await host.PostAsync("api/audited-events/widgets");

        records.Repository.Should().ContainSingle().Which.Metadata.Should().Equal(
            [("IpAddress", "127.0.0.1")],
            "an anonymous request with no User-Agent gave legacy's metadata its IpAddress alone");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_do_not_audit_commands_resource_event_still_carries_legacys_metadata()
    {
        // Legacy filled the metadata (ResourceEventHandler.cs:34-46) before it looked for [DoNotAudit] (:72), so an event
        // that was not audited was still written with it.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        using var body = JsonDocument.Parse(await host.PostAsync("api/audited-events/unaudited-widgets"));

        records.Repository.Should().BeEmpty();
        body.RootElement.GetProperty("metadata").GetProperty("IpAddress").GetString().Should().Be("127.0.0.1");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_is_audited_before_its_data_is_linked()
    {
        // Legacy's generated pipeline ran ResourceEventHandler, which audited the event (ResourceEventHandler.cs:72-77),
        // before LinkGeneratorHandler.AddLinksAsync linked its data, and returned it only after both (Signature365
        // 8b40ecc961, AdminPortalCloseTenantCommandExecutorPipeline.cs:140-149: the audit at :141-143, the links at :146,
        // the return at :149). So the data Signature365 stored had only the links its handler gave it, none here, and the
        // response, written after, carried them all.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        using var body = JsonDocument.Parse(await host.PostAsync("api/audited-events/widgets"));

        records.Repository.Should().ContainSingle().Which.DataLinkRels.Should().BeEmpty("legacy audited the event before it linked the event's data");
        body.RootElement.GetProperty("data").GetProperty("$links").GetProperty("self").GetProperty("href").GetString().Should().Be(
            new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-7").AbsoluteUri,
            "legacy linked the data after auditing it, before writing it");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_whose_data_its_self_query_loaded_is_audited_with_the_links_that_query_gave_it()
    {
        // Legacy loaded the data through the self query's nested pipeline (ResourceEventHandler.cs:49-52, :113-125), which
        // linked its own result (LinkGeneratorMiddlewareBuilder.cs:14, SupportsNestedExecution), before the outer event
        // was audited. So data a self query loaded was audited with its links, each made as every link was: the request's
        // base URI and the link's filled URL (EntityOperationResourceLinkGenerator.cs:86, :104-107; ApiLinkGenerator.cs:
        // 92-110). Signature365 stores the audited data, links and all (EventMapper.cs:30).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        await host.PutAsync("api/audited-events/widgets/widget-11");

        var record = records.Repository.Should().ContainSingle().Subject;
        record.DataLinkRels.Should().Equal(
            ["self"],
            "the self query's own pipeline linked the widget it loaded before the event was audited");
        record.DataLinkHrefs["self"].Should().Be(
            new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-11").AbsoluteUri,
            "the self query's pipeline made the link under this request's base URI");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_whose_data_its_self_query_loaded_is_audited_with_no_links_when_its_app_disables_them()
    {
        // In an app that never called AddHateoasLinks() (BlueprintApiBuilderExtensions.cs:101-111) no pipeline had a link
        // step, the self query's nested one included (EventStore's generated pipelines have none), so the data was audited
        // with no links, "$links":{} (EventStore.Tests/Handlers/ResourceEventCreatedEventHandlerTests.cs:70). The href was
        // made from the resource type's self link whatever the app (ResourceEventHandler.cs:54-65).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(
            records,
            static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>().DisableHateoasLinks());

        using var body = JsonDocument.Parse(await host.PutAsync("api/audited-events/widgets/widget-11"));

        var record = records.Repository.Should().ContainSingle().Subject;
        record.DataLinkRels.Should().BeEmpty("the self query's own pipeline has no link step in an app that does not link");
        record.Href.Should().Be(new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-11").AbsoluteUri);
        body.RootElement.GetProperty("data").GetProperty("$links").EnumerateObject().Should().BeEmpty();
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_that_is_a_member_of_its_results_union_is_resolved_stamped_given_its_href_and_audited()
    {
        // Legacy's ResourceEventHandler matched the result its command returned, an OkResult whose content was a
        // ResourceEvent (Signature365 8b40ecc961, ResourceEventHandler.cs:26-30), not the command's declared type, so an
        // event was loaded through its self query (:49-52), stamped (:38-39), given its href (:54-65) and audited (:72-77)
        // whatever the command declared. A Results<,> is only declared: the handler returns one of its members.
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        await host.PutAsync("api/audited-events/union-widgets/widget-12");

        var record = records.Repository.Should().ContainSingle("the union's resource event is audited as any command's is").Subject;
        record.OperationType.Should().Be(typeof(UpdateAuditedWidgetOrNotFoundCommand));
        record.CorrelationId.Should().StartWith($"00-{TraceId}-");
        record.Href.Should().Be(new Uri(host.Client.BaseAddress!, "api/href-events/widgets/widget-12").AbsoluteUri);
        record.DataId.Should().Be("widget-12", "the event's self query loaded its data");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_querys_resource_event_is_given_no_metadata()
    {
        // Legacy's ResourceEventHandler, which filled the metadata, never ran for a query
        // (ResourceEventHandlerMiddlewareBuilder.cs:12-15), and an event's metadata began empty (ResourceEvent.cs:140).
        var records = new AuditRecords();
        await using var host = await AuditingHost.StartAsync(records, static blueprint => blueprint.AddResourceEventAuditRepository<RecordingResourceEventAuditRepository>());

        using var body = JsonDocument.Parse(await host.GetAsync("api/audited-events/passed-on-widgets"));

        var metadata = body.RootElement.GetProperty("metadata");
        metadata.ValueKind.Should().Be(JsonValueKind.Object);
        metadata.EnumerateObject().Should().BeEmpty();
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
    /// <param name="Metadata">The event's metadata, in its dictionary's order, each value as its string.</param>
    /// <param name="DataLinkRels">The rels of the links the event's data carried when it was audited.</param>
    /// <param name="DataLinkHrefs">The hrefs of those links, by rel.</param>
    public sealed record AuditRecord(
        Type? OperationType,
        string? CorrelationId,
        string? Href,
        string? DataId,
        string? DepartmentName,
        IReadOnlyList<(string Key, string? Value)> Metadata,
        IReadOnlyList<string> DataLinkRels,
        IReadOnlyDictionary<string, string?> DataLinkHrefs);

    /// <summary>A repository, registered scoped as <c>AddResourceEventAuditRepository</c> registers it, that records what it is given.</summary>
    public sealed class RecordingResourceEventAuditRepository(AuditRecords records) : IResourceEventAuditRepository
    {
        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            var links = (resourceEvent.Data as ILinkableResource)?.Links;

            records.Repository.Enqueue(new AuditRecord(
                resourceEvent.Operation?.GetType(),
                resourceEvent.CorrelationId,
                resourceEvent.Href,
                (resourceEvent.Data as HrefWidgetApiResource)?.Id,
                (resourceEvent.Data as AlwaysExpandTestEmployee)?.Department.Value?.Name,
                resourceEvent.Metadata.Select(static m => (m.Key, m.Value?.ToString())).ToArray(),
                links?.Keys.ToArray() ?? [],
                links?.ToDictionary(static l => l.Key, static l => (string?)l.Value.Href) ?? []));

            return Task.CompletedTask;
        }
    }

    /// <summary>A provider the app registers, whose metadata legacy added after the request's own.</summary>
    public sealed class AppContextMetadataProvider : IContextMetadataProvider
    {
        public void PopulateMetadata(Action<string, string> add)
        {
            add("app.key", "app-value");
        }
    }

    /// <summary>Makes the user context of the caller <see cref="AuditingHost"/> authenticates.</summary>
    public sealed class MetadataUserContextFactory : Blueprint.Authorization.IUserAuthorisationContextFactory
    {
        public Task<Blueprint.Authorization.IUserAuthorisationContext?> CreateContextAsync(System.Security.Claims.ClaimsIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<Blueprint.Authorization.IUserAuthorisationContext?>(new MetadataUserContext(identity.Name!));
        }
    }

    /// <summary>A user whose metadata is Signature365's user's: its Id, then its TenantId.</summary>
    public sealed class MetadataUserContext(string id) : Blueprint.Authorization.IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => id;

        public string IdentityId => id;

        public string AccountId => "tenant-1";

        public string Email => $"{id}@example.test";

        public string Name => id;

        public void PopulateMetadata(Action<string, object> add)
        {
            add("Id", this.Id);
            add("TenantId", this.AccountId);
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

    /// <summary>A repository whose every write is refused with a client error.</summary>
    public sealed class ClientErrorResourceEventAuditRepository : IResourceEventAuditRepository
    {
        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            throw new ApiException("The event was already recorded.", 409);
        }
    }

    /// <summary>A repository whose every write fails with a server error.</summary>
    public sealed class ServerErrorResourceEventAuditRepository : IResourceEventAuditRepository
    {
        public Task<object?> GetCurrentDataAsync(string href, Type resourceType)
        {
            return Task.FromResult<object?>(null);
        }

        public Task AddAsync(ResourceEvent resourceEvent)
        {
            throw new ApiException("The audit store is down.", 503);
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
        /// <summary>The header naming the user a request is authenticated as.</summary>
        public const string TestUserHeader = "X-Test-User";

        private readonly WebApplication _app;

        private AuditingHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<ServerError> serverErrors, ConcurrentQueue<LoggedEntry> logged)
        {
            this._app = app;
            this.Client = client;
            this.ServerErrors = serverErrors;
            this.Logged = logged;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<ServerError> ServerErrors { get; }

        /// <summary>Gets every entry logged at Information or above.</summary>
        public ConcurrentQueue<LoggedEntry> Logged { get; }

        public static async Task<AuditingHost> StartAsync(AuditRecords records, Action<BlueprintServiceBuilder> configure)
        {
            var serverErrors = new ConcurrentQueue<ServerError>();
            var logged = new ConcurrentQueue<LoggedEntry>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Host.UseDefaultServiceProvider(static options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });

            // Logging enabled at Critical is also what makes ASP.NET Core start the request's activity.
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ErrorRecordingLoggerProvider(serverErrors, logged));
            builder.Services.AddSingleton(records);

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(configure);

            var app = builder.Build();

            // A request naming a user in TestUserHeader is authenticated as that user, as an authentication handler would.
            app.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.TryGetValue(TestUserHeader, out var userName))
                {
                    httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName.ToString())],
                        authenticationType: "Test"));
                }

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new AuditingHost(app, client, serverErrors, logged);
        }

        public Task<string> PostAsync(string path, params (string Name, string Value)[] headers)
        {
            return this.SendAsync(System.Net.Http.HttpMethod.Post, path, headers);
        }

        public Task<string> GetAsync(string path)
        {
            return this.SendAsync(System.Net.Http.HttpMethod.Get, path, []);
        }

        public Task<string> PutAsync(string path)
        {
            return this.SendAsync(System.Net.Http.HttpMethod.Put, path, []);
        }

        private async Task<string> SendAsync(System.Net.Http.HttpMethod method, string path, (string Name, string Value)[] headers)
        {
            using var request = new System.Net.Http.HttpRequestMessage(method, path);

            if (method != System.Net.Http.HttpMethod.Get)
            {
                request.Content = new System.Net.Http.StringContent("{}", Encoding.UTF8, "application/json");
            }

            request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");

            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await this.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().BeInRange(200, 201, "the server raised: {0}", string.Join(" | ", this.ServerErrors.Select(static e => e.Exception.Message)));

            return body;
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>An entry logged at Error or above, with the category of the logger that logged it.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="Exception">The exception logged, or one carrying the message when none was.</param>
    private sealed record ServerError(string Category, Exception Exception);

    /// <summary>An entry logged at Information or above.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="Level">The entry's level.</param>
    /// <param name="EventId">The entry's event id.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception logged, if any.</param>
    private sealed record LoggedEntry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class ErrorRecordingLoggerProvider(ConcurrentQueue<ServerError> errors, ConcurrentQueue<LoggedEntry> logged) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ErrorRecordingLogger(categoryName, errors, logged);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ErrorRecordingLogger(string category, ConcurrentQueue<ServerError> errors, ConcurrentQueue<LoggedEntry> logged) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
            {
                logged.Enqueue(new LoggedEntry(category, logLevel, eventId, formatter(state, exception), exception));
            }

            if (logLevel >= LogLevel.Error)
            {
                errors.Enqueue(new ServerError(category, exception ?? new InvalidOperationException(formatter(state, exception))));
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

/// <summary>
/// Creates a widget, reporting it with a legacy created event to which the handler adds metadata of its own, one key of
/// which the pipeline adds again.
/// </summary>
[AllowAnonymous]
[RootLink("/audited-events/annotated-widgets")]
[HttpPost]
public partial class CreateAnnotatedAuditedWidgetCommand : ICommand<ResourceCreated<HrefWidgetApiResource>>
{
    public static ResourceCreated<HrefWidgetApiResource> Handle(CreateAnnotatedAuditedWidgetCommand command)
    {
        var created = new ResourceCreated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-10" });
        created.WithMetadata("reason", "created-in-test").WithMetadata("IpAddress", "set-by-handler");

        return created;
    }
}

/// <summary>Updates a widget, reporting it with a legacy updated event that names the widget by its self query.</summary>
[AllowAnonymous]
[RootLink("/audited-events/widgets/{Id}")]
[HttpPut]
public partial class UpdateAuditedWidgetCommand : ICommand<ResourceUpdated<HrefWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceUpdated<HrefWidgetApiResource> Handle(UpdateAuditedWidgetCommand command)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new GetHrefWidgetQuery { Id = command.Id });
    }
}

/// <summary>
/// Updates a widget, reporting it with a legacy updated event that names the widget by its self query, from a command that
/// declares it could instead find no widget.
/// </summary>
[AllowAnonymous]
[RootLink("/audited-events/union-widgets/{Id}")]
[HttpPut]
public partial class UpdateAuditedWidgetOrNotFoundCommand
    : ICommand<Results<ResourceUpdated<HrefWidgetApiResource>, NotFoundResult<HrefWidgetApiResource>>>
{
    public string Id { get; set; } = null!;

    public static Results<ResourceUpdated<HrefWidgetApiResource>, NotFoundResult<HrefWidgetApiResource>> Handle(UpdateAuditedWidgetOrNotFoundCommand command)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new GetHrefWidgetQuery { Id = command.Id });
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

/// <summary>
/// A query that passes on a legacy event another service returned, with that service's href and correlation id, as
/// Signature365's AdminPortal injection override queries pass on Subscriptions' events.
/// </summary>
[AllowAnonymous]
[RootLink("/audited-events/passed-on-widgets")]
public partial class PassOnAuditedWidgetQuery : IQuery<ResourceUpdated<HrefWidgetApiResource>>
{
    public const string UpstreamHref = "https://subscriptions.example.test/api/href-events/widgets/widget-9";

    public const string UpstreamCorrelationId = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    public static ResourceUpdated<HrefWidgetApiResource> Handle(PassOnAuditedWidgetQuery query)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-9" })
        {
            Href = UpstreamHref,
            CorrelationId = UpstreamCorrelationId,
        };
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
