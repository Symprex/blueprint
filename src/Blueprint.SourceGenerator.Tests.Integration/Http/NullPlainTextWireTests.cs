using System.Net;
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
/// Proves that a <see cref="PlainTextResult"/> made with no content is answered as legacy Blueprint answered it: its
/// status with an empty body.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/PlainTextResult.cs</c>) kept the content as given (:21-37).
/// It set the result's status (<c>HttpResult.cs:41-56</c>) and wrote the content through an
/// <c>HttpResponseStreamWriter</c>, which writes nothing for a null string (:50-63). Signature365's log download
/// returns one for a session with no log.
/// </remarks>
public sealed class NullPlainTextWireTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    public async Task A_plain_text_result_with_no_content_is_its_status_with_an_empty_body(int status)
    {
        await using var host = await NullPlainTextHost.StartAsync();

        using var response = await host.Client.GetAsync($"null-plain-text/{status}");

        ((int)response.StatusCode).Should().Be(status);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public void A_plain_text_result_with_no_content_has_empty_content()
    {
        string? content = null;

        new PlainTextResult(content).Content.Should().BeEmpty();
        new PlainTextResult(content, HttpStatusCode.NotFound).Content.Should().BeEmpty();
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class NullPlainTextHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private NullPlainTextHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<NullPlainTextHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new NullPlainTextHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A query answered with a plain-text result that has no content, at the status it names.</summary>
[AllowAnonymous]
[RootLink("/null-plain-text/{Status}")]
public partial class GetNullPlainTextQuery : IQuery<PlainTextResult>
{
    public int Status { get; set; }

    public static PlainTextResult Handle(GetNullPlainTextQuery query)
    {
        return new PlainTextResult(null, (HttpStatusCode)query.Status);
    }
}
