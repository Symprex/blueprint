using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Blueprint.EndToEnd.Tests.Harness;
using Blueprint.EndToEnd.Tests.SampleSystem;

namespace Blueprint.EndToEnd.Tests;

/// <summary>
/// Streaming request bodies through a gateway on real Kestrel (#71, D19): the sample gateway's
/// <c>[Expose&lt;AttachTemplateFileCommand&gt;]</c> takes a <c>multipart/form-data</c> upload and forwards its file and fields
/// upstream as a form of its own, and its <c>[Expose&lt;ImportTemplateSourceCommand&gt;]</c> hands a raw request body to the
/// inner service as it arrives. Both are documented with their media type.
/// </summary>
[Collection(SampleSystemCollection.Name)]
public sealed class StreamRequestTests(SampleSystemFixture system)
{
    [Fact]
    public async Task A_multipart_upload_through_the_gateway_reaches_the_inner_app_intact()
    {
        var id = await system.SeedTemplateAsync("Attachment Template");
        var file = RandomNumberGenerator.GetBytes(5 * 1024 * 1024);
        using var caller = system.Gateway.CreateClient(system.CreateCallerToken());

        using var form = new MultipartFormDataContent
        {
            { new StringContent("Signed contract"), "description" },
            { FileContent(file, "application/pdf"), "file", "contract.pdf" },
        };

        var response = await caller.PostAsync($"gateway/templates/{id}/attachments", form);

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("templateId").GetString().Should().Be(id);
        body.RootElement.GetProperty("fileName").GetString().Should().Be("contract.pdf");
        body.RootElement.GetProperty("contentType").GetString().Should().Be("application/pdf");
        body.RootElement.GetProperty("description").GetString().Should().Be("Signed contract");
        body.RootElement.GetProperty("length").GetInt64().Should().Be(file.Length);
        body.RootElement.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(file)));

        system.InnerRequests.Requests.Should().Contain(static r => r.Path.EndsWith("/attachments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_gateway_OpenAPI_documents_an_upload_as_multipart_form_data_and_a_stream_as_an_octet_stream()
    {
        using var gateway = system.Gateway.CreateClient(system.CreateCallerToken());
        var response = await gateway.GetAsync("gateway/.blueprint/openapi.json");
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        using var document = JsonDocument.Parse(text);

        var upload = FindOperation(document, "/templates/{id}/attachments", "post").GetProperty("requestBody").GetProperty("content");
        upload.EnumerateObject().Select(static c => c.Name).Should().Equal("multipart/form-data");
        var reference = upload.GetProperty("multipart/form-data").GetProperty("schema").GetProperty("$ref").GetString()!;
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(reference["#/components/schemas/".Length..]).GetProperty("properties");
        properties.GetProperty("file").GetProperty("type").GetString().Should().Be("string");
        properties.GetProperty("file").GetProperty("format").GetString().Should().Be("binary");
        properties.GetProperty("description").GetProperty("type").GetString().Should().Be("string");

        var import = FindOperation(document, "/templates/{id}/source", "put").GetProperty("requestBody").GetProperty("content");
        import.EnumerateObject().Select(static c => c.Name).Should().Equal("application/octet-stream");
        import.GetProperty("application/octet-stream").GetProperty("schema").GetProperty("format").GetString().Should().Be("binary");
    }

    [Fact]
    public async Task A_large_stream_body_reaches_the_inner_app_through_the_gateway_before_the_caller_has_finished_sending_it()
    {
        // A pair of its own, so the inner app can report how far it has read the body while the caller is still sending.
        var issuer = new TestJwtIssuer();
        var probe = new BodyReadProbe(threshold: 1024 * 1024);
        await using var host = new MultiAppHost(issuer);
        var inner = await host.StartAsync<Blueprint.SourceGenerator.Sample.Startup>("inner", options =>
            options.ConfigureServices = services => services.AddSingleton<IStartupFilter>(probe));
        var gateway = await host.StartAsync<Blueprint.SourceGenerator.Sample.Gateway.Startup>("gateway", options =>
        {
            options.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.TemplateServiceAddressKey] = new Uri(inner.BaseAddress, "api/").ToString();
            options.Settings[Blueprint.SourceGenerator.Sample.Gateway.Startup.ServiceTokenKey] = issuer.CreateToken([new Claim("sub", "gateway-service")]);
            options.ConfigureServices = services => services.AddSingleton<Blueprint.SourceGenerator.Sample.Gateway.ITenantTokenExchanger>(
                new TestTenantTokenExchanger(issuer, SampleSystemFixture.TemplateScopes));
            options.ForwardCallerCredential = true;
        });

        var token = issuer.CreateToken([new Claim("sub", "e2e-user"), .. SampleSystemFixture.TemplateScopes.Select(static s => new Claim("scope", s))]);
        var id = await SeedTemplateAsync(inner, token);

        // 24 MB, under Kestrel's default 30 MB limit on a request body: the caller sends the first megabyte, then waits until the inner app has read it. A gateway that buffered the
        // body anywhere would hold that megabyte until the caller finished, which it never would while it waits.
        var payload = RandomNumberGenerator.GetBytes(24 * 1024 * 1024);
        using var content = new GatedContent(payload, firstChunk: 1024 * 1024, gate: probe.ThresholdRead, gateTimeout: TimeSpan.FromSeconds(15));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var caller = gateway.CreateClient(token);

        var response = await caller.PutAsync($"gateway/templates/{id}/source", content);

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        content.GateOpenedWhileSending.Should().BeTrue(
            "the inner app should have read the first megabyte while the caller was still sending the rest, which it cannot if the gateway buffers the body");

        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("length").GetInt64().Should().Be(payload.Length);
        body.RootElement.GetProperty("sha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(payload)));
    }

    private static JsonElement FindOperation(JsonDocument document, string path, string method)
    {
        var paths = document.RootElement.GetProperty("paths");
        var names = paths.EnumerateObject().Select(static p => p.Name).ToList();
        var name = names.Should().ContainSingle(n => string.Equals(n, path, StringComparison.OrdinalIgnoreCase), string.Join(", ", names)).Which;

        return paths.GetProperty(name).GetProperty(method);
    }

    private static ByteArrayContent FileContent(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static async Task<string> SeedTemplateAsync(RunningApp inner, string token)
    {
        using var client = inner.CreateClient(token);
        var response = await client.PostAsJsonAsync("api/templates", new { name = "Imported", htmlSource = "<p>x</p>", plainTextSource = "x" });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Wraps the request body of every import the inner app receives, before anything else in the app sees it, and reports
    /// once the app has read <c>threshold</c> bytes of it. It changes nothing the app reads.
    /// </summary>
    private sealed class BodyReadProbe(long threshold) : IStartupFilter
    {
        private readonly TaskCompletionSource _thresholdRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ThresholdRead => this._thresholdRead.Task;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (HttpMethods.IsPut(context.Request.Method) && context.Request.Path.Value?.EndsWith("/source", StringComparison.Ordinal) == true)
                    {
                        context.Request.Body = new ObservedReadStream(context.Request.Body, threshold, this._thresholdRead);
                    }

                    await nextMiddleware();
                });

                next(app);
            };
        }
    }

    /// <summary>A read-only stream that counts what is read from it and completes a task once a threshold is reached.</summary>
    private sealed class ObservedReadStream(Stream inner, long threshold, TaskCompletionSource thresholdRead) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return this.Observe(await inner.ReadAsync(buffer, cancellationToken));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.Observe(inner.Read(buffer, offset, count));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private int Observe(int read)
        {
            this._read += read;
            if (this._read >= threshold)
            {
                thresholdRead.TrySetResult();
            }

            return read;
        }
    }

    /// <summary>
    /// Content of unknown length, sent chunked: its first chunk, then nothing until <c>gate</c> completes (or the timeout
    /// passes), then the rest.
    /// </summary>
    private sealed class GatedContent(byte[] payload, int firstChunk, Task gate, TimeSpan gateTimeout) : HttpContent
    {
        public bool GateOpenedWhileSending { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync(payload.AsMemory(0, firstChunk));
            await stream.FlushAsync();

            this.GateOpenedWhileSending = await Task.WhenAny(gate, Task.Delay(gateTimeout)) == gate;

            for (var offset = firstChunk; offset < payload.Length; offset += 81920)
            {
                await stream.WriteAsync(payload.AsMemory(offset, Math.Min(81920, payload.Length - offset)));
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
