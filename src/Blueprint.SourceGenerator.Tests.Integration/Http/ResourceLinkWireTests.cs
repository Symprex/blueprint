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

        public static async Task<LinkWireHost> StartAsync()
        {
            var serverErrors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ErrorRecordingLoggerProvider(serverErrors));

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

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
