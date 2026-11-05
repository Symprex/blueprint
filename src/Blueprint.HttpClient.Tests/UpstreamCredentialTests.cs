using System.Net;
using System.Net.Http.Headers;
using Blueprint.HttpClient.Readers;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves the credential the send primitive applies to a request, and where in the request's composition it applies it.
/// </summary>
public sealed class UpstreamCredentialTests
{
    [Fact]
    public async Task A_bearer_credential_sets_the_Authorization_header()
    {
        var captured = await SendAsync(new BearerTokenCredential("tok-1"), modifyRequest: null);

        captured.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "tok-1"));
    }

    [Fact]
    public async Task The_credential_is_applied_after_ModifyRequest_so_it_wins_over_a_caller_Authorization()
    {
        var captured = await SendAsync(
            new BearerTokenCredential("from-credential"),
            modifyRequest: request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "from-caller"));

        captured.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "from-credential"));
    }

    [Fact]
    public async Task A_credential_sees_what_ModifyRequest_did()
    {
        var order = new List<string>();
        var credential = new RecordingCredential(order);

        await SendAsync(credential, modifyRequest: _ => order.Add("modify"));

        order.Should().Equal("modify", "credential");
    }

    [Fact]
    public async Task Without_a_credential_a_caller_Authorization_is_left_alone()
    {
        var captured = await SendAsync(credential: null, modifyRequest: request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "from-caller"));

        captured.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "from-caller"));
    }

    [Fact]
    public async Task A_client_s_request_modifier_runs_after_ModifyRequest_and_before_the_credential()
    {
        var order = new List<string>();

        await SendAsync(new RecordingCredential(order), modifyRequest: _ => order.Add("modify"), requestModifier: _ => order.Add("client"));

        order.Should().Equal("modify", "client", "credential");
    }

    [Fact]
    public void A_bearer_credential_refuses_a_missing_token()
    {
        var act = () => new BearerTokenCredential(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<HttpRequestMessage> SendAsync(IUpstreamCredential? credential, Action<HttpRequestMessage>? modifyRequest, Action<HttpRequestMessage>? requestModifier = null)
    {
        HttpRequestMessage? captured = null;
        var client = new BlueprintHttpClient(
            new System.Net.Http.HttpClient(
                new FakeHttpHandler(
                    req =>
                    {
                        captured = req;
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", new MediaTypeHeaderValue("application/json")) };
                    }))
            { BaseAddress = new Uri("https://localhost") });

        await client.SendAsync(
            HttpMethod.Get,
            "/templates/t-1",
            new GetTemplateQuery { Id = "t-1" },
            operationTypeInfo: null,
            new JsonResponseReader<System.Text.Json.JsonElement>(),
            "GetTemplateQuery",
            modifyRequest is null ? null : new CallOptions { ModifyRequest = modifyRequest },
            requestModifier,
            credential);

        return captured!;
    }

    private sealed class RecordingCredential : IUpstreamCredential
    {
        private readonly List<string> _order;

        public RecordingCredential(List<string> order)
        {
            this._order = order;
        }

        public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this._order.Add("credential");
            return ValueTask.CompletedTask;
        }
    }
}
