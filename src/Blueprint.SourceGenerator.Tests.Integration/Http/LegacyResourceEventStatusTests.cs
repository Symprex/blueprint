using System.Net;
using System.Text;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a legacy <see cref="ResourceEvent{TResource}"/> — created, updated or deleted — is sent as a 200 with its
/// envelope and no <c>Location</c>, as legacy Blueprint sent it, and documented with the status it is sent with.
/// </summary>
/// <remarks>
/// Legacy's generated pipeline wrapped whatever an operation returned that was not an <c>OperationResult</c> in an
/// <c>OkResult</c> (<c>OperationExecutorMiddlewareBuilder.OperationResultCastFrame</c>), whose status is 200 unless set,
/// and a resource event — <c>ResourceCreated</c>, <c>ResourceUpdated</c> and <c>ResourceDeleted</c> included — is a plain
/// object. Its <c>OkResultOperationExecutor</c> wrote the status and the content and no <c>Location</c>. Signature365's
/// committed documents document every such operation, a DELETE included, as a 200 with the <c>ResourceEventOf…</c>
/// envelope.
/// </remarks>
public sealed class LegacyResourceEventStatusTests
{
    [Theory]
    [InlineData("POST", "api/legacy-events/widgets", "Created")]
    [InlineData("PUT", "api/legacy-events/widgets/widget-1", "Updated")]
    [InlineData("DELETE", "api/legacy-events/widgets/widget-1", "Deleted")]
    public async Task A_legacy_resource_event_is_a_200_with_its_envelope_and_no_location(string method, string path, string changeType)
    {
        await using var host = await KestrelTestHost.StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        response.Headers.Location.Should().BeNull();
        using var envelope = JsonDocument.Parse(body);
        Property(envelope.RootElement, "changeType").GetString().Should().Be(changeType);
        Property(Property(envelope.RootElement, "data"), "name").GetString().Should().Be(changeType.ToLowerInvariant());
        host.ServerErrors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/legacy-events/widgets", "post")]
    [InlineData("/legacy-events/widgets/{Id}", "put")]
    [InlineData("/legacy-events/widgets/{Id}", "delete")]
    public void A_legacy_resource_event_is_documented_as_the_200_it_is_sent_as(string path, string method)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var responses = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        responses.EnumerateObject().Select(static r => r.Name).Where(static status => status.StartsWith('2')).Should().Equal("200");
        responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema")
            .GetProperty("$ref").GetString().Should().Be("#/components/schemas/ResourceEventOfLegacyEventWidgetApiResource");
        responses.GetProperty("200").TryGetProperty("headers", out _).Should().BeFalse("legacy sent no Location");
    }

    /// <summary>The named member of <paramref name="element"/>, whatever the serializer's property casing.</summary>
    private static JsonElement Property(JsonElement element, string name)
    {
        return element.EnumerateObject().Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}

/// <summary>A widget whose changes are reported with legacy resource events.</summary>
public partial class LegacyEventWidgetApiResource : ApiResource
{
    public string Id { get; init; } = "widget-1";

    public required string Name { get; init; }
}

/// <summary>Creates a widget, reporting it with a legacy created event.</summary>
[AllowAnonymous]
[RootLink("/legacy-events/widgets")]
[HttpPost]
[Blueprint.Auditing.DoNotAudit]
public partial class CreateLegacyEventWidgetCommand : ICommand<ResourceCreated<LegacyEventWidgetApiResource>>
{
    public static ResourceCreated<LegacyEventWidgetApiResource> Handle(CreateLegacyEventWidgetCommand command)
    {
        // A self link, which a Location would be made from.
        var widget = new LegacyEventWidgetApiResource { Name = "created" };
        widget.Links["self"] = new Link { Href = "https://widgets.example.test/legacy-events/widgets/widget-1" };

        return new ResourceCreated<LegacyEventWidgetApiResource>(widget);
    }
}

/// <summary>Updates a widget, reporting it with a legacy updated event.</summary>
[AllowAnonymous]
[RootLink("/legacy-events/widgets/{Id}")]
[HttpPut]
[Blueprint.Auditing.DoNotAudit]
public partial class UpdateLegacyEventWidgetCommand : ICommand<ResourceUpdated<LegacyEventWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceUpdated<LegacyEventWidgetApiResource> Handle(UpdateLegacyEventWidgetCommand command)
    {
        return new ResourceUpdated<LegacyEventWidgetApiResource>(new LegacyEventWidgetApiResource { Name = "updated" });
    }
}

/// <summary>Deletes a widget, reporting it with a legacy deleted event that carries the widget.</summary>
[AllowAnonymous]
[RootLink("/legacy-events/widgets/{Id}")]
[HttpDelete]
[Blueprint.Auditing.DoNotAudit]
public partial class DeleteLegacyEventWidgetCommand : ICommand<ResourceDeleted<LegacyEventWidgetApiResource>>
{
    public string Id { get; set; } = null!;

    public static ResourceDeleted<LegacyEventWidgetApiResource> Handle(DeleteLegacyEventWidgetCommand command)
    {
        return new ResourceDeleted<LegacyEventWidgetApiResource>(new LegacyEventWidgetApiResource { Name = "deleted" });
    }
}
