using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
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
    private const string TemplateJson = """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""";

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
    public async Task When_the_host_wrote_no_content_that_no_member_claims_then_throws()
    {
        // A 204 neither member claims is a contract mismatch like any other response none claims (below): a default union
        // holds no member at all, so every IsT* is false and every AsT* throws, far from the call that went wrong. Legacy's
        // client, which had no unions, threw for any response it could not understand (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:228).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var act = () => client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*204*matches none of*");
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

    [Fact]
    public async Task When_the_host_wrote_a_resource_event_member_then_deserialises_it()
    {
        // The host writes a legacy resource event as a 200 JSON envelope (HttpOperationResultExecutor). Here it is an
        // OperationResult; legacy's was a plain object its client read from any JSON body (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:199-211).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"$object\":\"event\",\"eventId\":\"template.created\",\"changeType\":\"Created\",\"resourceObject\":\"template\",\"data\":" + TemplateJson + "}",
                new MediaTypeHeaderValue("application/json")),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, ResourceEvent<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.EventId.Should().Be("template.created");
        result.AsT2.Data!.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_the_host_wrote_a_status_only_response_for_the_base_status_code_member_then_returns_that_status()
    {
        // Legacy's client made a StatusCodeResult of the response's status for a response that was neither JSON nor text
        // (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:223-226). The base type is abstract here, so it is
        // the status's own result.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.StatusCode.Should().Be(202);
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task When_the_host_wrote_an_ok_member_then_deserialises_its_content()
    {
        // The host writes an OkResult<T> as its content, a 200 JSON body; the Sample's GetTemplateQuery is such a union.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TemplateJson, new MediaTypeHeaderValue("application/json")),
        }));

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_two_members_could_be_the_json_the_host_wrote_then_throws()
    {
        // Both members are written as a 200 JSON body, so the body cannot say which one the host wrote: reading it as the
        // first would silently hide a contract no response can satisfy.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TemplateJson, new MediaTypeHeaderValue("application/json")),
        }));

        var act = () => client.SendAsync<Results<TemplateApiResource, OkResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*more than one*");
    }

    [Fact]
    public async Task When_the_host_wrote_no_content_for_an_http_no_content_member_then_returns_it()
    {
        // The host writes an HttpNoContentResult as an empty 204 with no content type (HttpOperationResultExecutor); a union
        // may declare one (BP112 keeps it apart from NoResultOperationResult), so a 204 is that member, not an unmatched one.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, HttpNoContentResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Should().BeOfType<HttpNoContentResult>();
    }

    [Fact]
    public async Task When_the_host_wrote_an_empty_200_for_a_no_result_member_then_returns_it()
    {
        // The host writes a NoResultOperationResult as an empty 200 with no content type (HttpOperationResultExecutor).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await client.SendAsync<Results<NoResultOperationResult, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Should().BeSameAs(NoResultOperationResult.Instance);
    }
}