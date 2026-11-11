using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// D11's binding tiers on real Kestrel (Symprex/blueprint#66). The sample gateway's assembly declares
/// <c>[GatewayBind("TenantId", FromClaim = "tenant_id")]</c>, and the inner service's <c>TenantIsolationApiAuthoriser</c>
/// refuses a <c>TenantId</c> other than the caller's <c>account_id</c>, so a tenant that came from anywhere but the
/// expected claim is refused upstream as well as echoed back wrong.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class GatewayBindingTests(SampleSystemFixture system)
{
    [Fact]
    public async Task The_inner_op_receives_the_tenant_from_the_callers_claim()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-a", accountId: "tenant-a"));

        var note = await PostNoteAsync(caller, "gateway/tenant-notes", new { text = "Hello" });

        note.GetProperty("tenantId").GetString().Should().Be("tenant-a");
        note.GetProperty("text").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task A_tenant_the_caller_sends_in_the_body_is_ignored()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-b", accountId: "tenant-b"));

        var note = await PostNoteAsync(caller, "gateway/tenant-notes", new { text = "Hello", tenantId = "intruder" });

        note.GetProperty("tenantId").GetString().Should().Be("tenant-b");
    }

    [Fact]
    public async Task A_tenant_the_caller_sends_in_the_query_string_is_ignored()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-c", accountId: "tenant-c"));

        var note = await PostNoteAsync(caller, "gateway/tenant-notes?tenantId=intruder", new { text = "Hello" });

        note.GetProperty("tenantId").GetString().Should().Be("tenant-c");
    }

    [Fact]
    public async Task A_caller_without_the_claim_is_refused_by_the_gateway()
    {
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await caller.PostAsJsonAsync("gateway/tenant-notes", new { text = "Hello", tenantId = "default-account" });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, text);
        text.Should().Contain("tenant_id");
    }

    [Fact]
    public async Task The_op_tier_binding_beats_the_app_tier_binding()
    {
        // The app tier would send tenant-app, which the inner service refuses for account tenant-op.
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-app", accountId: "tenant-op"));

        var note = await PostNoteAsync(caller, "gateway/partner-notes", new { text = "Hello" });

        note.GetProperty("tenantId").GetString().Should().Be("tenant-op");
    }

    [Fact]
    public async Task A_fixed_value_replaces_the_callers_and_a_hidden_property_stays_unset()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-d", accountId: "tenant-d"));

        var note = await PostNoteAsync(caller, "gateway/partner-notes", new { text = "Hello", source = "caller", internalNote = "from caller" });

        note.GetProperty("source").GetString().Should().Be("partner-portal");
        note.TryGetProperty("internalNote", out var internalNote).Should().BeTrue();
        internalNote.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_map_hook_changes_the_inner_op_before_it_is_sent()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-e", accountId: "tenant-e"));

        var note = await PostNoteAsync(caller, "gateway/partner-notes", new { text = "  padded  " });

        note.GetProperty("text").GetString().Should().Be("padded");
    }

    [Fact]
    public async Task The_map_hook_can_refuse_the_call()
    {
        using var caller = system.Gateway.CreateClient(this.CreateToken(tenantId: "tenant-f", accountId: "tenant-f"));

        var response = await caller.PostAsJsonAsync("gateway/partner-notes", new { text = " blocked " });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, text);
        text.Should().Contain("Partners may not send this note.");
    }

    [Fact]
    public async Task Bound_and_hidden_properties_are_absent_from_the_gateways_OpenAPI()
    {
        using var gateway = system.Gateway.CreateClient(system.CreateCallerToken());

        var response = await gateway.GetAsync("gateway/.blueprint/openapi.json");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var document = JsonDocument.Parse(text);

        var tenantNote = FindRequestProperties(document, "/tenant-notes");
        tenantNote.Should().Contain(["text", "source", "internalNote"]).And.NotContain(p => p.Equals("tenantId", StringComparison.OrdinalIgnoreCase));

        var partnerNote = FindRequestProperties(document, "/partner-notes");
        partnerNote.Should().Equal("text");
    }

    private static async Task<JsonElement> PostNoteAsync(System.Net.Http.HttpClient caller, string path, object body)
    {
        var response = await caller.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Gets the names of an operation's request properties from the OpenAPI document: its parameters and the properties
    /// of its request body's schema, following a <c>$ref</c> into the components.
    /// </summary>
    private static List<string> FindRequestProperties(JsonDocument document, string path)
    {
        var paths = document.RootElement.GetProperty("paths");
        var name = paths.EnumerateObject().Select(static p => p.Name)
            .Should().ContainSingle(n => string.Equals(n, path, StringComparison.OrdinalIgnoreCase), document.RootElement.GetRawText()).Which;
        var operation = paths.GetProperty(name).GetProperty("post");

        var names = new List<string>();
        if (operation.TryGetProperty("parameters", out var parameters))
        {
            names.AddRange(parameters.EnumerateArray().Select(static p => p.GetProperty("name").GetString()!));
        }

        if (operation.TryGetProperty("requestBody", out var requestBody))
        {
            var schema = requestBody.GetProperty("content").EnumerateObject().First().Value.GetProperty("schema");
            if (schema.TryGetProperty("$ref", out var reference))
            {
                schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
            }

            names.AddRange(schema.GetProperty("properties").EnumerateObject().Select(static p => p.Name));
        }

        return names;
    }

    private string CreateToken(string tenantId, string accountId)
    {
        return system.Issuer.CreateToken(
        [
            new Claim("sub", "e2e-user"),
            new Claim("tenant_id", tenantId),
            new Claim("account_id", accountId),
            .. SampleSystemFixture.TemplateScopes.Select(static s => new Claim("scope", s)),
        ]);
    }
}
