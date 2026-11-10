using System.Text.Json;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an <see cref="ApiException" />'s extension keys are named with the app's JSON naming policy when its
/// problem is written, as a 422's <c>errors</c> keys are.
/// </summary>
/// <remarks>
/// System.Text.Json writes extension data keys as they are, whatever the options' policies, so an exception made with
/// <c>["Errors"]</c> answered <c>"Errors"</c> to a client that reads every other member camelCase: Signature365's
/// template syntax errors never reached its Portal. The key policy is the options' <c>DictionaryKeyPolicy</c>, else their
/// <c>PropertyNamingPolicy</c>, as for a 422's keys. <c>traceId</c> is Blueprint's own member and keeps its name.
/// </remarks>
public sealed class ProblemExtensionKeyNamingWireTests
{
    [Fact]
    public async Task Extension_keys_are_camelCase_under_the_default_options()
    {
        await using var host = await ExtensionHost.StartAsync(static _ => { });

        var body = await host.GetAsync("pascal-extensions");

        body.Should().Be("""{"type":"syntax_error","title":"Template has syntax errors","status":400,"errors":["Unexpected token"],"htmlErrors":["Unclosed tag"]}""");
    }

    [Fact]
    public async Task Extension_keys_are_written_as_they_are_with_neither_policy_set()
    {
        await using var host = await ExtensionHost.StartAsync(static json =>
        {
            json.PropertyNamingPolicy = null;
            json.DictionaryKeyPolicy = null;
        });

        var body = await host.GetAsync("pascal-extensions");

        // With no policy ProblemDetails' own members are written by their JSON property names, which are lower case.
        body.Should().Be("""{"type":"syntax_error","title":"Template has syntax errors","status":400,"Errors":["Unexpected token"],"HtmlErrors":["Unclosed tag"]}""");
    }

    [Fact]
    public async Task Extension_keys_are_named_once_with_the_dictionary_key_policy_and_the_trace_id_is_not_named()
    {
        await using var host = await ExtensionHost.StartAsync(static json => json.DictionaryKeyPolicy = new PrefixNamingPolicy(), withActivity: true);

        var names = MemberNames(await host.GetAsync("pascal-extensions"));

        names.Should().Equal("type", "title", "status", "x_Errors", "x_HtmlErrors", "traceId");
    }

    [Fact]
    public async Task Extension_keys_are_named_with_the_property_naming_policy_when_there_is_no_dictionary_key_policy()
    {
        await using var host = await ExtensionHost.StartAsync(static json =>
        {
            json.DictionaryKeyPolicy = null;
            json.PropertyNamingPolicy = new PrefixNamingPolicy();
        });

        var names = MemberNames(await host.GetAsync("pascal-extensions"));

        names.Should().Contain(["x_Errors", "x_HtmlErrors"]).And.NotContain(["Errors", "HtmlErrors", "x_x_Errors"]);
    }

    [Theory]
    [InlineData("colliding-written-first", "errors", "already named")]
    [InlineData("colliding-renamed-first", "errors", "already named")]
    [InlineData("colliding-both-renamed", "htmlErrors", "HTMLErrors")]
    [InlineData("colliding-both-renamed-reversed", "htmlErrors", "HTMLErrors")]
    public async Task Of_two_keys_named_alike_the_one_already_so_named_wins_and_otherwise_the_ordinally_first(string outcome, string name, string value)
    {
        await using var host = await ExtensionHost.StartAsync(static _ => { });

        var body = await host.GetAsync(outcome);

        // The other key is left out, not written under its own name.
        MemberNames(body).Should().Equal("type", "title", "status", name);

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty(name).GetString().Should().Be(value);
    }

    private static string[] MemberNames(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
    }

    /// <summary>A naming policy whose output shows the casing of the name it was given, and whether it was applied twice.</summary>
    private sealed class PrefixNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "x_" + name;
        }
    }

    /// <summary>The integration app, served by Kestrel with the given serializer options.</summary>
    private sealed class ExtensionHost : IAsyncDisposable
    {
        private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private ExtensionHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<ExtensionHost> StartAsync(Action<JsonSerializerOptions> configure, bool withActivity = false)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            // Logging enabled is what makes ASP.NET Core start the request's activity, which the operation's is made under.
            if (withActivity)
            {
                builder.Logging.AddProvider(new EnabledLoggerProvider());
            }

            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint => blueprint
                .AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>()
                .ConfigureJson(json => configure(json.SerializerOptions)));

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ExtensionHost(app, client);
        }

        public async Task<string> GetAsync(string outcome)
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "exception-problems/" + outcome);
            request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");

            using var response = await this._client.SendAsync(request);

            ((int)response.StatusCode).Should().Be(400);

            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    /// <summary>A logger provider whose loggers are enabled and record nothing.</summary>
    private sealed class EnabledLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new EnabledLogger();
        }

        public void Dispose()
        {
        }

        private sealed class EnabledLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
