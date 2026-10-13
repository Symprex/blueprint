using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Expansion;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that every link a resource is written with, <c>self</c> included, is the absolute URL legacy Blueprint wrote.
/// </summary>
/// <remarks>
/// <para>
/// Legacy made every link, <c>self</c> among them, the same way: <c>LinkGeneratorHandler.AddLinksAsync</c> ran every
/// <c>IResourceLinkGenerator</c> (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/Middleware/LinkGeneratorHandler.cs:85-101</c>),
/// and <c>EntityOperationResourceLinkGenerator</c> added each link registered for the resource's type, <c>self</c> no
/// different, if the user could see it, with the href <c>IApiLinkGenerator.CreateUrl</c> made
/// (<c>EntityOperationResourceLinkGenerator.cs:59-108</c>): the request's base URI followed by the link's filled URL
/// (<c>ApiLinkGenerator.cs:92-110</c>). So a resource's <c>$links.self</c> was absolute, as its other links were.
/// </para>
/// <para>
/// Each test runs the app as a consumer runs it, <c>AddBlueprintApi</c> and <c>MapBlueprintApi</c> on Kestrel, because
/// the base URI is the request's.
/// </para>
/// </remarks>
public sealed class ResourceLinkWireTests
{
    [Fact]
    public async Task A_querys_resource_is_written_with_every_link_absolute_self_included()
    {
        await using var host = await LinkWireHost.StartAsync();

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/widget-1"));

        var links = body.RootElement.GetProperty("$links");
        links.GetProperty("self").GetProperty("href").GetString().Should().Be(
            host.AbsoluteUri("api/link-wire/widgets/widget-1"),
            "legacy made the self link as it made every link, under the request's base URI");
        links.GetProperty("archive").GetProperty("href").GetString().Should().Be(host.AbsoluteUri("api/link-wire/widgets/widget-1/archive"));
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_data_is_written_with_every_link_absolute_self_included()
    {
        // Legacy wrapped the returned event in an OkResult, and LinkGeneratorHandler linked the event's data through the
        // event's IApiResourceCollection (LinkGeneratorHandler.cs:17-21, :39-42; ResourceEvent.cs:201-204), whatever the
        // event's subclass.
        await using var host = await LinkWireHost.StartAsync();

        var raw = await host.SendAsync(System.Net.Http.HttpMethod.Post, "api/link-wire/widgets");
        using var body = JsonDocument.Parse(raw);

        var links = body.RootElement.GetProperty("data").GetProperty("$links");
        links.TryGetProperty("self", out var self).Should().BeTrue("legacy linked the data of a ResourceCreated event; the response was {0}", raw);
        self.GetProperty("href").GetString().Should().Be(
            host.AbsoluteUri("api/link-wire/widgets/widget-2"),
            "legacy made the self link of an event's data as it made every link, under the request's base URI");
        links.GetProperty("archive").GetProperty("href").GetString().Should().Be(host.AbsoluteUri("api/link-wire/widgets/widget-2/archive"));
        host.ServerErrors.Should().BeEmpty();
    }

    // Legacy's Link.Type carried only a Newtonsoft ignore (Signature365 8b40ecc961, Blueprint/Link.cs:19-23), and its HTTP
    // output was System.Text.Json with BlueprintJsonOptions (Blueprint.Http/BlueprintHttpOptionsSetup.cs:20-22;
    // Formatters/SystemTextJsonResultOutputFormatter.cs:64), which set no DefaultIgnoreCondition (BlueprintJsonOptions.cs:22-45);
    // no Signature365 app changed either. EntityOperationResourceLinkGenerator made each resource link with its Href alone
    // (EntityOperationResourceLinkGenerator.cs:104-107). So every such link was written {"href":...,"type":null}, as a
    // program built from those two files verbatim writes it.
    [Fact]
    public async Task A_querys_resource_links_without_a_type_are_written_with_a_null_type_as_legacy_wrote_them()
    {
        await using var host = await LinkWireHost.StartAsync();

        var raw = await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/widget-1");

        raw.Should().Contain($$"""
            "self":{"href":"{{host.AbsoluteUri("api/link-wire/widgets/widget-1")}}","type":null}
            """);
        raw.Should().Contain($$"""
            "archive":{"href":"{{host.AbsoluteUri("api/link-wire/widgets/widget-1/archive")}}","type":null}
            """);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_commands_resource_event_data_links_without_a_type_are_written_with_a_null_type_as_legacy_wrote_them()
    {
        // The event's data was linked by the same generator (LinkGeneratorHandler.cs:17-21, :39-42) and written by the same
        // serializer.
        await using var host = await LinkWireHost.StartAsync();

        var raw = await host.SendAsync(System.Net.Http.HttpMethod.Post, "api/link-wire/widgets");
        using var body = JsonDocument.Parse(raw);

        var self = body.RootElement.GetProperty("data").GetProperty("$links").GetProperty("self");
        self.EnumerateObject().Select(static p => p.Name).Should().Equal(["href", "type"], "the response was {0}", raw);
        self.GetProperty("type").ValueKind.Should().Be(JsonValueKind.Null);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_list_querys_items_are_written_with_every_link_absolute_self_included()
    {
        // Legacy linked each item of a resource collection (LinkGeneratorHandler.cs:39-66).
        await using var host = await LinkWireHost.StartAsync();

        var raw = await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets");
        using var body = JsonDocument.Parse(raw);

        var items = body.RootElement.GetProperty("values").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        items.Select(static i => i.GetProperty("$links").TryGetProperty("self", out var s) ? s.GetProperty("href").GetString() : null)
            .Should().Equal(
                [host.AbsoluteUri("api/link-wire/widgets/widget-3"), host.AbsoluteUri("api/link-wire/widgets/widget-4")],
                "legacy linked every item of a list; the response was {0}",
                raw);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_self_links_placeholder_value_is_escaped_as_legacy_escaped_it()
    {
        // Legacy filled a placeholder through ApiOperationLink.CreateRelativeUrl, which escapes its value
        // (Uri.EscapeDataString, Blueprint/ApiOperationLink.cs:264-285 at Signature365 8b40ecc961), not by interpolating
        // it raw.
        await using var host = await LinkWireHost.StartAsync();

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/a%20b"));

        body.RootElement.GetProperty("$links").GetProperty("self").GetProperty("href").GetString().Should().Be(
            host.AbsoluteUri("api/link-wire/widgets/a%20b"));
        host.ServerErrors.Should().BeEmpty();
    }

    // Legacy linked only in an app that called AddHateoasLinks(), which registered EntityOperationResourceLinkGenerator and
    // the LinkGeneratorMiddlewareBuilder pipeline step (Signature365 8b40ecc961, Blueprint.Http/
    // BlueprintApiBuilderExtensions.cs:101-111). An app that did not had no link step at all (EventStore's generated
    // pipelines have none), so its resources were written with the dictionary ApiResource always carries, empty
    // (ApiResource.cs:45-48): "$links":{} (EventStore.Tests/Handlers/ResourceEventCreatedEventHandlerTests.cs:70).
    [Fact]
    public async Task When_the_app_disables_links_then_a_querys_resource_is_written_with_empty_links()
    {
        await using var host = await LinkWireHost.StartAsync(static blueprint => blueprint.DisableHateoasLinks());

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/widget-1"));

        AssertEmptyLinks(body.RootElement);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_two_apps_in_one_process_each_have_their_own_service_provider_then_each_keeps_its_own_link_setting()
    {
        // Not legacy-derived: legacy's setting was which services each app's container was given (Blueprint.Http/
        // BlueprintApiBuilderExtensions.cs:101-111). This one is held on the app, not in a static, so an app that disables links
        // does not disable them for another app in the same process. Two apps registered in one service provider are not supported (BlueprintApp.HateoasLinksDisabled).
        await using var withoutLinks = await LinkWireHost.StartAsync(static blueprint => blueprint.DisableHateoasLinks());
        await using var withLinks = await LinkWireHost.StartAsync();

        using var unlinked = JsonDocument.Parse(await withoutLinks.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/widget-1"));
        using var linked = JsonDocument.Parse(await withLinks.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets/widget-1"));

        AssertEmptyLinks(unlinked.RootElement);
        linked.RootElement.GetProperty("$links").GetProperty("self").GetProperty("href").GetString().Should().Be(
            withLinks.AbsoluteUri("api/link-wire/widgets/widget-1"));
        withoutLinks.ServerErrors.Should().BeEmpty();
        withLinks.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_app_disables_links_then_a_commands_event_data_has_empty_links_and_the_event_keeps_its_href()
    {
        // The href never came from the data's links: ResourceEventHandler made it from the resource type's self link
        // itself, in every app (ResourceEventHandler.cs:54-65).
        await using var host = await LinkWireHost.StartAsync(static blueprint => blueprint.DisableHateoasLinks());

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Post, "api/link-wire/widgets"));

        AssertEmptyLinks(body.RootElement.GetProperty("data"));
        body.RootElement.GetProperty("href").GetString().Should().Be(host.AbsoluteUri("api/link-wire/widgets/widget-2"));
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_app_disables_links_then_a_list_querys_items_have_empty_links()
    {
        await using var host = await LinkWireHost.StartAsync(static blueprint => blueprint.DisableHateoasLinks());

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/widgets"));

        var items = body.RootElement.GetProperty("values").EnumerateArray().ToArray();
        items.Should().HaveCount(2);
        foreach (var item in items)
        {
            AssertEmptyLinks(item);
        }

        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_app_disables_links_then_a_link_its_handler_added_is_still_written()
    {
        // Only the link step was missing; a resource's own AddLink was the handler's (ApiResource.cs:87-107).
        await using var host = await LinkWireHost.StartAsync(static blueprint => blueprint.DisableHateoasLinks());

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/handler-linked-widgets/widget-5"));

        body.RootElement.GetProperty("$links").EnumerateObject().Select(static link => (link.Name, link.Value.GetProperty("href").GetString()))
            .Should().Equal(("manual", "https://elsewhere.example/widgets/widget-5"));
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_app_does_not_disable_links_then_the_handlers_link_is_written_beside_the_generated_ones()
    {
        await using var host = await LinkWireHost.StartAsync();

        using var body = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/handler-linked-widgets/widget-5"));

        body.RootElement.GetProperty("$links").EnumerateObject().Select(static link => link.Name)
            .Should().BeEquivalentTo(["manual", "self", "archive"]);
        host.ServerErrors.Should().BeEmpty();
    }

    // Legacy keyed a [Link] without a Rel by its operation's descriptor name, taken when the link was built and so before
    // an app's conventions renamed the descriptor: `linkAttribute.Rel ?? descriptor.Name` (Signature365 8b40ecc961,
    // Blueprint/Configuration/OperationScanner.cs:294-303), the name cased by Blueprint/ApiOperationDescriptor.cs:50 and
    // Utilities/StringExtensions.cs:65-120. So two such links on one resource had two keys: legacy's own test counted "2 +
    // self" (Blueprint.Tests/ResourceEvents/Given_ResourceEvent_Data_Return.cs:99-100, fixtures :155-185). Signature365's
    // committed documents show the same key in x-links: Portal's DeleteGroupCommand is "DeleteGroup"
    // (Portal/Spec/openapi.json:26495), and Partners.Gateway's PartnersGatewayGetPartnerCountriesQuery is
    // "PartnersGatewayGetPartnerCountries" beside the operationId "GetPartnerCountries" its convention made
    // (Partners.Gateway/Spec/openapi.json:5443-5444). IntegrationTestBlueprintApp removes "Sample", as that convention did.
    [Fact]
    public async Task A_link_without_a_rel_is_keyed_by_its_operations_legacy_name_from_before_the_apps_removal()
    {
        await using var host = await LinkWireHost.StartAsync();

        var raw = await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/link-wire/rel-less-widgets/widget-6");
        using var body = JsonDocument.Parse(raw);

        body.RootElement.GetProperty("$links").EnumerateObject().Select(static link => (link.Name, link.Value.GetProperty("href").GetString()))
            .Should().BeEquivalentTo(
                [
                    ("self", host.AbsoluteUri("api/link-wire/rel-less-widgets/widget-6")),
                    ("DeleteSampleRelLessLinkWireWidget", host.AbsoluteUri("api/link-wire/rel-less-widgets/widget-6")),
                    ("GetRelLessLinkWireWidgetvCard", host.AbsoluteUri("api/link-wire/rel-less-widgets/widget-6/vcard")),
                ],
                "legacy keyed each Rel-less link by its own operation's cased name, before the app removed \"Sample\"; the response was {0}",
                raw);
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_without_a_rel_is_listed_in_x_links_by_its_legacy_name_beside_the_apps_operation_id()
    {
        // Legacy's x-links wrote `rel = l.Rel, operationId = descriptor.Name` (Blueprint.OpenApi/BlueprintLinkSchemaProcessor.cs:43-52),
        // the rel from before the app's conventions and the operationId from after them.
        await using var host = await LinkWireHost.StartAsync();

        using var document = JsonDocument.Parse(await host.SendAsync(System.Net.Http.HttpMethod.Get, "api/.blueprint/openapi.json"));

        document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RelLessLinkWireWidgetApiResource")
            .GetProperty("x-links").EnumerateArray()
            .Select(static link => (link.GetProperty("rel").GetString(), link.GetProperty("operationId").GetString()))
            .Should().BeEquivalentTo(
            [
                ("DeleteSampleRelLessLinkWireWidget", "DeleteRelLessLinkWireWidget"),
                ("GetRelLessLinkWireWidgetvCard", "GetRelLessLinkWireWidgetvCard"),
            ]);
        host.ServerErrors.Should().BeEmpty();
    }

    private static void AssertEmptyLinks(JsonElement resource)
    {
        resource.TryGetProperty("$links", out var links).Should().BeTrue("legacy always wrote the links dictionary; the resource was {0}", resource.GetRawText());
        links.ValueKind.Should().Be(JsonValueKind.Object);
        links.EnumerateObject().Should().BeEmpty("an app without AddHateoasLinks() had no link step; the resource was {0}", resource.GetRawText());
    }

    /// <summary>The integration app registered and mapped as a consumer registers and maps it, served by Kestrel.</summary>
    private sealed class LinkWireHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LinkWireHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<Exception> serverErrors)
        {
            this._app = app;
            this.Client = client;
            this.ServerErrors = serverErrors;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<Exception> ServerErrors { get; }

        public static async Task<LinkWireHost> StartAsync(Action<BlueprintServiceBuilder>? configure = null)
        {
            var serverErrors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ErrorRecordingLoggerProvider(serverErrors));

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint =>
            {
                blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>();
                configure?.Invoke(blueprint);
            });

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new LinkWireHost(app, client, serverErrors);
        }

        public string AbsoluteUri(string path)
        {
            return new Uri(this.Client.BaseAddress!, path).AbsoluteUri;
        }

        public async Task<string> SendAsync(System.Net.Http.HttpMethod method, string path)
        {
            using var request = new System.Net.Http.HttpRequestMessage(method, path);

            if (method != System.Net.Http.HttpMethod.Get)
            {
                request.Content = new System.Net.Http.StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await this.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            ((int)response.StatusCode).Should().Be(200, "the server raised: {0}; body {1}", string.Join(" | ", this.ServerErrors.Select(static e => e.Message)), body);

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

/// <summary>A widget with a self link and one other link.</summary>
public partial class LinkWireWidgetApiResource : ApiResource
{
    public string Id { get; init; } = "widget-1";
}

/// <summary>Gets a widget, the self link of <see cref="LinkWireWidgetApiResource"/>.</summary>
[AllowAnonymous]
[SelfLink(typeof(LinkWireWidgetApiResource), "/link-wire/widgets/{Id}")]
public partial class GetLinkWireWidgetQuery : IQuery<LinkWireWidgetApiResource>
{
    public string Id { get; set; } = null!;

    public static LinkWireWidgetApiResource Handle(GetLinkWireWidgetQuery query)
    {
        return new LinkWireWidgetApiResource { Id = query.Id };
    }
}

/// <summary>Archives a widget, the widget's <c>archive</c> link.</summary>
[AllowAnonymous]
[Link(typeof(LinkWireWidgetApiResource), "/link-wire/widgets/{Id}/archive", "archive")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class ArchiveLinkWireWidgetCommand : ICommand<ResourceUpdated<LinkWireWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceUpdated<LinkWireWidgetApiResource> Handle(ArchiveLinkWireWidgetCommand command)
    {
        return new ResourceUpdated<LinkWireWidgetApiResource>(new LinkWireWidgetApiResource { Id = command.Id });
    }
}

/// <summary>Lists two widgets.</summary>
[AllowAnonymous]
[RootLink("/link-wire/widgets")]
public partial class ListLinkWireWidgetsQuery : IQuery<ListApiResource<LinkWireWidgetApiResource>>
{
    public static ListApiResource<LinkWireWidgetApiResource> Handle(ListLinkWireWidgetsQuery query)
    {
        return new ListApiResource<LinkWireWidgetApiResource>([new LinkWireWidgetApiResource { Id = "widget-3" }, new LinkWireWidgetApiResource { Id = "widget-4" }]);
    }
}

/// <summary>Gets a widget whose handler adds a link of its own.</summary>
[AllowAnonymous]
[RootLink("/link-wire/handler-linked-widgets/{Id}")]
public partial class GetHandlerLinkedLinkWireWidgetQuery : IQuery<LinkWireWidgetApiResource>
{
    public string Id { get; set; } = null!;

    public static LinkWireWidgetApiResource Handle(GetHandlerLinkedLinkWireWidgetQuery query)
    {
        var widget = new LinkWireWidgetApiResource { Id = query.Id };
        widget.AddLink("manual", new Link { Href = "https://elsewhere.example/widgets/" + query.Id });

        return widget;
    }
}

/// <summary>A widget whose non-self links declare no <c>Rel</c>.</summary>
public partial class RelLessLinkWireWidgetApiResource : ApiResource
{
    public string Id { get; init; } = "widget-6";
}

/// <summary>Gets a widget, the self link of <see cref="RelLessLinkWireWidgetApiResource"/>.</summary>
[AllowAnonymous]
[SelfLink(typeof(RelLessLinkWireWidgetApiResource), "/link-wire/rel-less-widgets/{Id}")]
public partial class GetRelLessLinkWireWidgetQuery : IQuery<RelLessLinkWireWidgetApiResource>
{
    public string Id { get; set; } = null!;

    public static RelLessLinkWireWidgetApiResource Handle(GetRelLessLinkWireWidgetQuery query)
    {
        return new RelLessLinkWireWidgetApiResource { Id = query.Id };
    }
}

/// <summary>
/// Deletes a widget through a link without a <c>Rel</c>, as Signature365's <c>DeleteGroupCommand</c> does. Its name holds
/// the "Sample" <see cref="IntegrationTestBlueprintApp"/> removes, which legacy's rel kept.
/// </summary>
[AllowAnonymous]
[Link(typeof(RelLessLinkWireWidgetApiResource), "/link-wire/rel-less-widgets/{Id}")]
[HttpDelete]
public partial class DeleteSampleRelLessLinkWireWidgetCommand : ICommand<RelLessLinkWireWidgetApiResource>
{
    public string Id { get; set; } = null!;

    public static RelLessLinkWireWidgetApiResource Handle(DeleteSampleRelLessLinkWireWidgetCommand command)
    {
        return new RelLessLinkWireWidgetApiResource { Id = command.Id };
    }
}

/// <summary>Gets a widget's vCard through a second link without a <c>Rel</c>, whose name legacy's casing folds.</summary>
[AllowAnonymous]
[Link(typeof(RelLessLinkWireWidgetApiResource), "/link-wire/rel-less-widgets/{Id}/vcard")]
public partial class GetRelLessLinkWireWidgetVCardQuery : IQuery<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(GetRelLessLinkWireWidgetVCardQuery query)
    {
        return "BEGIN:VCARD";
    }
}

/// <summary>Creates a widget, reporting it with a legacy created event carrying the widget.</summary>
[AllowAnonymous]
[RootLink("/link-wire/widgets")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class CreateLinkWireWidgetCommand : ICommand<ResourceCreated<LinkWireWidgetApiResource>>
{
    public static ResourceCreated<LinkWireWidgetApiResource> Handle(CreateLinkWireWidgetCommand command)
    {
        return new ResourceCreated<LinkWireWidgetApiResource>(new LinkWireWidgetApiResource { Id = "widget-2" });
    }
}
