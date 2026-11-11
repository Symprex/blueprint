using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Sample.Gateway;
using Blueprint.SourceGenerator.Sample.Gateway.Api;
using Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(ApiOperationContext, TInner, CallOptions, CancellationToken)"/>
/// authenticates a forward for the gateway operation being served (D7) and translates the upstream problems it names (D8),
/// from the sample gateway's own operations.
/// </summary>
public sealed class BlueprintHttpClientContextForwardTests : IAsyncLifetime
{
    private const string TemplateJson = """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""";

    private readonly GatewayContextFactory _contexts = new();

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await this._contexts.DisposeAsync();
    }

    [Fact]
    public async Task By_default_the_callers_Authorization_header_is_forwarded_over_one_ModifyRequest_set()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            this._contexts.For<GetGatewayTemplateQuery>(callerAuthorization: "Bearer caller-token"),
            new GetTemplateQuery { Id = "t-1" },
            new CallOptions { ModifyRequest = request => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "from-options") });

        sent!.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "caller-token"));
    }

    [Fact]
    public async Task By_default_a_caller_with_no_Authorization_header_forwards_none()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            this._contexts.For<GetGatewayTemplateQuery>(callerAuthorization: null),
            new GetTemplateQuery { Id = "t-1" });

        sent!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task An_UpstreamCredential_strategy_replaces_the_callers_Authorization_header()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            this._contexts.For<GetServiceTemplateQuery>(callerAuthorization: "Bearer caller-token"),
            new GetTemplateQuery { Id = "t-1" });

        sent!.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", GatewayContextFactory.ServiceToken));
    }

    [Fact]
    public async Task An_UpstreamCredential_strategy_that_is_not_registered_fails_naming_it_and_sends_nothing()
    {
        var sent = false;
        var client = CreateClient(_ =>
        {
            sent = true;

            return Json(HttpStatusCode.OK, TemplateJson);
        });
        await using var unregistered = new ServiceCollection().BuildServiceProvider();
        var context = this._contexts.For<GetServiceTemplateQuery>(callerAuthorization: "Bearer caller-token");
        context.ServiceProvider = unregistered;

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            context,
            new GetTemplateQuery { Id = "t-1" });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(nameof(ServiceTokenCredentialStrategy));
        sent.Should().BeFalse();
    }

    [Fact]
    public async Task An_upstream_problem_named_by_TranslateError_is_raised_as_the_local_factorys_problem()
    {
        var client = CreateClient(_ => Problem(HttpStatusCode.NotFound, "not_found", "Template 't-1' not found."));

        var act = () => client.ForwardAsync<UpdateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            this._contexts.For<RenameGatewayTemplateCommand>(callerAuthorization: "Bearer caller-token"),
            new UpdateTemplateCommand { Id = "t-1", Name = "Renamed", HtmlSource = "<p>r</p>" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Should().NotBeOfType<BlueprintApiException>();
        exception.Type.Should().Be("gateway_template_missing");
        exception.Title.Should().Be("Gateway template missing");
        exception.HttpStatus.Should().Be(404);
        exception.Detail.Should().Be("Template 't-1' not found.");
        exception.InnerException.Should().BeOfType<BlueprintApiException>().Which.Type.Should().Be("not_found");
    }

    [Fact]
    public async Task An_upstream_problem_TranslateError_does_not_name_escapes_unchanged()
    {
        var client = CreateClient(_ => Problem(HttpStatusCode.Conflict, "template_conflict", "Modified concurrently."));

        var act = () => client.ForwardAsync<UpdateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            this._contexts.For<RenameGatewayTemplateCommand>(callerAuthorization: "Bearer caller-token"),
            new UpdateTemplateCommand { Id = "t-1", Name = "Renamed", HtmlSource = "<p>r</p>" });

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.Type.Should().Be("template_conflict");
    }

    private static BlueprintHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new BlueprintHttpClient(new System.Net.Http.HttpClient(new FakeHttpHandler(handler))
        {
            BaseAddress = new Uri("https://upstream.example/api/"),
        });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{"type":"{{type}}","title":"Upstream title","status":{{(int)status}},"detail":"{{detail}}"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
    }
}
