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
