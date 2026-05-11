using System.Net;
using System.Text;
using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Tests that <see cref="BlueprintHttpClient{TApp}"/> correctly resolves URLs and HTTP methods
/// from operation descriptors, serializes operation bodies, and handles response status codes
/// (200, 204, 404, 422) into the expected types or exceptions.
/// </summary>
public sealed class BlueprintHttpClientTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task SendAsync_get_query_sends_get_request_to_correct_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[],"totalItems":0}""", Encoding.UTF8, "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);

        await client.SendAsync<JsonElement>(new SearchTemplatesQuery { Q = "hello", Page = 1, PageSize = 10 });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates/search");
        captured.RequestUri.Query.Should().Contain("Q=hello", "query string properties should be appended for GET");
        captured.RequestUri.Query.Should().Contain("Page=1");
        captured.RequestUri.Query.Should().Contain("PageSize=10");
    }

    [Fact]
    public async Task SendAsync_post_command_sends_post_with_json_body()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":"t-1","name":"My Template","htmlSource":"<p>Hi</p>","plainTextSource":"Hi","ownerId":"owner-1"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);

        var result = await client.SendAsync<JsonElement>(
            new CreateTemplateCommand { Name = "My Template", HtmlSource = "<p>Hi</p>", PlainTextSource = "Hi" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates");
        captured.Content.Should().NotBeNull("POST commands should have a JSON body");

        result.GetProperty("name").GetString().Should().Be("My Template");
    }

    [Fact]
    public async Task SendAsync_put_command_sends_put_to_url_with_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"t-42","name":"Updated","htmlSource":"<p>u</p>","plainTextSource":"u","ownerId":"owner-1"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);

        await client.SendAsync<JsonElement>(
            new UpdateTemplateCommand { Id = "t-42", Name = "Updated", HtmlSource = "<p>u</p>", PlainTextSource = "u" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Put);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates/t-42");
    }

    [Fact]
    public async Task SendAsync_delete_command_sends_delete_to_url_with_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.SendAsync(new ArchiveTemplateCommand { Id = "t-99" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates/t-99");
    }

    [Fact]
    public async Task SendAsync_204_no_content_returns_default()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = this._fixture.CreateClient(handler);

        var result = await client.SendAsync<TemplateApiResource>(new ArchiveTemplateCommand { Id = "t-1" });

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_422_throws_validation_exception_with_errors()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(
                """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.22","title":"Validation Failed","status":422,"errors":{"Name":["Name is required."]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        });

        var client = this._fixture.CreateClient(handler);

        var act = () => client.SendAsync<TemplateApiResource>(
            new CreateTemplateCommand { HtmlSource = "<p>x</p>" });

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task SendAsync_404_throws_blueprint_api_exception()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found", Encoding.UTF8, "text/plain"),
        });

        var client = this._fixture.CreateClient(handler);

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "missing" });

        var ex = await act.Should().ThrowAsync<BlueprintApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SendAsync_500_throws_blueprint_api_exception()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error", Encoding.UTF8, "text/plain"),
        });

        var client = this._fixture.CreateClient(handler);

        var act = () => client.SendAsync<object>(new SearchTemplatesQuery { Q = "x", Page = 1, PageSize = 5 });

        var ex = await act.Should().ThrowAsync<BlueprintApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SendAsync_void_with_success_does_not_throw()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = this._fixture.CreateClient(handler);

        var act = () => client.SendAsync(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_void_with_error_throws_api_exception()
    {
        var handler = new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("Forbidden", Encoding.UTF8, "text/plain"),
        });

        var client = this._fixture.CreateClient(handler);

        var act = () => client.SendAsync(new ArchiveTemplateCommand { Id = "t-1" });

        var ex = await act.Should().ThrowAsync<BlueprintApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Constructor_throws_when_httpClient_is_null()
    {
        var act = () => new BlueprintHttpClient<SampleBlueprintApp>(null!, this._fixture.App);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_throws_when_app_is_null()
    {
        var act = () => new BlueprintHttpClient<SampleBlueprintApp>(new System.Net.Http.HttpClient(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_catch_all_route_preserves_slashes_in_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"content":"ok"}""", Encoding.UTF8, "application/json"),
            };
        });

        var client = this._fixture.CreateClient(handler);

        await client.SendAsync<JsonElement>(new GetFolderContentsQuery { FolderPath = "UK/Marketing/Archive" });

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain(
            "UK/Marketing/Archive",
            "catch-all route parameter should NOT URL-encode slashes");
        captured.RequestUri.PathAndQuery.Should().NotContain(
            "%2F",
            "slashes in catch-all parameters must be preserved, not percent-encoded");
    }
}
