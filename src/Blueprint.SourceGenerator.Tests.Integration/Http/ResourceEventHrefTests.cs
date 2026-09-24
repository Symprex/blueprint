using System.Text;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a resource event's <c>href</c> is made as legacy Blueprint made it: the registered <c>self</c> link of the
/// event's resource type, filled from the event's data or, failing that, its self query, under the API's base URI.
/// </summary>
/// <remarks>
/// Legacy's <c>ResourceEventHandler</c> set <c>Href = apiLinkGenerator.CreateUrl(DataModel.GetLinkFor(ResourceType,
/// "self"), Data ?? SelfQuery)</c>, and left it alone when no self link was registered. <c>CreateUrl</c> is the base URI
/// (<c>{scheme}://{host}{pathBase}/{basePath}/</c>) followed by the link's URL with its placeholders filled. It read neither
/// the resource's own <c>$links</c> nor the operation's URL. Signature365 stores the href with every audited event
/// (<c>ResourceEventCreatedEvent.Href</c>, <c>ApiEvent.Href</c>) and its clients read it from the response.
/// </remarks>
public sealed class ResourceEventHrefTests
{
    [Theory]
    [InlineData("POST", "api/href-events/legacy/widgets", "href-events/widgets/widget-1")]
    [InlineData("PUT", "api/href-events/legacy/widgets/widget-2", "href-events/widgets/widget-2")]
    [InlineData("DELETE", "api/href-events/legacy/widgets/gone", "href-events/widgets/gone")]
    [InlineData("POST", "api/href-events/legacy/widgets/widget-3/secret", "href-events/widgets/widget-3")]
    [InlineData("POST", "api/href-events/legacy/unlinked", null)]
    [InlineData("POST", "api/href-events/native/widgets", "href-events/widgets/widget-1")]
    [InlineData("PUT", "api/href-events/native/widgets/widget-4", "href-events/widgets/widget-4")]
    [InlineData("PUT", "api/href-events/native/widgets/gone", "href-events/widgets/gone")]
    public async Task A_resource_event_href_is_the_self_link_of_its_resource_type_filled_from_its_data_or_self_query(
        string method,
        string path,
        string? expectedRelativeHref)
    {
        await using var host = await KestrelTestHost.StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        ((int)response.StatusCode).Should().BeInRange(200, 201, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        using var envelope = JsonDocument.Parse(body);
        var href = envelope.RootElement.EnumerateObject()
            .Where(static p => string.Equals(p.Name, "href", StringComparison.OrdinalIgnoreCase))
            .Select(static p => p.Value)
            .SingleOrDefault();

        if (expectedRelativeHref is null)
        {
            href.ValueKind.Should().BeOneOf([JsonValueKind.Undefined, JsonValueKind.Null], "legacy left the href unset when no self link was registered");
        }
        else
        {
            // The base URI is the address the client called plus the API's base path, "api/".
            href.GetString().Should().Be(new Uri(host.Client.BaseAddress!, "api/" + expectedRelativeHref).AbsoluteUri);
        }

        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_resource_event_executed_outside_a_request_keeps_the_href_its_handler_gave_it()
    {
        // Outside a request there is no base URI to make the href under. The link generator registered for such hosts
        // guesses one (https://localhost/, without the API's base path), and an href made from it would be stored with
        // the audited event as though it were real.
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new OutOfRequestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var context = new ApiOperationContext(serviceProvider, app, ReportHrefWidgetChangedCommand.Configure(), CancellationToken.None);

        var result = await executor.GetPipeline(typeof(ReportHrefWidgetChangedCommand)).ExecuteAsync(context);

        var resourceEvent = result.Should().BeAssignableTo<ResourceEvent>(
            "the pipeline raised: {0}",
            (result as UnhandledExceptionResult)?.Exception.ToString()).Subject;
        resourceEvent.Data.Should().NotBeNull("the event carries a widget, from which a self link could have been filled");
        resourceEvent.Href.Should().BeNull();
    }

    private sealed class OutOfRequestServiceProvider(BlueprintApp app) : IServiceProvider
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

            // What AddBlueprintApi registers for a host without a request.
            return serviceType == typeof(IApiLinkGenerator) ? new ApiLinkGenerator(app, "https://localhost/") : null;
        }
    }
}

/// <summary>A widget whose events carry an href.</summary>
public partial class HrefWidgetApiResource : ApiResource
{
    public string Id { get; init; } = "widget-1";

    public string Name { get; init; } = "widget";
}

/// <summary>The part of a widget that one of its events reports, the event belonging to the whole widget.</summary>
public partial class HrefWidgetSecretPartialApiResource : ApiResource
{
    public string Id { get; init; } = "widget-1";

    public string Secret { get; init; } = "secret";
}

/// <summary>A resource no self link is registered for.</summary>
public partial class HrefUnlinkedApiResource : ApiResource
{
    public string Id { get; init; } = "unlinked-1";
}

/// <summary>Gets a widget, the self link of <see cref="HrefWidgetApiResource"/>; the widget <see cref="GoneId"/> does not exist.</summary>
[AllowAnonymous]
[SelfLink(typeof(HrefWidgetApiResource), "/href-events/widgets/{Id}")]
public partial class GetHrefWidgetQuery : IQuery<Results<OkResult<HrefWidgetApiResource>, NotFoundResult<HrefWidgetApiResource>>>
{
    public const string GoneId = "gone";

    public string Id { get; set; } = null!;

    public static Results<OkResult<HrefWidgetApiResource>, NotFoundResult<HrefWidgetApiResource>> Handle(GetHrefWidgetQuery query)
    {
        return query.Id == GoneId
            ? new NotFoundResult<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = query.Id })
            : new OkResult<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = query.Id, Name = "fetched" });
    }
}

/// <summary>Creates a widget, reporting it with a legacy created event carrying the widget.</summary>
[AllowAnonymous]
[RootLink("/href-events/legacy/widgets")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class CreateHrefLegacyWidgetCommand : ICommand<ResourceCreated<HrefWidgetApiResource>>
{
    public static ResourceCreated<HrefWidgetApiResource> Handle(CreateHrefLegacyWidgetCommand command)
    {
        // A self link of the widget's own, which legacy did not make the href from.
        var widget = new HrefWidgetApiResource { Id = "widget-1" };
        widget.Links["self"] = new Link { Href = "https://elsewhere.example.test/widgets/widget-1" };

        return new ResourceCreated<HrefWidgetApiResource>(widget);
    }
}

/// <summary>Updates a widget, reporting it with a legacy updated event that names the widget by its self query.</summary>
[AllowAnonymous]
[RootLink("/href-events/legacy/widgets/{Id}")]
[HttpPut]
[Blueprint.Auditing.DoNotAudit]
public partial class UpdateHrefLegacyWidgetCommand : ICommand<ResourceUpdated<HrefWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceUpdated<HrefWidgetApiResource> Handle(UpdateHrefLegacyWidgetCommand command)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new GetHrefWidgetQuery { Id = command.Id });
    }
}

/// <summary>Deletes a widget, reporting it with a legacy deleted event that names the widget by its self query.</summary>
[AllowAnonymous]
[RootLink("/href-events/legacy/widgets/{Id}")]
[HttpDelete]
[Blueprint.Auditing.DoNotAudit]
public partial class DeleteHrefLegacyWidgetCommand : ICommand<ResourceDeleted<HrefWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceDeleted<HrefWidgetApiResource> Handle(DeleteHrefLegacyWidgetCommand command)
    {
        return new ResourceDeleted<HrefWidgetApiResource>(new GetHrefWidgetQuery { Id = command.Id });
    }
}

/// <summary>
/// Rotates a widget's secret, reporting it with a legacy event that carries only the secret but belongs to the widget, as
/// Signature365's <c>ResourceEventHelpers.CreatePartialApiResourceEvent</c> makes its add-on client-secret events.
/// </summary>
[AllowAnonymous]
[RootLink("/href-events/legacy/widgets/{Id}/secret")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class RotateHrefLegacyWidgetSecretCommand : ICommand<ResourceEvent<HrefWidgetSecretPartialApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceEvent<HrefWidgetSecretPartialApiResource> Handle(RotateHrefLegacyWidgetSecretCommand command)
    {
        return new ResourceEvent<HrefWidgetSecretPartialApiResource>(
            ResourceEventChangeType.Updated,
            "hrefWidget.secretRotated",
            new HrefWidgetSecretPartialApiResource { Id = command.Id })
        {
            ResourceType = typeof(HrefWidgetApiResource),
        };
    }
}

/// <summary>Creates a resource no self link is registered for, reporting it with a legacy created event.</summary>
[AllowAnonymous]
[RootLink("/href-events/legacy/unlinked")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class CreateHrefLegacyUnlinkedCommand : ICommand<ResourceCreated<HrefUnlinkedApiResource>>
{
    public static ResourceCreated<HrefUnlinkedApiResource> Handle(CreateHrefLegacyUnlinkedCommand command)
    {
        return new ResourceCreated<HrefUnlinkedApiResource>(new HrefUnlinkedApiResource());
    }
}

/// <summary>Creates a widget, reporting it with the framework's own event carrying the widget.</summary>
[AllowAnonymous]
[RootLink("/href-events/native/widgets")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class CreateHrefNativeWidgetCommand : ICommand<ResourceEventResult<HrefWidgetApiResource>>
{
    public static ResourceEventResult<HrefWidgetApiResource> Handle(CreateHrefNativeWidgetCommand command)
    {
        var widget = new HrefWidgetApiResource { Id = "widget-1" };

        return new ResourceEventResult<HrefWidgetApiResource>(ChangeType.Created, "hrefWidget.created", widget.Id, widget);
    }
}

/// <summary>Updates a widget, reporting it with the framework's own deferred event that names the widget by its self query.</summary>
[AllowAnonymous]
[RootLink("/href-events/native/widgets/{Id}")]
[HttpPut]
[Blueprint.Auditing.DoNotAudit]
public partial class UpdateHrefNativeWidgetCommand : ICommand<DeferredResourceEventResult<HrefWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static DeferredResourceEventResult<HrefWidgetApiResource> Handle(UpdateHrefNativeWidgetCommand command)
    {
        return new DeferredResourceEventResult<HrefWidgetApiResource>(
            ChangeType.Updated,
            "hrefWidget.updated",
            new GetHrefWidgetQuery { Id = command.Id });
    }
}

/// <summary>Reports a change to a widget from outside a request, with a legacy updated event carrying the widget.</summary>
public partial class ReportHrefWidgetChangedCommand : ICommand<ResourceUpdated<HrefWidgetApiResource>>
{
    public static ResourceUpdated<HrefWidgetApiResource> Handle(ReportHrefWidgetChangedCommand command)
    {
        return new ResourceUpdated<HrefWidgetApiResource>(new HrefWidgetApiResource { Id = "widget-5" });
    }
}