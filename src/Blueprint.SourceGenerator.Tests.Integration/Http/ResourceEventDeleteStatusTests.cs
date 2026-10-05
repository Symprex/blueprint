using System.Net;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a DELETE reporting a deletion with the framework's own <see cref="ResourceEventResult{T}"/> is documented as
/// the 204 with no body the host sends it as.
/// </summary>
/// <remarks>
/// The host sends a <see cref="ResourceEventResult"/> by its change type, a deleted one as a 204, which carries no body
/// (<c>HttpOperationResultExecutor</c>'s bodiless-status guard). The generator documents a resource event by its HTTP method,
/// by the framework's convention that a DELETE reports a deletion, and documented a DELETE as a 200 with the event: a
/// response the host never sends, and a body a generated client would wait to parse.
/// </remarks>
public sealed class ResourceEventDeleteStatusTests
{
    [Fact]
    public async Task A_deleted_framework_resource_event_is_sent_as_a_204_without_a_body()
    {
        await using var host = await KestrelTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync("api/native-events/gadgets/gadget-1");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public void A_DELETE_reporting_a_framework_resource_event_is_documented_as_the_204_it_is_sent_as()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var responses = document.RootElement.GetProperty("paths").GetProperty("/native-events/gadgets/{Id}").GetProperty("delete").GetProperty("responses");

        responses.EnumerateObject().Select(static r => r.Name).Where(static status => status.StartsWith('2')).Should().Equal("204");
        responses.GetProperty("204").TryGetProperty("content", out _).Should().BeFalse("a 204 carries no body");
    }
}

/// <summary>A gadget whose deletion is reported with the framework's own resource event.</summary>
public sealed class NativeEventGadget
{
    public string Id { get; init; } = string.Empty;
}

/// <summary>Deletes a gadget, reporting it with the framework's own deleted event carrying the gadget.</summary>
[AllowAnonymous]
[RootLink("/native-events/gadgets/{Id}")]
[HttpDelete]
[Blueprint.Auditing.DoNotAudit]
public partial class DeleteNativeEventGadgetCommand : ICommand<ResourceEventResult<NativeEventGadget>>
{
    public string Id { get; set; } = string.Empty;

    public static ResourceEventResult<NativeEventGadget> Handle(DeleteNativeEventGadgetCommand command)
    {
        return new ResourceEventResult<NativeEventGadget>(ChangeType.Deleted, "gadget.deleted", command.Id, new NativeEventGadget { Id = command.Id });
    }
}
