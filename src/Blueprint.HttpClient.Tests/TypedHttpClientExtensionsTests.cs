using System.Net;
using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Tests the source-generated typed HTTP client extension methods for <see cref="SampleBlueprintApp"/>
/// so runtime request construction stays aligned with the generated signatures and descriptor metadata.
/// </summary>
public sealed class TypedHttpClientExtensionsTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public TypedHttpClientExtensionsTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task SearchTemplatesAsync_get_query_sends_get_request_to_correct_url()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.SearchTemplatesAsync(maxResults: 50, minResults: 5, page: 2, pageSize: 10, q: "hello", sortBy: TemplateSortOrder.Name);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Contain("/templates/search");
        captured.RequestUri.Query.Should().Contain("Q=hello");
        captured.RequestUri.Query.Should().Contain("Page=2");
        captured.RequestUri.Query.Should().Contain("PageSize=10");
        captured.RequestUri.Query.Should().Contain("MaxResults=50");
        captured.RequestUri.Query.Should().Contain("MinResults=5");
    }

    [Fact]
    public async Task CreateTemplateAsync_post_command_sends_post_with_json_body()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.CreateTemplateAsync(
            name: "My Template",
            priority: 42,
            htmlSource: "<p>Hi</p>",
            plainTextSource: "Hi",
            slug: "my-template",
            visibility: "public");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.PathAndQuery.Should().Be("/templates");
        captured.Content.Should().NotBeNull();
        capturedBody.Should().NotBeNull();

        using var document = JsonDocument.Parse(capturedBody!);
        var root = document.RootElement;
        root.GetProperty("name").GetString().Should().Be("My Template");
        root.GetProperty("priority").GetInt32().Should().Be(42);
        root.GetProperty("htmlSource").GetString().Should().Be("<p>Hi</p>");
        root.GetProperty("plainTextSource").GetString().Should().Be("Hi");
        root.GetProperty("slug").GetString().Should().Be("my-template");
        root.GetProperty("visibility").GetString().Should().Be("public");
    }

    [Fact]
    public async Task ArchiveTemplateAsync_delete_command_sends_delete_to_url_with_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.ArchiveTemplateAsync("t-99");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Delete);
        captured.RequestUri!.PathAndQuery.Should().Be("/templates/t-99");
    }

    [Fact]
    public async Task GetTemplateAsync_get_query_sends_get_request_to_url_with_id()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = this._fixture.CreateClient(handler);

        await client.GetTemplateAsync("t-42");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.PathAndQuery.Should().Be("/templates/t-42");
    }
}
