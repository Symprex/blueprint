using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;
using PipelineRecorder = Blueprint.EndToEnd.Tests.GatewayExposeTests.PipelineRecorder;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// <c>[ForwardsTo&lt;TInner&gt;]</c> on real Kestrel (Symprex/blueprint#68, D10): the sample gateway's
/// <c>AddGatewayDeclaredNoteCommand</c> declares <c>Text</c> and <c>Channel</c>, forwarded as the inner
/// <c>AddTenantNoteCommand</c>'s <c>Text</c> and <c>Source</c>, and validated by the inner properties' rules.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayForwardsToTests(SampleSystemFixture system)
{
    [Fact]
    public async Task The_inner_op_receives_the_declared_properties_the_rename_and_the_bound_tenant()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken("tenant-g"));

        var response = await caller.PostAsJsonAsync("gateway/declared-notes", new { text = "Hello", channel = "email", internalNote = "from caller" });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var document = JsonDocument.Parse(text);
        var note = document.RootElement;
        note.GetProperty("tenantId").GetString().Should().Be("tenant-g");
        note.GetProperty("text").GetString().Should().Be("Hello");
        note.GetProperty("source").GetString().Should().Be("email");
        note.GetProperty("internalNote").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_inner_propertys_MaxLength_refuses_an_over_long_value_at_the_gateway_before_any_hop()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken("tenant-h"));
        using var pipelines = new PipelineRecorder();

        var response = await caller.PostAsJsonAsync("gateway/declared-notes", new { text = "Hello", channel = new string('c', 51) });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, text);

        // Named by the gateway's property: the inner service would have named Source.
        text.Should().Contain("Channel").And.NotContain("\"Source\"");
        pipelines.Count("AddGatewayDeclaredNoteCommandPipeline").Should().Be(1);
        pipelines.Count("AddTenantNoteCommandPipeline").Should().Be(0);
    }

    [Fact]
    public async Task The_inner_propertys_Required_refuses_a_missing_value_at_the_gateway()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken("tenant-i"));
        using var pipelines = new PipelineRecorder();

        var response = await caller.PostAsJsonAsync("gateway/declared-notes", new { channel = "email" });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, text);
        text.Should().Contain("Text");
        pipelines.Count("AddTenantNoteCommandPipeline").Should().Be(0);
    }

    private string CreateToken(string tenant)
    {
        return system.Issuer.CreateToken(
        [
            new Claim("sub", "e2e-user"),
            new Claim("tenant_id", tenant),
            new Claim("account_id", tenant),
            .. SampleSystemFixture.TemplateScopes.Select(static s => new Claim("scope", s)),
        ]);
    }
}
