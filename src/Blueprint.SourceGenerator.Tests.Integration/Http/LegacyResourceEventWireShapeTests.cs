using System.Net;
using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a legacy resource event is written, and documented, with legacy's envelope members only: not also with the
/// <c>WasSuccessful</c> it inherits from <see cref="OperationResult"/>.
/// </summary>
/// <remarks>
/// Legacy's <c>ResourceEvent</c> was a plain object, written through Newtonsoft.Json as an <c>OkResult</c>'s content. Here
/// it is an <see cref="OperationResult"/>, and System.Text.Json wrote that type's public <c>WasSuccessful</c> too: a real
/// Signature365 command (Partners' <c>CreatePartnerCompanyCommand</c>) written by the host's own response writer ended its
/// event with <c>"wasSuccessful":true</c>. The expected members are those Signature365's committed Partners document gives
/// <c>ResourceEventOfPartnerCompany</c>.
/// </remarks>
public sealed class LegacyResourceEventWireShapeTests
{
    private static readonly string[] _legacyEnvelopeMembers =
    [
        "$object", "eventId", "changeType", "created", "resourceObject", "href", "changedValues", "metadata", "secureData",
        "correlationId", "data",
    ];

    [Fact]
    public async Task A_legacy_resource_event_is_written_with_legacy_envelope_members_only()
    {
        await using var host = await KestrelTestHost.StartAsync();

        using var response = await host.Client.PostAsync("api/href-events/legacy/widgets", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        using var envelope = JsonDocument.Parse(body);

        // The integration app writes its property names as declared; Signature365's host camel-cases them.
        envelope.RootElement.EnumerateObject().Select(static p => p.Name)
            .Should().OnlyContain(name => _legacyEnvelopeMembers.Contains(name, StringComparer.OrdinalIgnoreCase));
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_legacy_resource_event_with_no_changed_values_is_written_without_a_changedValues_key()
    {
        // Legacy's converter wrote changedValues only when set (Signature365 8b40ecc961, JsonConverterFactoryForResourceEventOfT.cs),
        // so Signature365's CreateSignature response had no key where this one carried "changedValues": null.
        await using var host = await KestrelTestHost.StartAsync();

        using var response = await host.Client.PostAsync("api/href-events/legacy/widgets", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the server raised: {0}", string.Join(" | ", host.ServerErrors.Select(static e => e.Message)));
        using var envelope = JsonDocument.Parse(body);
        envelope.RootElement.EnumerateObject().Select(static p => p.Name).Should().NotContain(
            static name => string.Equals(name, "changedValues", StringComparison.OrdinalIgnoreCase));
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public void A_legacy_resource_event_is_documented_with_legacy_envelope_members_only()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var envelope = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ResourceEventOfHrefWidgetApiResource");

        envelope.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().BeEquivalentTo(_legacyEnvelopeMembers);
    }
}
