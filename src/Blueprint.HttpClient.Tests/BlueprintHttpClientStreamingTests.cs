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

    [Theory]
    [InlineData("forward")]
    [InlineData("raw")]
    public async Task When_a_streamed_failures_body_stalls_then_its_read_times_out_within_the_clients_timeout(string send)
    {
        // A streamed send returns at the headers, outside HttpClient.Timeout, so a failure's body that never finished used to
        // be waited on for as long as the caller's token allowed, which was forever without one.
        var stalled = new StalledContent();
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = stalled }, TimeSpan.FromMilliseconds(200));
        using var request = client.CreateRequest(HttpMethod.Get, "templates/t-1/download", new DownloadTemplateQuery { Id = "t-1" });

        Func<Task> act = send == "forward"
            ? () => client.ForwardAsync<DownloadTemplateQuery, ProxyResult>(new DownloadTemplateQuery { Id = "t-1" })
            : () => client.SendRawAsync(request);

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>().WaitAsync(TimeSpan.FromSeconds(10))).Which;
        exception.Type.Should().Be("service_timeout");
        exception.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);
        stalled.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task When_a_streamed_success_is_read_after_the_clients_timeout_then_it_is_not_cut_off()
    {
        // The bound is on a failure's body only: a streamed success is read for as long as its reader takes.
        var upstream = File(HttpStatusCode.OK, "slow reader");
        var client = CreateClient(_ => upstream, TimeSpan.FromMilliseconds(100));

        using var response = await client.SendRawAsync(
            client.CreateRequest(HttpMethod.Get, "templates/t-1/download", new DownloadTemplateQuery { Id = "t-1" }));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        (await response.Content.ReadAsStringAsync()).Should().Be("slow reader");
    }

    private static BlueprintHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler, TimeSpan? timeout = null)
    {
        var httpClient = new System.Net.Http.HttpClient(new FakeHttpHandler(handler))
        {
            BaseAddress = new Uri("https://upstream.example/api/"),
        };

        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new BlueprintHttpClient(httpClient);
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

    /// <summary>
    /// A problem body whose headers arrived but whose bytes never do, until the read is cancelled.
    /// </summary>
    private sealed class StalledContent : HttpContent
    {
        public StalledContent()
        {
            this.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/problem+json");
        }

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return this.SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
