using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that <see cref="BlueprintHttpClient{TApp}"/> reads a response declared as a <see cref="Results{T1,T2}"/> into
/// the member the host wrote, rather than deserialising every body as JSON: a status-only result by its status and
/// empty body, a <see cref="PlainTextResult"/> by its text content type, and any other member from JSON.
/// </summary>
/// <remarks>
/// The fake responses are shaped as <c>HttpOperationResultExecutor</c> writes them: a <see cref="StatusCodeResult"/>
/// sets only the status, and a <see cref="PlainTextResult"/> is written as <c>text/plain; charset=utf-8</c>.
/// </remarks>
public class BlueprintHttpClientUnionResultTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientUnionResultTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task When_the_host_wrote_the_status_only_member_then_returns_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task When_the_host_wrote_the_plain_text_member_then_returns_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("The return content", Encoding.UTF8, "text/plain"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Should().Be("The return content");
        result.AsT2.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task When_the_host_wrote_plain_text_with_the_status_only_members_status_then_returns_the_text()
    {
        // A status-only result has no body, so a 202 that has one is the other member.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("Queued", Encoding.UTF8, "text/plain"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Should().Be("Queued");
    }

    [Fact]
    public async Task When_the_host_wrote_a_json_member_then_deserialises_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""",
                Encoding.UTF8,
                "application/json"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, TemplateApiResource>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_the_host_wrote_no_content_that_no_member_claims_then_returns_the_default()
    {
        // What SendAsync returns for a 204 whatever the response type, as a generated typed extension relies on.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeFalse();
        result.IsT2.Should().BeFalse();
    }

    [Fact]
    public async Task When_the_response_matches_no_member_then_throws()
    {
        // 200 with a JSON body is neither the 202 nor text: returning a default union would hide a contract mismatch.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", new MediaTypeHeaderValue("application/json")),
        }));

        var act = () => client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*matches none of*");
    }
}
