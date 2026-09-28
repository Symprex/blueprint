using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a <see cref="PlainTextResult"/> writes its <see cref="PlainTextResult.ContentType"/> exactly, with no
/// <c>charset</c> appended, as legacy Blueprint wrote it.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/PlainTextResult.cs:57</c>) wrote
/// <c>response.ContentType = this.ContentType</c> verbatim — the property, defaulted to <c>text/plain</c> with no charset
/// (<c>:44</c>), and never rewritten. Signature365's vCard, CSV and script downloads set a non-default
/// <see cref="PlainTextResult.ContentType"/> and rely on it being written unchanged.
/// </remarks>
public sealed class PlainTextContentTypeWireTests
{
    [Fact]
    public async Task A_set_content_type_is_written_verbatim_with_no_charset()
    {
        await using var host = await PlainTextContentTypeHost.StartAsync();

        using var response = await host.Client.GetAsync("plain-text-content-type/vcard");

        response.Content.Headers.ContentType!.ToString().Should().Be("text/vcard");
    }

    [Fact]
    public async Task The_default_content_type_is_text_plain_with_no_charset()
    {
        await using var host = await PlainTextContentTypeHost.StartAsync();

        using var response = await host.Client.GetAsync("plain-text-content-type/default");

        response.Content.Headers.ContentType!.ToString().Should().Be("text/plain");
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class PlainTextContentTypeHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PlainTextContentTypeHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<PlainTextContentTypeHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new PlainTextContentTypeHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A query answered with a plain-text result whose content type it names.</summary>
[AllowAnonymous]
[RootLink("/plain-text-content-type/{Kind}")]
public partial class GetPlainTextContentTypeQuery : IQuery<PlainTextResult>
{
    public string Kind { get; set; } = default!;

    public static PlainTextResult Handle(GetPlainTextContentTypeQuery query)
    {
        var result = new PlainTextResult("BEGIN:VCARD\nEND:VCARD");

        if (query.Kind == "vcard")
        {
            result.ContentType = "text/vcard";
        }

        return result;
    }
}
