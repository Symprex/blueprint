using System.Net;
using System.Text;
using Blueprint.Http;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The transport's streaming surface (D18): a forward declaring <see cref="ProxyResult"/> hands the upstream response over
/// unread, and <see cref="BlueprintHttpClient.SendRawAsync"/> sends a request built by
/// <see cref="BlueprintHttpClient.CreateRequest{TOperation}"/> and returns its response at the headers. A failure is mapped,
/// and its response disposed, before anything could be streamed.
/// </summary>
public class BlueprintHttpClientStreamingTests
{
    [Fact]
    public async Task When_forwarding_as_a_ProxyResult_then_the_upstream_response_is_handed_over_undisposed()
    {
        var upstream = File(HttpStatusCode.OK, "file body");
        var client = CreateClient(_ => upstream);

        var result = await client.ForwardAsync<DownloadTemplateQuery, ProxyResult>(new DownloadTemplateQuery { Id = "t-1" });

        result.DownstreamResponse.Should().BeSameAs(upstream);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        ((TrackedContent)upstream.Content).IsDisposed.Should().BeFalse();
        (await upstream.Content.ReadAsStringAsync()).Should().Be("file body");
    }

    [Fact]
    public async Task When_forwarding_as_a_ProxyResult_and_the_upstream_fails_then_the_problem_escapes_and_the_response_is_disposed()
    {
        var upstream = Problem(HttpStatusCode.NotFound, """{"type":"not_found","title":"Not found","detail":"Template 'x' not found.","status":404}""");
        var client = CreateClient(_ => upstream);

        var act = () => client.ForwardAsync<DownloadTemplateQuery, ProxyResult>(new DownloadTemplateQuery { Id = "x" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Detail.Should().Be("Template 'x' not found.");
        ((TrackedContent)upstream.Content).IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task When_sending_a_built_request_raw_then_the_success_response_is_returned_undisposed()
    {
        HttpRequestMessage? sent = null;
        var upstream = File(HttpStatusCode.OK, "raw body");
        var client = CreateClient(request =>
        {
            sent = request;

            return upstream;
        });

        using var request = client.CreateRequest(HttpMethod.Get, "/templates/t-1/download", new DownloadTemplateQuery { Id = "t-1" });
        request.Headers.Add("X-Trace", "abc");
        using var response = await client.SendRawAsync(request);

        sent!.RequestUri!.AbsoluteUri.Should().Be("https://upstream.example/api/templates/t-1/download");
        sent.Headers.GetValues("X-Trace").Should().Equal("abc");
        sent.Content.Should().BeNull("a GET carries its properties in its URL");
        response.Should().BeSameAs(upstream);
        (await response.Content.ReadAsStringAsync()).Should().Be("raw body");
    }

    [Fact]
    public async Task When_sending_raw_and_the_upstream_fails_then_the_problem_escapes_and_the_response_is_disposed()
    {
        var upstream = Problem(HttpStatusCode.Conflict, """{"type":"conflict","title":"Conflict","detail":"Busy.","status":409}""");
        var client = CreateClient(_ => upstream);

        using var request = client.CreateRequest(HttpMethod.Get, "templates/t-1/download", new DownloadTemplateQuery { Id = "t-1" });
        var act = () => client.SendRawAsync(request);

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.Conflict);
        exception.Type.Should().Be("conflict");
        ((TrackedContent)upstream.Content).IsDisposed.Should().BeTrue();
    }

    private static BlueprintHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new BlueprintHttpClient(new System.Net.Http.HttpClient(new FakeHttpHandler(handler))
        {
            BaseAddress = new Uri("https://upstream.example/api/"),
        });
    }

    private static HttpResponseMessage File(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status) { Content = new TrackedContent(body, "application/octet-stream") };
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new TrackedContent(json, "application/problem+json") };
    }

    /// <summary>
    /// A response body that records whether it was disposed, as disposing a response disposes its content.
    /// </summary>
    private sealed class TrackedContent(string body, string mediaType) : StringContent(body, Encoding.UTF8, mediaType)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
