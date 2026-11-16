using System.Net;
using System.Text;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// An operation whose host reads a stream or a multipart body (#71, D19) is sent as one by every client: a <c>[FromBody]</c>
/// <see cref="Stream"/> as the request's content itself, and an operation with an <see cref="IFormFile"/> as
/// <c>multipart/form-data</c>, its files streamed as parts and its other body properties as fields. Neither is serialised
/// as JSON.
/// </summary>
public class BlueprintHttpClientStreamingRequestTests
{
    private const string UploadJson = """{"templateId":"t-1","length":5,"sha256":"X"}""";

    [Fact]
    public async Task A_typed_client_sends_a_file_operation_as_multipart_form_data()
    {
        CapturedRequest? sent = null;
        var api = new SampleContractApi(CreateClient(request => sent = Capture(request)));

        await api.AttachTemplateFileAsync(new AttachTemplateFileCommand
        {
            Id = "t-1",
            Description = "Quarterly figures",
            File = CreateFile("report.pdf", "application/pdf", "%PDF-1.7 figures"),
        });

        sent!.Method.Should().Be(HttpMethod.Post);
        sent.Url.Should().Be("https://upstream.example/api/templates/t-1/attachments");
        sent.MediaType.Should().Be("multipart/form-data");
        sent.Parts.Should().BeEquivalentTo(
            [
                new Part("description", null, "text/plain", "Quarterly figures"),
                new Part("file", "report.pdf", "application/pdf", "%PDF-1.7 figures"),
            ],
            static options => options.WithoutStrictOrdering());
    }

    [Fact]
    public async Task A_file_operation_leaves_out_a_part_whose_property_is_unset()
    {
        CapturedRequest? sent = null;
        var api = new SampleContractApi(CreateClient(request => sent = Capture(request)));

        await api.AttachTemplateFileAsync(new AttachTemplateFileCommand { Id = "t-1", File = CreateFile("a.txt", "text/plain", "a") });

        sent!.Parts.Select(static p => p.Name).Should().Equal("file");
    }

    [Fact]
    public async Task The_app_client_sends_a_file_operation_as_multipart_form_data()
    {
        CapturedRequest? sent = null;
        var client = new BlueprintHttpClient<SampleBlueprintApp>(CreateHttpClient(request => sent = Capture(request)), new SampleBlueprintApp());

        await client.SendAsync(new AttachTemplateFileCommand { Id = "t-1", File = CreateFile("a.txt", "text/plain", "hello") });

        sent!.MediaType.Should().Be("multipart/form-data");
        sent.Parts.Should().ContainSingle().Which.Should().Be(new Part("file", "a.txt", "text/plain", "hello"));
    }

    [Fact]
    public async Task A_typed_client_sends_a_stream_operation_with_the_stream_as_its_body()
    {
        CapturedRequest? sent = null;
        var api = new SampleContractApi(CreateClient(request => sent = Capture(request)));

        await api.ImportTemplateSourceAsync(new ImportTemplateSourceCommand
        {
            Id = "t-1",
            Content = new NonSeekableStream(Encoding.UTF8.GetBytes("<p>imported</p>")),
        });

        sent!.Method.Should().Be(HttpMethod.Put);
        sent.Url.Should().Be("https://upstream.example/api/templates/t-1/source");
        sent.MediaType.Should().Be("application/octet-stream");
        sent.Body.Should().Be("<p>imported</p>");
        sent.ContentLength.Should().BeNull("a stream of unknown length is sent as it is read, not measured first");
    }

    [Fact]
    public async Task A_forward_sends_a_stream_operation_with_the_stream_as_its_body()
    {
        CapturedRequest? sent = null;
        var client = CreateClient(request => sent = Capture(request));

        await client.ForwardAsync<ImportTemplateSourceCommand, TemplateUploadApiResource>(new ImportTemplateSourceCommand
        {
            Id = "t-1",
            Content = new NonSeekableStream(Encoding.UTF8.GetBytes("forwarded")),
        });

        sent!.MediaType.Should().Be("application/octet-stream");
        sent.Body.Should().Be("forwarded");
    }

    [Fact]
    public async Task A_forward_sends_a_file_operation_as_multipart_form_data()
    {
        CapturedRequest? sent = null;
        var client = CreateClient(request => sent = Capture(request));

        await client.ForwardAsync<AttachTemplateFileCommand, TemplateUploadApiResource>(new AttachTemplateFileCommand
        {
            Id = "t-1",
            Description = "d",
            File = CreateFile("a.bin", "application/octet-stream", "bytes"),
        });

        sent!.MediaType.Should().Be("multipart/form-data");
        sent.Parts.Should().BeEquivalentTo(
            [new Part("description", null, "text/plain", "d"), new Part("file", "a.bin", "application/octet-stream", "bytes")],
            static options => options.WithoutStrictOrdering());
    }

    [Fact]
    public async Task A_derived_operation_that_hides_its_bases_stream_with_text_is_sent_as_JSON()
    {
        // It inherited the base's generated body builder, which used to send the base's (empty) stream instead.
        CapturedRequest? sent = null;
        var client = CreateClient(request => sent = Capture(request));

        await client.ForwardAsync<GatewayImportTemplateSourceTextCommand, TemplateUploadApiResource>(
            new GatewayImportTemplateSourceTextCommand { Id = "t-1", Content = "<p>text</p>" });

        sent!.MediaType.Should().Be("application/json");
        sent.Body.Should().Contain("\"content\":\"<p>text</p>\"");
    }

    private static BlueprintHttpClient CreateClient(Action<HttpRequestMessage> onRequest)
    {
        return new BlueprintHttpClient(CreateHttpClient(onRequest));
    }

    private static System.Net.Http.HttpClient CreateHttpClient(Action<HttpRequestMessage> onRequest)
    {
        return new System.Net.Http.HttpClient(new FakeHttpHandler(request =>
        {
            onRequest(request);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UploadJson, Encoding.UTF8, "application/json") };
        }))
        {
            BaseAddress = new Uri("https://upstream.example/api/"),
        };
    }

    private static FormFile CreateFile(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static CapturedRequest Capture(HttpRequestMessage request)
    {
        var content = request.Content!;
        var parts = new List<Part>();

        if (content is MultipartFormDataContent form)
        {
            foreach (var part in form)
            {
                parts.Add(new Part(
                    part.Headers.ContentDisposition!.Name!.Trim('"'),
                    part.Headers.ContentDisposition.FileName?.Trim('"'),
                    part.Headers.ContentType?.MediaType,
                    part.ReadAsStringAsync().GetAwaiter().GetResult()));
            }
        }

        var contentLength = content.Headers.ContentLength;
        var body = content is MultipartFormDataContent ? null : content.ReadAsStringAsync().GetAwaiter().GetResult();

        return new CapturedRequest(request.Method, request.RequestUri!.AbsoluteUri, content.Headers.ContentType?.MediaType, contentLength, body, parts);
    }

    private sealed record CapturedRequest(HttpMethod Method, string Url, string? MediaType, long? ContentLength, string? Body, List<Part> Parts);

    private sealed record Part(string Name, string? FileName, string? MediaType, string Content);

    /// <summary>A stream that cannot seek, so nothing can learn its length without reading it, as a request body.</summary>
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
