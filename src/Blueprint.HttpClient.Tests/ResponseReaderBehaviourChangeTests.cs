using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The behaviour changes #60 made deliberately when it rebuilt <see cref="BlueprintHttpClient{TApp}"/> on the shared
/// transport and readers, none of which the suite exercised before: an abstract resource event is refused before a request
/// is sent, a failure while a resource-event body is read is mapped as a failure to send is, and a <c>Results&lt;A, A&gt;</c>
/// whose success is not JSON is its first member.
/// </summary>
public sealed class ResponseReaderBehaviourChangeTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _fixture;

    public ResponseReaderBehaviourChangeTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task An_abstract_resource_event_is_refused_before_a_request_is_sent()
    {
        var sent = 0;
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var act = () => client.SendAsync<AuditedTemplateEvent>(new ArchiveTemplateCommand { Id = "t-1" });

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(nameof(AuditedTemplateEvent));
        sent.Should().Be(0);
    }

    [Fact]
    public async Task A_union_with_an_abstract_resource_event_member_is_refused_before_a_request_is_sent()
    {
        // The union's other member could otherwise read the response, and the contract would never be refused at all.
        var sent = 0;
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        var act = () => client.SendAsync<Results<AuditedTemplateEvent, StatusCodeResult.Accepted>>(new ArchiveTemplateCommand { Id = "t-1" });

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(nameof(AuditedTemplateEvent));
        sent.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_timeout_while_a_resource_event_body_is_read_is_service_timeout(bool envelope)
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException());

        var act = () => SendResourceEvent(this.CreateUnbufferedClient(_ => FailingBody(timeout)), envelope, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;

        exception.Type.Should().Be("service_timeout");
        exception.HttpStatus.Should().Be(408);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lost_connection_while_a_resource_event_body_is_read_is_service_unavailable(bool envelope)
    {
        var lost = new HttpRequestException("The response ended prematurely.");

        var act = () => SendResourceEvent(this.CreateUnbufferedClient(_ => FailingBody(lost)), envelope, CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;

        exception.Type.Should().Be("service_unavailable");
        exception.InnerException.Should().BeSameAs(lost);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_callers_cancellation_while_a_resource_event_body_is_read_propagates_unchanged(bool envelope)
    {
        using var cancellation = new CancellationTokenSource();
        var client = this.CreateUnbufferedClient(_ =>
        {
            // The response is back; the caller cancels before its body is read.
            cancellation.Cancel();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"$object":"event","eventId":"templates.archived","changeType":"updated","resourceObject":"template","data":{"$object":"template","id":"t-1"}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var act = () => SendResourceEvent(client, envelope, cancellation.Token);

        var exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;

        exception.Should().NotBeAssignableTo<ApiException>();
        exception.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task A_bodiless_202_read_as_a_union_of_two_identical_members_is_the_first()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, StatusCodeResult.Accepted>>(new ArchiveTemplateCommand { Id = "t-1" });

        result!.IsT1.Should().BeTrue();
        result.IsT2.Should().BeFalse();
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task A_text_response_read_as_a_union_of_two_identical_members_is_the_first()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("pong", Encoding.UTF8, "text/plain"),
        }));

        var result = await client.SendAsync<Results<PlainTextResult, PlainTextResult>>(new HealthCheckQuery());

        result!.IsT1.Should().BeTrue();
        result.IsT2.Should().BeFalse();
        result.AsT1.Content.Should().Be("pong");
    }

    private static Task SendResourceEvent(BlueprintHttpClient<Blueprint.SourceGenerator.Sample.SampleBlueprintApp> client, bool envelope, CancellationToken cancellationToken)
    {
        var command = new ArchiveTemplateCommand { Id = "t-1" };

        return envelope
            ? client.SendResourceEventEnvelopeAsync<TemplateApiResource>(command, cancellationToken)
            : client.SendResourceEventAsync<TemplateApiResource>(command, cancellationToken);
    }

    private static HttpResponseMessage FailingBody(Exception failure)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingContent(failure) };
    }

    /// <summary>
    /// A client that hands the response back as soon as its headers are read, so that its body is read by the reader, as
    /// an <see cref="System.Net.Http.HttpClient"/> sending with <see cref="HttpCompletionOption.ResponseHeadersRead"/> does.
    /// </summary>
    private BlueprintHttpClient<Blueprint.SourceGenerator.Sample.SampleBlueprintApp> CreateUnbufferedClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new BlueprintHttpClient<Blueprint.SourceGenerator.Sample.SampleBlueprintApp>(
            new HeadersReadHttpClient(new FakeHttpHandler(handler)) { BaseAddress = new Uri("https://localhost") },
            this._fixture.App);
    }

    /// <summary>
    /// A resource event no response can be read as, because it cannot be created.
    /// </summary>
    public abstract class AuditedTemplateEvent : ResourceEvent<TemplateApiResource>
    {
        protected AuditedTemplateEvent(ResourceEventChangeType changeType, string eventId, TemplateApiResource data)
            : base(changeType, eventId, data)
        {
        }
    }

    private sealed class HeadersReadHttpClient(HttpMessageHandler handler) : System.Net.Http.HttpClient(handler)
    {
        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return this.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }

    /// <summary>
    /// A JSON body that fails with a given exception as it is read.
    /// </summary>
    private sealed class FailingContent : HttpContent
    {
        private readonly Exception _failure;

        public FailingContent(Exception failure)
        {
            this._failure = failure;
            this.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return Task.FromException(this._failure);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromException<Stream>(this._failure);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
