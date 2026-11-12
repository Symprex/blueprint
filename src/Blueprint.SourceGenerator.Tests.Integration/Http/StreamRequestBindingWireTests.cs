using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves the two request bodies an operation can bind besides JSON (#71, D19), through the real router on Kestrel: a
/// body-owned <see cref="Stream"/> property is handed the request body itself, and an operation with an
/// <see cref="IFormFile"/> property is bound from a <c>multipart/form-data</c> body, its files by name and its other body
/// properties from the form's fields. Both are documented as their media type's <c>requestBody</c>.
/// </summary>
public sealed class StreamRequestBindingWireTests
{
    [Fact]
    public async Task A_stream_property_is_handed_the_request_body_alongside_its_route_and_query_properties()
    {
        await using var host = await StreamRequestHost.StartAsync();
        var payload = RandomNumberGenerator.GetBytes(3 * 1024 * 1024);

        using var request = new HttpRequestMessage(HttpMethod.Post, "stream-request/imports/folder-1?label=nightly")
        {
            Content = new UnknownLengthContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var (status, body) = await host.SendAsync(request);

        status.Should().Be(200);
        body.Should().Be($"\"folder=folder-1;label=nightly;bytes={payload.Length};sha256={Sha256(payload)}\"");
    }

    [Fact]
    public async Task A_stream_property_reads_a_body_of_any_content_type()
    {
        await using var host = await StreamRequestHost.StartAsync();
        var payload = Encoding.UTF8.GetBytes("{\"not\":\"parsed\"}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "stream-request/imports/folder-1")
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var (status, body) = await host.SendAsync(request);

        status.Should().Be(200);
        body.Should().Be($"\"folder=folder-1;label=;bytes={payload.Length};sha256={Sha256(payload)}\"");
    }

    [Fact]
    public async Task A_multipart_body_binds_files_by_name_and_other_properties_from_its_fields()
    {
        await using var host = await StreamRequestHost.StartAsync();
        var file = RandomNumberGenerator.GetBytes(200 * 1024);
        var thumbnail = Encoding.UTF8.GetBytes("thumb");

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Quarterly figures"), "description" },
            { new StringContent("7"), "priority" },
            { new StringContent("finance"), "tags" },
            { new StringContent("q3"), "tags" },
            { FileContent(file, "application/pdf"), "file", "report.pdf" },
            { FileContent(thumbnail, "image/png"), "thumbnail", "report.png" },
        };

        var (status, body) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments") { Content = form });

        status.Should().Be(200);
        body.Should().Be(
            "\"folder=folder-1;description=Quarterly figures;priority=7;tags=finance,q3;"
            + $"file=report.pdf|application/pdf|{file.Length}|{Sha256(file)};thumbnail=report.png|image/png|5|{Sha256(thumbnail)}\"");
    }

    [Fact]
    public async Task A_multipart_body_leaves_a_file_or_field_it_does_not_carry_unset()
    {
        await using var host = await StreamRequestHost.StartAsync();
        var file = Encoding.UTF8.GetBytes("hello");

        using var form = new MultipartFormDataContent
        {
            { FileContent(file, "text/plain"), "file", "hello.txt" },
        };

        var (status, body) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments") { Content = form });

        status.Should().Be(200);
        body.Should().Be($"\"folder=folder-1;description=;priority=0;tags=;file=hello.txt|text/plain|5|{Sha256(file)};thumbnail=none\"");
    }

    [Fact]
    public async Task A_multipart_operation_refuses_a_JSON_body_with_415()
    {
        await using var host = await StreamRequestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments")
        {
            Content = new StringContent("""{"description":"x"}""", Encoding.UTF8, "application/json"),
        };

        var (status, _) = await host.SendAsync(request);

        status.Should().Be(415);
    }

    [Fact]
    public async Task A_multipart_field_that_does_not_parse_as_its_property_type_is_a_400()
    {
        await using var host = await StreamRequestHost.StartAsync();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("high"), "priority" },
            { FileContent("x"u8.ToArray(), "text/plain"), "file", "x.txt" },
        };

        var (status, body) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments") { Content = form });

        status.Should().Be(400);
        body.Should().Contain("priority");
    }

    [Fact]
    public async Task A_multipart_operation_refuses_a_chunked_body_with_no_content_type_with_415()
    {
        await using var host = await StreamRequestHost.StartAsync();

        var (status, _) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments")
        {
            Content = new UnknownLengthContent(Encoding.UTF8.GetBytes("not a form")),
        });

        status.Should().Be(415);
    }

    [Fact]
    public async Task A_multipart_operation_with_no_body_and_no_content_type_runs_with_its_form_properties_unset()
    {
        await using var host = await StreamRequestHost.StartAsync();

        var (status, body) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments")
        {
            Content = new UnknownLengthContent([]),
        });

        status.Should().Be(200);
        body.Should().Contain("file=none");
    }

    [Fact]
    public async Task A_multipart_body_that_ends_before_its_closing_boundary_is_a_400()
    {
        await using var host = await StreamRequestHost.StartAsync();
        var truncated = new ByteArrayContent(Encoding.UTF8.GetBytes("--b\r\nContent-Disposition: form-data; name=\"description\"\r\n\r\nx"));
        truncated.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=b");

        var (status, _) = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, "stream-request/folders/folder-1/attachments") { Content = truncated });

        status.Should().Be(400);
    }

    [Fact]
    public void A_stream_operation_is_sent_to_a_URL_that_carries_its_query_properties()
    {
        // Only a GET used to carry a query string, and a stream body has nowhere else to put them, so they were lost.
        var command = new StreamRequestImportCommand { FolderId = "folder-1", Label = "nightly", Limit = 5 };

        BuildRequestUrl(command).Should().Be("/stream-request/imports/folder-1?label=nightly&limit=5");
        StreamRequestImportCommand.Http.BuildUrl(command).Should().Be("/stream-request/imports/folder-1?label=nightly&limit=5");
    }

    [Fact]
    public void A_query_property_hiding_a_base_one_is_sent_once()
    {
        // The query inventory used to hold the hidden base property too, so each value was sent twice.
        var command = new StreamRequestHiddenTagsCommand { Tags = [1, 2] };

        BuildRequestUrl(command).Should().Be("/stream-request/tagged?tags=1&tags=2");
    }

    [Fact]
    public void A_decimal_query_value_is_sent_as_the_host_parses_it_whatever_the_senders_culture()
    {
        // The host parses query values with the invariant culture; ToString() under de-DE used to send 1,5, which binds as 15.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            BuildRequestUrl(new StreamRequestHiddenTagsCommand { Rate = 1.5m }).Should().Be("/stream-request/tagged?rate=1.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_stream_operation_with_no_query_values_is_sent_to_its_bare_URL()
    {
        BuildRequestUrl(new StreamRequestImportCommand { FolderId = "folder-1" }).Should().Be("/stream-request/imports/folder-1");
    }

    [Fact]
    public async Task A_form_whose_files_share_one_buffered_request_body_is_sent_with_every_file_intact()
    {
        // With FormOptions.BufferBody, each file reads its own range of one shared stream; opening every file while the form
        // was built used to move that stream under the first part, which then refused to be sent.
        var bytes = Encoding.UTF8.GetBytes("first-filesecond");
        var shared = new MemoryStream(bytes);
        var command = new StreamRequestAttachCommand
        {
            FolderId = "folder-1",
            File = new FormFile(shared, 0, 10, "file", "first.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" },
            Thumbnail = new FormFile(shared, 10, 6, "thumbnail", "second.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" },
        };

        using var content = ((IHttpRequestBodyBuilder)command).BuildRequestBody()!;
        using var sent = new MemoryStream();
        await content.CopyToAsync(sent);

        var text = Encoding.UTF8.GetString(sent.ToArray());
        text.Should().Contain("first-file").And.Contain("second");
    }

    [Fact]
    public void The_document_describes_an_operation_whose_Stream_hides_a_base_string_as_a_stream_body()
    {
        // Looking the property up by name alone used to throw AmbiguousMatchException over two Content properties.
        var operation = RenderOperation("/stream-request/hidden/stream", "post");

        operation.GetProperty("requestBody").GetProperty("content").EnumerateObject().Select(static p => p.Name)
            .Should().Equal("application/octet-stream");
    }

    [Fact]
    public void The_document_describes_an_operation_whose_string_hides_a_base_Stream_as_a_JSON_body()
    {
        var (operation, components) = RenderOperationAndComponents("/stream-request/hidden/text", "post");

        var content = operation.GetProperty("requestBody").GetProperty("content");
        content.EnumerateObject().Select(static p => p.Name).Should().Equal("application/json");

        var reference = content.GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        components.GetProperty(reference["#/components/schemas/".Length..]).GetProperty("properties").GetProperty("content")
            .GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void The_document_describes_a_stream_property_as_a_binary_octet_stream_request_body()
    {
        var operation = RenderOperation("/stream-request/imports/{FolderId}", "post");

        var content = operation.GetProperty("requestBody").GetProperty("content");
        content.EnumerateObject().Select(static p => p.Name).Should().Equal("application/octet-stream");

        var schema = content.GetProperty("application/octet-stream").GetProperty("schema");
        schema.GetProperty("type").GetString().Should().Be("string");
        schema.GetProperty("format").GetString().Should().Be("binary");

        ParameterNames(operation).Should().BeEquivalentTo("FolderId", "label", "limit");
    }

    [Fact]
    public async Task A_stream_operation_answers_a_query_value_that_does_not_parse_with_400()
    {
        await using var host = await StreamRequestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "stream-request/imports/folder-1?limit=bad")
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };

        var (status, _) = await host.SendAsync(request);

        status.Should().Be(400);
    }

    [Fact]
    public void The_document_gives_a_stream_operation_with_a_fallible_query_property_a_400_but_no_415()
    {
        // Suppressing a stream's 415, since it reads any media type, used to drop its query property's 400 with it.
        var operation = RenderOperation("/stream-request/imports/{FolderId}", "post");

        var statuses = operation.GetProperty("responses").EnumerateObject().Select(static p => p.Name).ToList();
        statuses.Should().Contain("400").And.NotContain("415");
    }

    [Fact]
    public void The_document_describes_an_operation_with_a_file_as_a_multipart_form_data_request_body()
    {
        var (operation, components) = RenderOperationAndComponents("/stream-request/folders/{FolderId}/attachments", "post");

        var content = operation.GetProperty("requestBody").GetProperty("content");
        content.EnumerateObject().Select(static p => p.Name).Should().Equal("multipart/form-data");

        var reference = content.GetProperty("multipart/form-data").GetProperty("schema").GetProperty("$ref").GetString()!;
        var properties = components.GetProperty(reference["#/components/schemas/".Length..]).GetProperty("properties");

        properties.EnumerateObject().Select(static p => p.Name)
            .Should().BeEquivalentTo("description", "priority", "tags", "digest", "file", "thumbnail");
        properties.GetProperty("file").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("file").GetProperty("format").GetString().Should().Be("binary");
        properties.GetProperty("thumbnail").GetProperty("format").GetString().Should().Be("binary");
        properties.GetProperty("priority").GetProperty("type").GetString().Should().Be("integer");

        ParameterNames(operation).Should().Equal("FolderId");
    }

    [Fact]
    public void The_document_describes_a_multipart_byte_array_as_the_repeated_integer_fields_it_is_bound_from()
    {
        // The JSON mapping, a base64 string, used to be applied to a form field, which is bound one byte per value.
        var properties = RenderMultipartSchemaProperties();

        var digest = properties.GetProperty("digest");
        digest.GetProperty("type").GetString().Should().Be("array");
        digest.GetProperty("items").GetProperty("type").GetString().Should().Be("integer");
    }

    private static JsonElement RenderMultipartSchemaProperties()
    {
        var (operation, components) = RenderOperationAndComponents("/stream-request/folders/{FolderId}/attachments", "post");
        var reference = operation.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data")
            .GetProperty("schema").GetProperty("$ref").GetString()!;

        return components.GetProperty(reference["#/components/schemas/".Length..]).GetProperty("properties");
    }

    private static JsonElement RenderOperation(string path, string method)
    {
        return RenderOperationAndComponents(path, method).Operation;
    }

    private static string? BuildRequestUrl<T>(T operation)
        where T : IHttpRequestBuilder<T>
    {
        return T.BuildRequestUrl(operation);
    }

    private static (JsonElement Operation, JsonElement Components) RenderOperationAndComponents(string path, string method)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        var root = JsonDocument.Parse(rendered.Json).RootElement;

        return (
            root.GetProperty("paths").GetProperty(path).GetProperty(method),
            root.GetProperty("components").GetProperty("schemas"));
    }

    private static List<string?> ParameterNames(JsonElement operation)
    {
        return operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(static p => p.GetProperty("name").GetString()).ToList()
            : [];
    }

    private static ByteArrayContent FileContent(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class StreamRequestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private StreamRequestHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<StreamRequestHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            return new StreamRequestHost(app, new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") });
        }

        public async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request)
        {
            using (request)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await this._client.SendAsync(request);

                return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>Content whose length is not known up front, so the client sends it chunked, with no Content-Length.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            return stream.WriteAsync(bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

/// <summary>An import whose content is the request body itself, read as a stream, beside a route and a query property.</summary>
[AllowAnonymous]
[RootLink("/stream-request/imports/{FolderId}")]
[HttpPost]
public partial class StreamRequestImportCommand : ICommand<string>
{
    public string FolderId { get; set; } = default!;

    [FromQuery]
    public string? Label { get; set; }

    [FromQuery]
    public int? Limit { get; set; }

    [FromBody]
    public Stream Content { get; set; } = default!;

    public static async Task<string> HandleAsync(StreamRequestImportCommand command)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16 * 1024];
        long total = 0;
        int read;

        while ((read = await command.Content.ReadAsync(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            total += read;
        }

        return $"folder={command.FolderId};label={command.Label};bytes={total};sha256={Convert.ToHexString(hash.GetHashAndReset())}";
    }
}

/// <summary>An attachment upload: a required and an optional file, with scalar and repeated form fields.</summary>
[AllowAnonymous]
[RootLink("/stream-request/folders/{FolderId}/attachments")]
[HttpPost]
public partial class StreamRequestAttachCommand : ICommand<string>
{
    public string FolderId { get; set; } = default!;

    public string? Description { get; set; }

    public int Priority { get; set; }

    public string[]? Tags { get; set; }

    public byte[]? Digest { get; set; }

    public IFormFile File { get; set; } = default!;

    public IFormFile? Thumbnail { get; set; }

    public static async Task<string> HandleAsync(StreamRequestAttachCommand command)
    {
        return $"folder={command.FolderId};description={command.Description};priority={command.Priority};"
            + $"tags={string.Join(",", command.Tags ?? [])};file={await DescribeAsync(command.File)};thumbnail={await DescribeAsync(command.Thumbnail)}";
    }

    private static async Task<string> DescribeAsync(IFormFile? file)
    {
        if (file is null)
        {
            return "none";
        }

        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream);

        return $"{file.FileName}|{file.ContentType}|{file.Length}|{Convert.ToHexString(hash)}";
    }
}

/// <summary>A base whose <c>Content</c> is text, which <see cref="StreamRequestHiddenStreamCommand"/> hides with a stream.</summary>
public abstract class StreamRequestTextContentBase
{
    public string? Content { get; set; }
}

/// <summary>An import whose stream body hides its base's text property of the same name.</summary>
[AllowAnonymous]
[RootLink("/stream-request/hidden/stream")]
[HttpPost]
public partial class StreamRequestHiddenStreamCommand : StreamRequestTextContentBase, ICommand<string>
{
    [FromBody]
    public new Stream Content { get; set; } = Stream.Null;

    public static Task<string> HandleAsync(StreamRequestHiddenStreamCommand command)
    {
        return Task.FromResult("ok");
    }
}

/// <summary>A base whose <c>Content</c> is a stream body, which <see cref="StreamRequestHiddenTextCommand"/> hides with text.</summary>
public abstract class StreamRequestStreamContentBase
{
    [FromBody]
    public Stream Content { get; set; } = Stream.Null;
}

/// <summary>An operation whose text property hides its base's stream, so its body is JSON again.</summary>
[AllowAnonymous]
[RootLink("/stream-request/hidden/text")]
[HttpPost]
public partial class StreamRequestHiddenTextCommand : StreamRequestStreamContentBase, ICommand<string>
{
    public new string? Content { get; set; }

    public static Task<string> HandleAsync(StreamRequestHiddenTextCommand command)
    {
        return Task.FromResult(command.Content ?? string.Empty);
    }
}

/// <summary>A base whose query-bound <c>Tags</c> <see cref="StreamRequestHiddenTagsCommand"/> hides with its own.</summary>
public abstract class StreamRequestTagsBase
{
    [FromQuery]
    public int[]? Tags { get; set; }
}

/// <summary>A stream import whose query properties include one hiding its base's, and a decimal.</summary>
[AllowAnonymous]
[RootLink("/stream-request/tagged")]
[HttpPost]
public partial class StreamRequestHiddenTagsCommand : StreamRequestTagsBase, ICommand<string>
{
    [FromQuery]
    public new int[]? Tags { get; set; }

    [FromQuery]
    public decimal? Rate { get; set; }

    [FromBody]
    public Stream Content { get; set; } = Stream.Null;

    public static Task<string> HandleAsync(StreamRequestHiddenTagsCommand command)
    {
        return Task.FromResult($"tags={string.Join(",", command.Tags ?? [])};rate={command.Rate?.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
