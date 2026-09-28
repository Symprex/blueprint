using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a DELETE's properties are read from its JSON body, through the real router, as legacy Blueprint read the
/// body of every request but a GET.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>) populated the operation from the body whenever the method was not GET
/// (<c>Blueprint.Http/MessagePopulation/HttpBodyMessagePopulationSource.cs:145-147</c>). A request with no body or no
/// content type left the operation as it was (<c>:52-55</c>). The query-string catch-all applied to a GET alone
/// (<c>BlueprintApiBuilderExtensions.cs:68-73</c>). Its document gave a non-GET's properties as its request body, not as
/// parameters (<c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:134-137</c>, <c>:159-175</c>).
/// </para>
/// <para>
/// Signature365's Admin Portal sends <c>DELETE /messages/bulk-delete</c> with <c>{ ids }</c> (<c>DeleteMessagesCommand</c>,
/// its generated <c>deleteMessagesOptions(body)</c>). Its Portal sends <c>{ force }</c> to <c>DELETE /folders/{id}</c> and
/// <c>/images/{id}</c> (<c>DeleteFolderCommand</c>, <c>DeleteImageContainerCommand</c>). The operations here are shaped as those.
/// </para>
/// </remarks>
public sealed class DeleteBodyWireTests
{
    [Fact]
    public async Task A_DELETEs_properties_are_read_from_its_JSON_body()
    {
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.DeleteAsync("delete-body/messages/bulk-delete", """{"ids":["message-1","message-2"]}""");

        status.Should().Be(200);
        body.Should().Be("\"ids=message-1,message-2\"");
    }

    [Fact]
    public async Task A_DELETE_reads_its_route_placeholder_from_the_path_and_its_other_properties_from_its_body()
    {
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.DeleteAsync("delete-body/folders/folder-1", """{"force":true}""");

        status.Should().Be(200);
        body.Should().Be("\"id=folder-1;force=True\"");
    }

    [Fact]
    public async Task A_DELETE_without_a_body_is_bound_from_its_route_alone()
    {
        // No body and no content type leaves the operation as it was (HttpBodyMessagePopulationSource.cs:52-55): most of
        // Signature365's DELETEs send nothing but the URL.
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.DeleteAsync("delete-body/folders/folder-1", json: null);

        status.Should().Be(200);
        body.Should().Be("\"id=folder-1;force=False\"");
    }

    [Fact]
    public async Task A_body_less_DELETE_with_a_stray_non_JSON_content_type_is_bound_from_its_route_alone()
    {
        // A body-less DELETE that nonetheless carries a Content-Type header (e.g. a proxy or client default) used to
        // reach PipelineHelpers.ReadJsonBodyAsync's content-type guard and 415, once R98 made DELETE a body method too.
        // Legacy ignored a body no parser could read for every non-GET alike (HttpBodyMessagePopulationSource.cs:80-86
        // at Signature365 8b40ecc961): no parser selected, no error, the operation left as it was.
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.DeleteWithStrayContentTypeAsync("delete-body/folders/folder-1");

        status.Should().Be(200);
        body.Should().Be("\"id=folder-1;force=False\"");
    }

    [Theory]
    [InlineData("PATCH", "e2e/templates/stray-1", "id:stray-1|description:")]
    [InlineData("PUT", "e2e/settings/stray-1", "id:stray-1|value:")]
    public async Task A_PATCH_or_PUT_with_a_stray_non_JSON_content_type_is_bound_from_its_route_alone(string method, string url, string expectedBody)
    {
        // R116: legacy's silent ignore is not only DELETE/HEAD/OPTIONS (R108) — POST/PUT/PATCH too, for the same reason
        // (HttpBodyMessagePopulationSource.cs:80-86 at Signature365 8b40ecc961): no parser selected, no error, the
        // operation left as it was.
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.SendWithStrayContentTypeAsync(new HttpMethod(method), url);

        status.Should().Be(200);
        body.Should().Be($"\"{expectedBody}\"");
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=----stray")]
    public async Task A_POST_with_a_form_content_type_returns_415(string contentType)
    {
        // R117: legacy registered FormBodyParser by default (Signature365 8b40ecc961,
        // Blueprint.Http/BlueprintHttpOptionsSetup.cs:25) for exactly these two media types
        // (Formatters/FormBodyParser.cs:29-30) and bound form fields/IFormFile from them. This framework has no form
        // binding, so a form POST must not silently reach the handler with its properties unset (R116's silent ignore
        // covers every other non-JSON type, not these two).
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, _) = await host.SendWithContentTypeAsync(HttpMethod.Post, "e2e/items", contentType);

        status.Should().Be(415);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=----stray")]
    public async Task A_DELETE_with_a_form_content_type_returns_415(string contentType)
    {
        // Legacy's FormBodyParser applied to any non-GET method, DELETE included (no method check in
        // HttpBodyMessagePopulationSource.cs), so a DELETE with one of these content types 415s too, even without a
        // body of its own.
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, _) = await host.SendWithContentTypeAsync(HttpMethod.Delete, "delete-body/folders/folder-1", contentType);

        status.Should().Be(415);
    }

    [Fact]
    public async Task A_DELETEs_query_string_is_not_read_for_a_property_its_body_owns()
    {
        // The query catch-all applied to a GET alone (BlueprintApiBuilderExtensions.cs:68-73).
        await using var host = await DeleteBodyHost.StartAsync();

        var (status, body) = await host.DeleteAsync("delete-body/folders/folder-1?force=true", json: null);

        status.Should().Be(200);
        body.Should().Be("\"id=folder-1;force=False\"");
    }

    [Fact]
    public void The_document_publishes_a_DELETEs_properties_as_its_request_body_and_not_as_query_parameters()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());
        using var document = JsonDocument.Parse(rendered.Json);
        var operation = document.RootElement.GetProperty("paths").GetProperty("/delete-body/messages/bulk-delete").GetProperty("delete");

        var reference = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString()!;
        var component = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(reference["#/components/schemas/".Length..]);

        component.GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Equal("ids");

        var parameterNames = operation.TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(static p => p.GetProperty("name").GetString()).ToList()
            : [];
        parameterNames.Should().NotContain(static name => string.Equals(name, "ids", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class DeleteBodyHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private DeleteBodyHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<DeleteBodyHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new DeleteBodyHost(app, client);
        }

        /// <summary>Sends a DELETE, with a JSON body when one is given and with no body or content type otherwise.</summary>
        public async Task<(int Status, string Body)> DeleteAsync(string url, string? json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);

            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await this.Client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>Sends a DELETE with an empty body but a stray, non-JSON Content-Type header (no data to read).</summary>
        public Task<(int Status, string Body)> DeleteWithStrayContentTypeAsync(string url)
        {
            return this.SendWithContentTypeAsync(HttpMethod.Delete, url, "text/plain", body: string.Empty);
        }

        /// <summary>Sends a request of <paramref name="method"/> with a stray, non-JSON Content-Type header and a non-empty body.</summary>
        public Task<(int Status, string Body)> SendWithStrayContentTypeAsync(HttpMethod method, string url)
        {
            return this.SendWithContentTypeAsync(method, url, "text/plain", body: "not json");
        }

        /// <summary>Sends a request of <paramref name="method"/> carrying exactly <paramref name="contentType"/> as its Content-Type header.</summary>
        public async Task<(int Status, string Body)> SendWithContentTypeAsync(HttpMethod method, string url, string contentType, string body = "")
        {
            using var request = new HttpRequestMessage(method, url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await this.Client.SendAsync(request);

            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>Shaped as Signature365 Admin Portal's <c>DeleteMessagesCommand</c>: a DELETE whose one property is its body.</summary>
[AllowAnonymous]
[RootLink("/delete-body/messages/bulk-delete")]
[HttpDelete]
public partial class DeleteBodyMessagesCommand : ICommand<string>
{
    [Required]
    public string[] Ids { get; set; } = default!;

    public static string Handle(DeleteBodyMessagesCommand command)
    {
        return "ids=" + string.Join(",", command.Ids);
    }
}

/// <summary>
/// Shaped as Signature365 Portal's <c>DeleteFolderCommand</c>: a DELETE with a route placeholder and a body property.
/// </summary>
[AllowAnonymous]
[RootLink("/delete-body/folders/{Id}")]
[HttpDelete]
public partial class DeleteBodyFolderCommand : ICommand<string>
{
    public string Id { get; set; } = default!;

    public bool Force { get; set; }

    public static string Handle(DeleteBodyFolderCommand command)
    {
        return $"id={command.Id};force={command.Force}";
    }
}
