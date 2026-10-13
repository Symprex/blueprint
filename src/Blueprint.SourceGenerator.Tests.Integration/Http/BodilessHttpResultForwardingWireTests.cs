using System.Net;
using System.Text;
using Blueprint.Auditing;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves, over two real Kestrel hosts, that a bodiless, content-type-less response one Blueprint app returns as an
/// <see cref="HttpResult"/> forwards through <see cref="global::Blueprint.HttpClient.BlueprintHttpClient{TApp}"/> and
/// reaches the external caller of a second app unchanged (R112).
/// </summary>
/// <remarks>
/// The live shape this reproduces is Signature365 Microsoft365.Gateway's
/// <c>Microsoft365GatewayHandleGraphWebhookNotificationsCommand : ICommand&lt;HttpResult&gt;</c>, which forwards
/// Microsoft365's bodiless 202 on to Microsoft Graph (found wiring S365 Microsoft365, commit <c>125e760410</c>). Legacy's
/// client made that shape a <c>new StatusCodeResult(status)</c> whenever nothing else claimed the response and
/// <c>StatusCodeResult</c> was assignable to the response type — legacy's <c>StatusCodeResult</c> derived from
/// <c>HttpResult</c> (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:199-225</c>). The upstream
/// host maps at the empty base path so the client's leading-slash URLs resolve against its root exactly.
/// </remarks>
public sealed class BodilessHttpResultForwardingWireTests
{
    [Fact]
    public async Task A_bodiless_HttpResult_is_forwarded_through_a_gateway_command_and_reaches_the_caller_as_a_bodiless_202()
    {
        await using var upstream = await TwoHostFixture.StartAsync(basePath: string.Empty);
        await using var gateway = await TwoHostFixture.StartAsync(basePath: "api/");

        using var content = new StringContent(
            $$"""{"upstreamBaseUrl":"{{upstream.BaseUri}}"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await gateway.Client.PostAsync("e2e/forward-bodiless-http-result", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        response.Content.Headers.ContentType.Should().BeNull();
        upstream.ServerErrors.Should().BeEmpty("the upstream must answer cleanly for the forward to prove anything");
        gateway.ServerErrors.Should().BeEmpty(string.Join(" | ", gateway.ServerErrors.Select(static e => e.ToString())));
    }

    /// <summary>The integration app, served by Kestrel at the given base path, with its own errors recorded.</summary>
    private sealed class TwoHostFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TwoHostFixture(WebApplication app, System.Net.Http.HttpClient client, string baseUri, List<Exception> serverErrors)
        {
            this._app = app;
            this.Client = client;
            this.BaseUri = baseUri;
            this.ServerErrors = serverErrors;
        }

        public System.Net.Http.HttpClient Client { get; }

        /// <summary>The host's own root address, for another host's <c>BlueprintHttpClient</c> to be pointed at.</summary>
        public string BaseUri { get; }

        public List<Exception> ServerErrors { get; }

        public static async Task<TwoHostFixture> StartAsync(string basePath)
        {
            var serverErrors = new List<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();

            app.Use(async (httpContext, next) =>
            {
                try
                {
                    await next(httpContext);
                }
                catch (Exception exception)
                {
                    lock (serverErrors)
                    {
                        serverErrors.Add(exception);
                    }

                    throw;
                }
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(basePath);

            await app.StartAsync();

            var rootUri = app.Urls.First().TrimEnd('/') + "/";
            var baseUri = string.IsNullOrEmpty(basePath) ? rootUri : rootUri + basePath;
            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(baseUri) };

            return new TwoHostFixture(app, client, rootUri, serverErrors);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>A status-only <see cref="HttpResult"/>, as a handler might define its own, shaped like S365's private one.</summary>
internal sealed class E2EBodilessAcceptedResult : HttpResult
{
    public E2EBodilessAcceptedResult()
        : base(HttpStatusCode.Accepted)
    {
    }
}

/// <summary>Answers a bodiless, content-type-less 202, as Microsoft365's webhook-notification endpoint does.</summary>
[AllowAnonymous]
[DoNotAudit]
[RootLink("/e2e/bodiless-accepted-http-result")]
public partial class E2EBodilessAcceptedCommand : ICommand<HttpResult>
{
    /// <summary>Returns a bodiless 202.</summary>
    /// <param name="command">The command.</param>
    /// <returns>A status-only <see cref="HttpResult"/> of 202.</returns>
    public static HttpResult Handle(E2EBodilessAcceptedCommand command)
    {
        return new E2EBodilessAcceptedResult();
    }
}

/// <summary>
/// Forwards <see cref="E2EBodilessAcceptedCommand"/> from another Blueprint app, as Microsoft365.Gateway's
/// <c>Microsoft365GatewayHandleGraphWebhookNotificationsCommand</c> forwards Microsoft365's response.
/// </summary>
[AllowAnonymous]
[DoNotAudit]
[RootLink("/e2e/forward-bodiless-http-result")]
public partial class E2EForwardBodilessHttpResultCommand : ICommand<HttpResult>
{
    /// <summary>The address of the upstream app to forward the call to.</summary>
    public string UpstreamBaseUrl { get; set; } = default!;

    /// <summary>Calls the upstream app for <see cref="E2EBodilessAcceptedCommand"/> and returns what it answered.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The upstream's <see cref="HttpResult"/>, unchanged.</returns>
    public static async Task<HttpResult> Handle(E2EForwardBodilessHttpResultCommand command)
    {
        using var httpClient = new System.Net.Http.HttpClient { BaseAddress = new Uri(command.UpstreamBaseUrl) };
        var client = new global::Blueprint.HttpClient.BlueprintHttpClient<IntegrationTestBlueprintApp>(httpClient, new IntegrationTestBlueprintApp());

        return (await client.SendAsync<HttpResult>(new E2EBodilessAcceptedCommand()))!;
    }
}
