using System.Net;
using System.Text;
using Blueprint.SourceGenerator.Sample.Gateway.Api;
using Blueprint.SourceGenerator.Sample.Gateway.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Integration tests proving that gateway handlers correctly delegate operations
/// to the upstream service via <see cref="BlueprintHttpClient{TApp}"/> and map
/// upstream responses to gateway resource types.
/// </summary>
public sealed class GatewayHandlerIntegrationTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public GatewayHandlerIntegrationTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task ListGatewayTemplatesQueryHandler_returns_mapped_templates_from_upstream()
    {
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"t-1","name":"Template A","htmlSource":"<p>A</p>","plainTextSource":"A","isArchived":false},{"id":"t-2","name":"Template B","htmlSource":"<p>B</p>","plainTextSource":"B","isArchived":true}]""",
                    Encoding.UTF8,
                    "application/json"),
            });

        var client = this._fixture.CreateClient(handler);
        var sut = new ListGatewayTemplatesQueryHandler(client);

        var result = await sut.Handle(new ListGatewayTemplatesQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be("t-1");
        result[0].Name.Should().Be("Template A");
        result[0].IsArchived.Should().BeFalse();
        result[1].Id.Should().Be("t-2");
        result[1].Name.Should().Be("Template B");
        result[1].IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task ListGatewayTemplatesQueryHandler_throws_response_empty_when_upstream_returns_json_null()
    {
        // A JSON null is legacy's response_empty ApiException (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:21-24,
        // :203-208), not a null the handler could turn into an empty list. A 204 is no TemplateApiResource[] either: the
        // client throws for it, as legacy's did.
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });

        var client = this._fixture.CreateClient(handler);
        var sut = new ListGatewayTemplatesQueryHandler(client);

        var act = () => sut.Handle(new ListGatewayTemplatesQuery(), CancellationToken.None);

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.Type.Should().Be("response_empty");
    }

    [Fact]
    public async Task ListGatewayTemplatesQueryHandler_sends_GET_to_templates_endpoint()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);
        var sut = new ListGatewayTemplatesQueryHandler(client);

        await sut.Handle(new ListGatewayTemplatesQuery(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates");
    }

    [Fact]
    public async Task GetGatewayTemplateQueryHandler_returns_ok_result_when_upstream_succeeds()
    {
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"t-42","name":"Found Template","htmlSource":"<p>found</p>","plainTextSource":"found","isArchived":false}""",
                    Encoding.UTF8,
                    "application/json"),
            });

        var client = this._fixture.CreateClient(handler);
        var sut = new GetGatewayTemplateQueryHandler(client);

        var result = await sut.Handle(
            new GetGatewayTemplateQuery { Id = "t-42" },
            CancellationToken.None);

        result.IsT1.Should().BeTrue("the upstream returned a valid template");
        var ok = result.AsT1;
        ok.Content.Id.Should().Be("t-42");
        ok.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task GetGatewayTemplateQueryHandler_returns_not_found_when_upstream_returns_404()
    {
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"status":404,"title":"Not Found"}""",
                    Encoding.UTF8,
                    "application/problem+json"),
            });

        var client = this._fixture.CreateClient(handler);
        var sut = new GetGatewayTemplateQueryHandler(client);

        var result = await sut.Handle(
            new GetGatewayTemplateQuery { Id = "missing" },
            CancellationToken.None);

        result.IsT2.Should().BeTrue("the upstream returned 404");
    }

    [Fact]
    public async Task GetGatewayTemplateQueryHandler_sends_GET_with_id_in_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"t-99","name":"Test","htmlSource":"<p>test</p>","plainTextSource":"test","isArchived":false}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);
        var sut = new GetGatewayTemplateQueryHandler(client);

        await sut.Handle(new GetGatewayTemplateQuery { Id = "t-99" }, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain("t-99");
    }

    [Fact]
    public async Task CreateGatewayTemplateCommandHandler_sends_POST_and_maps_response()
    {
        HttpMethod? capturedMethod = null;
        string? capturedBody = null;
        var handler = new FakeHttpHandler(req =>
        {
            capturedMethod = req.Method;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":"new-1","name":"Created Template","htmlSource":"<p>Hello</p>","plainTextSource":"Hello","isArchived":false}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);
        var sut = new CreateGatewayTemplateCommandHandler(client);

        var result = await sut.Handle(
            new CreateGatewayTemplateCommand
            {
                Name = "Created Template",
                HtmlSource = "<p>Hello</p>",
                PlainTextSource = "Hello",
            },
            CancellationToken.None);

        result.Id.Should().Be("new-1");
        result.Name.Should().Be("Created Template");
        result.IsArchived.Should().BeFalse();

        capturedMethod.Should().Be(HttpMethod.Post);
        capturedBody.Should().NotBeNull("POST commands should have a JSON body");
        capturedBody.Should().Contain("Created Template");
        capturedBody.Should().Contain("<p>Hello</p>");
    }

    [Fact]
    public async Task CreateGatewayTemplateCommandHandler_throws_on_upstream_validation_error()
    {
        var handler = new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    """{"errors":{"Name":["'Name' must not be empty."]},"type":"https://httpstatuses.com/422","title":"Validation Failed","status":422}""",
                    Encoding.UTF8,
                    "application/problem+json"),
            });

        var client = this._fixture.CreateClient(handler);
        var sut = new CreateGatewayTemplateCommandHandler(client);

        var act = () => sut.Handle(
            new CreateGatewayTemplateCommand
            {
                Name = string.Empty,
                HtmlSource = string.Empty,
                PlainTextSource = string.Empty,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<Blueprint.Validation.ValidationException>();
    }
}
