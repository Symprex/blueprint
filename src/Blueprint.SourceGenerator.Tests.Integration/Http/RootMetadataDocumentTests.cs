using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Expansion;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an app answers <c>GET</c> at its base path with the root-metadata document legacy Blueprint served, and
/// that every base path a consumer passes to <c>MapBlueprintApi</c> gives single-slash routes and hrefs.
/// </summary>
/// <remarks>
/// <para>
/// Legacy registered <c>RootMetadataOperation</c> in every HTTP app (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.Http/BlueprintApiBuilderExtensions.cs:84-92</c>). It was <c>[RootLink("/", Rel = "self")]</c>,
/// <c>[AllowAnonymous]</c>, <c>[DoNotAudit]</c> and <c>[UnexposedOperation]</c> (<c>RootMetadataOperation.cs:10-14</c>), and
/// returned a <c>RootResource</c> (<c>$object</c> <c>"root"</c>, <c>ApiResource.cs:68-77</c>) with one link per root link —
/// a link with no resource type (<c>ApiDataModel.cs:212-215</c>), that is a <c>[RootLink]</c> or a <c>[Link(null, ...)]</c> —
/// of every exposed operation, keyed by the link's rel and with a templated href when it has placeholders
/// (<c>RootMetadataOperation.cs:27-38</c>; <c>ApiLinkGenerator.cs:92-118</c>). A link's rel defaulted to the descriptor's
/// name when the link was built, before the app's scanner conventions renamed the descriptor
/// (<c>Blueprint/Configuration/OperationScanner.cs:294-303</c>).
/// </para>
/// <para>
/// Legacy normalised the base path to end with one slash and appended each link's URL, whose leading slash it had
/// trimmed (<c>Blueprint.Http/ApplicationBuilderExtensions.cs:45</c>, <c>:105</c>; <c>Blueprint/ApiOperationLink.cs:48</c>).
/// </para>
/// <para>
/// Signature365's only consumer test of the document is VCard.Gateway's, mounted at <c>"/"</c>
/// (<c>backend/tests/Symprex.Signature365.VCard.Gateway.Tests/Api/RootMetadataTests.cs:27-34</c>).
/// </para>
/// </remarks>
public sealed class RootMetadataDocumentTests
{
    private const string VCardTemplate = "root-metadata/{NonPrefixedTenantId}-{NonPrefixedUserId}.vcf";

    public static TheoryData<string, string> BasePaths => new()
    {
        { "/", "/" },
        { string.Empty, "/" },
        { "api", "/api/" },
        { "/api/", "/api/" },
    };

    [Theory]
    [MemberData(nameof(BasePaths))]
    public async Task When_the_base_path_is_requested_then_the_root_document_is_returned(string basePath, string expectedBase)
    {
        await using var host = await RootMetadataHost.StartAsync(basePath);

        using var response = await host.Client.GetAsync(expectedBase);
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "legacy answered GET at the base path; the body was {0}", raw);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        // RootMetadataTests.cs:34: the placeholder link is a template the client fills in.
        raw.Should().Contain("{NonPrefixedTenantId}-{NonPrefixedUserId}.vcf");

        using var body = JsonDocument.Parse(raw);
        body.RootElement.GetProperty("$object").GetString().Should().Be("root");
        body.RootElement.GetProperty("$links").GetProperty("SampleGenerateRootvCard").GetProperty("href").GetString()
            .Should().Be(host.AbsoluteUri(expectedBase + VCardTemplate), "the base URI ends with one slash and the link's URL has none");
        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_root_document_is_rendered_then_each_root_link_is_keyed_by_its_rel_before_the_app_renamed_it()
    {
        // IntegrationTestBlueprintApp removes "Sample" from operation names, but legacy took the default rel from the
        // descriptor's name before its conventions ran (OperationScanner.cs:294-303).
        await using var host = await RootMetadataHost.StartAsync("api/");

        var links = await host.GetRootLinksAsync("/api/");

        links.Should().ContainKey("SampleGenerateRootvCard").And.NotContainKey("GenerateRootvCard");
        links.Should().ContainKey("ExportSample", "ExportCommandSample is named ExportSample before the app removes Sample")
            .And.NotContainKey("Export");
        links["bulk-delete"].Should().Be(host.AbsoluteUri("/api/root-metadata/bulk-delete"), "an explicit Rel is the key");
        links["RootMetadataViaLink"].Should().Be(host.AbsoluteUri("/api/root-metadata/via-link"), "a [Link(null, ...)] is a root link");
        links["RootMetadataNoLeadingSlash"].Should().Be(host.AbsoluteUri("/api/root-metadata/no-leading-slash"));
    }

    [Fact]
    public async Task When_the_root_document_is_rendered_then_each_root_link_is_written_with_a_null_type_as_legacy_wrote_it()
    {
        // RootMetadataOperation made each root link with its Href alone (RootMetadataOperation.cs:31-38), and legacy's
        // Link.Type had only a Newtonsoft ignore (Blueprint/Link.cs:19-23) under System.Text.Json output with no
        // DefaultIgnoreCondition (BlueprintJsonOptions.cs:22-45; BlueprintHttpOptionsSetup.cs:20-22), so it was written null.
        await using var host = await RootMetadataHost.StartAsync("api/");

        using var response = await host.Client.GetAsync("/api/");
        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().Contain($$"""
            "bulk-delete":{"href":"{{host.AbsoluteUri("/api/root-metadata/bulk-delete")}}","type":null}
            """);

        using var body = JsonDocument.Parse(raw);
        foreach (var link in body.RootElement.GetProperty("$links").EnumerateObject())
        {
            link.Value.EnumerateObject().Select(static p => p.Name).Should().Equal(["href", "type"], "root link {0}", link.Name);
            link.Value.GetProperty("type").ValueKind.Should().Be(JsonValueKind.Null, "root link {0}", link.Name);
        }

        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_an_operation_has_several_root_links_then_the_root_document_lists_each_and_each_routes()
    {
        // Legacy's RootLinkAttribute allowed several (Blueprint/RootLinkAttribute.cs:5, AllowMultiple = true); its scanner
        // made a link of each, with that attribute's own Rel (OperationScanner.cs:294-298), the document listed every root
        // link (RootMetadataOperation.cs:27-40), and every one was routed.
        await using var host = await RootMetadataHost.StartAsync("api/");

        var links = await host.GetRootLinksAsync("/api/");

        links["RootMetadataReports"].Should().Be(host.AbsoluteUri("/api/root-metadata/reports"));
        links["recent-reports"].Should().Be(host.AbsoluteUri("/api/root-metadata/reports/recent"));

        foreach (var path in new[] { "/api/root-metadata/reports", "/api/root-metadata/reports/recent" })
        {
            using var response = await host.Client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} is one of the operation's routes", path);
            (await response.Content.ReadAsStringAsync()).Should().Be("\"reports\"");
        }

        host.ServerErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_root_document_is_rendered_then_it_lists_neither_unexposed_operations_nor_resource_links()
    {
        // RootMetadataOperation.cs:29 skipped unexposed operations, its own "/" self link among them; resource links have a
        // resource type, so are not root links (ApiDataModel.cs:212-215).
        await using var host = await RootMetadataHost.StartAsync("api/");

        var links = await host.GetRootLinksAsync("/api/");

        links.Should().NotContainKey("RootMetadataHidden").And.NotContainKey("self");
    }

    [Fact]
    public async Task When_the_root_document_is_requested_anonymously_then_it_is_returned_where_operations_need_a_user()
    {
        // RootMetadataOperation.cs:14 is [AllowAnonymous]; it still lists root links the caller cannot use.
        await using var host = await RootMetadataHost.StartAsync("api/");

        using var protectedResponse = await host.Client.GetAsync("/api/e2e/protected");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the app's own operations do need a user");

        var links = await host.GetRootLinksAsync("/api/");

        links.Should().ContainKey("RootMetadataSecured");
    }

    [Fact]
    public async Task When_a_path_base_and_public_host_apply_then_root_hrefs_are_made_from_them()
    {
        // Legacy made every href from the request's base URI (ApiLinkGenerator.cs:30, :92-118), as Signature365's gateways
        // are reached behind a path base and a public host.
        await using var host = await RootMetadataHost.StartAsync("api/", pathBase: "/gw", publicHost: "public.example.com");

        var links = await host.GetRootLinksAsync("/gw/api/");

        links["bulk-delete"].Should().Be("http://public.example.com/gw/api/root-metadata/bulk-delete");
        links["SampleGenerateRootvCard"].Should().Be("http://public.example.com/gw/api/" + VCardTemplate);
    }

    [Fact]
    public async Task When_the_app_disables_links_then_the_root_document_still_lists_its_root_links()
    {
        // RootMetadataOperation.Invoke added the root links itself (RootMetadataOperation.cs:27-38), so an app without
        // AddHateoasLinks() served them too: EventStore's generated RootMetadataOperationExecutorPipeline has no link step.
        await using var host = await RootMetadataHost.StartAsync("api/", configure: static blueprint => blueprint.DisableHateoasLinks());

        var links = await host.GetRootLinksAsync("/api/");

        links["bulk-delete"].Should().Be(host.AbsoluteUri("/api/root-metadata/bulk-delete"));
    }

    [Fact]
    public async Task When_the_openapi_document_is_rendered_then_it_describes_no_root_document()
    {
        // RootMetadataOperation was [UnexposedOperation] (RootMetadataOperation.cs:12), so no OpenAPI path described it.
        await using var host = await RootMetadataHost.StartAsync("api/");

        using var document = JsonDocument.Parse(await host.Client.GetStringAsync("/api/.blueprint/openapi.json"));

        document.RootElement.GetProperty("paths").TryGetProperty("/", out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BasePaths))]
    public async Task When_an_app_is_mapped_at_a_base_path_then_every_route_it_maps_has_one_slash(string basePath, string expectedBase)
    {
        await using var host = await RootMetadataHost.StartAsync(basePath);

        // An operation URL without a leading slash is appended after the base path's one slash, as legacy appended its
        // leading-slash-trimmed URLs (ApplicationBuilderExtensions.cs:45, :105; ApiOperationLink.cs:48).
        using var operation = await host.Client.GetAsync(expectedBase + "root-metadata/no-leading-slash");
        operation.StatusCode.Should().Be(HttpStatusCode.OK, "the route is {0}root-metadata/no-leading-slash", expectedBase);

        using var openApi = await host.Client.GetAsync(expectedBase + ".blueprint/openapi.json");
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);

        using var catalogue = JsonDocument.Parse(await host.Client.GetStringAsync("/.well-known/api-catalog"));
        catalogue.RootElement.GetProperty("linkset")[0].GetProperty("service-desc")[0].GetProperty("href").GetString()
            .Should().Be(host.AbsoluteUri(expectedBase + ".blueprint/openapi.json"));
        host.ServerErrors.Should().BeEmpty();
    }

    /// <summary>The integration app registered as a consumer registers it and mapped at a given base path, on Kestrel.</summary>
    private sealed class RootMetadataHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private RootMetadataHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<Exception> serverErrors)
        {
            this._app = app;
            this.Client = client;
            this.ServerErrors = serverErrors;
        }

        public System.Net.Http.HttpClient Client { get; }

        public ConcurrentQueue<Exception> ServerErrors { get; }

        public static async Task<RootMetadataHost> StartAsync(
            string basePath,
            string? pathBase = null,
            string? publicHost = null,
            Action<BlueprintServiceBuilder>? configure = null)
        {
            var serverErrors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new ErrorRecordingLoggerProvider(serverErrors));

            // The one expander type every test expanding DepartmentResource uses (see TestDepartmentExpander's remarks).
            builder.Services.AddSingleton<IResourceExpander, TestDepartmentExpander>();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(blueprint =>
            {
                blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>();
                configure?.Invoke(blueprint);
            });

            if (publicHost is not null)
            {
                builder.Services.Configure<BlueprintHttpOptions>(o => o.PublicHost = publicHost);
            }

            var app = builder.Build();

            if (pathBase is not null)
            {
                app.UsePathBase(pathBase);
            }

            app.UseRouting();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(basePath);

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

            return new RootMetadataHost(app, client, serverErrors);
        }

        public string AbsoluteUri(string path)
        {
            return this.Client.BaseAddress!.GetLeftPart(UriPartial.Authority) + path;
        }

        public async Task<Dictionary<string, string?>> GetRootLinksAsync(string rootPath)
        {
            using var response = await this.Client.GetAsync(rootPath);
            var raw = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the body was {0}", raw);

            using var body = JsonDocument.Parse(raw);

            return body.RootElement.GetProperty("$links")
                .EnumerateObject()
                .ToDictionary(static link => link.Name, static link => link.Value.GetProperty("href").GetString(), StringComparer.Ordinal);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    private sealed class ErrorRecordingLoggerProvider(ConcurrentQueue<Exception> errors) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ErrorRecordingLogger(errors);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ErrorRecordingLogger(ConcurrentQueue<Exception> errors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                errors.Enqueue(exception ?? new InvalidOperationException(formatter(state, exception)));
            }
        }
    }
}

// --- Fixtures, hosted by IntegrationTestBlueprintApp, which removes "Sample" from operation names ---

/// <summary>
/// Root-linked as Signature365's VCard.Gateway operation is, and named with the "Sample" the app removes, so its root
/// link's rel is seen to be the name legacy gave it before the app renamed it: <c>SampleGenerateRootvCard</c>.
/// </summary>
[AllowAnonymous]
[RootLink("/root-metadata/{NonPrefixedTenantId}-{NonPrefixedUserId}.vcf")]
public partial class SampleGenerateRootVCardQuery : IQuery<string>
{
    public string NonPrefixedTenantId { get; set; } = null!;

    public string NonPrefixedUserId { get; set; } = null!;

    public static Task<string> HandleAsync(SampleGenerateRootVCardQuery query)
    {
        return Task.FromResult("BEGIN:VCARD");
    }
}

/// <summary>A root link with an explicit rel, as Signature365's <c>/messages/bulk-delete</c> has.</summary>
[AllowAnonymous]
[RootLink("/root-metadata/bulk-delete", Rel = "bulk-delete")]
public partial class RootMetadataExplicitRelQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataExplicitRelQuery query)
    {
        return Task.FromResult("deleted");
    }
}

/// <summary>An operation with two root links, the second with a rel of its own.</summary>
[AllowAnonymous]
[RootLink("/root-metadata/reports")]
[RootLink("/root-metadata/reports/recent", Rel = "recent-reports")]
public partial class RootMetadataReportsQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataReportsQuery query)
    {
        return Task.FromResult("reports");
    }
}

/// <summary>A resource-less <c>[Link]</c>, which legacy counted as a root link.</summary>
[AllowAnonymous]
[Link(null, "/root-metadata/via-link")]
public partial class RootMetadataViaLinkQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataViaLinkQuery query)
    {
        return Task.FromResult("linked");
    }
}

/// <summary>An unexposed operation, whose root link legacy left out of the document.</summary>
[AllowAnonymous]
[UnexposedOperation]
[RootLink("/root-metadata/hidden")]
public partial class RootMetadataHiddenQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataHiddenQuery query)
    {
        return Task.FromResult("hidden");
    }
}

/// <summary>An operation that needs a user, whose root link the anonymous document still lists.</summary>
[RootLink("/root-metadata/secured")]
public partial class RootMetadataSecuredQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataSecuredQuery query)
    {
        return Task.FromResult("secured");
    }
}

/// <summary>A root link declared without a leading slash, which legacy trimmed in any case.</summary>
[AllowAnonymous]
[RootLink("root-metadata/no-leading-slash")]
public partial class RootMetadataNoLeadingSlashQuery : IQuery<string>
{
    public static Task<string> HandleAsync(RootMetadataNoLeadingSlashQuery query)
    {
        return Task.FromResult("routed");
    }
}
