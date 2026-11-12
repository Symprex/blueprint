using System.Net.Http.Headers;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a route placeholder written with one of ASP.NET's inline forms — optional (<c>{Ext?}</c>), defaulted
/// (<c>{Kind=all}</c>), constrained (<c>{Id:int}</c>) or catch-all (<c>{*rest}</c>) — binds its property from the route
/// through the real router, and that the OpenAPI document and the input schema describe it as a path parameter.
/// </summary>
/// <remarks>
/// The generator used to read a placeholder's name only from <c>{Name}</c>, <c>{*Name}</c> and legacy's
/// <c>{Name:ResourceProperty}</c> / <c>{Name(format)}</c>, so an optional or defaulted placeholder named no property: the
/// property was bound from the query string instead, the route's value never reached the handler, and the document listed
/// it as a query parameter. Found by Blueprint.Mcp's comparison of MCP tool calls with HTTP (Symprex/blueprint#110).
/// </remarks>
public sealed class InlineRoutePlaceholderWireTests
{
    [Fact]
    public async Task An_optional_placeholder_binds_its_property_from_the_path_when_the_url_has_it()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/files/abc.txt");

        status.Should().Be(200);
        body.Should().Be("\"name=abc;ext=txt\"");
    }

    [Fact]
    public async Task An_optional_placeholder_leaves_its_property_null_when_the_url_omits_it()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/files/abc");

        status.Should().Be(200);
        body.Should().Be("\"name=abc;ext=(none)\"");
    }

    [Fact]
    public async Task A_defaulted_placeholder_binds_the_routes_default_when_the_url_omits_it()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/kinds");

        status.Should().Be(200);
        body.Should().Be("\"kind=all\"");
    }

    [Fact]
    public async Task A_defaulted_placeholder_binds_the_urls_value_when_the_url_has_it()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/kinds/recent");

        status.Should().Be(200);
        body.Should().Be("\"kind=recent\"");
    }

    [Fact]
    public async Task A_constrained_placeholder_binds_its_property_from_the_path()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/numbers/42");

        status.Should().Be(200);
        body.Should().Be("\"id=42\"");
    }

    [Fact]
    public async Task A_lower_case_catch_all_placeholder_binds_its_property_from_the_path()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/rest/a/b/c");

        status.Should().Be(200);
        body.Should().Be("\"rest=a/b/c\"");
    }

    [Theory]
    [InlineData("txt", "/inline-placeholders/files/abc.txt")]
    [InlineData(null, "/inline-placeholders/files/abc")]
    public void The_typed_client_leaves_an_optional_placeholder_and_its_period_out_when_it_is_null(string? ext, string expected)
    {
        var url = InlinePlaceholderFileQuery.Http.BuildUrl(new InlinePlaceholderFileQuery { Name = "abc", Ext = ext });

        url.Should().Be(expected);
    }

    [Theory]
    [InlineData("recent", "/inline-placeholders/kinds/recent")]
    [InlineData(null, "/inline-placeholders/kinds/all")]
    public void The_typed_client_sends_a_defaulted_placeholders_default_when_it_is_null(string? kind, string expected)
    {
        var url = InlinePlaceholderKindQuery.Http.BuildUrl(new InlinePlaceholderKindQuery { Kind = kind });

        url.Should().Be(expected);
    }

    [Fact]
    public void The_typed_client_sends_the_default_of_a_defaulted_placeholder_a_literal_segment_follows()
    {
        // Leaving the segment out would build /sections/details, which the route cannot match.
        var url = InlinePlaceholderSectionQuery.Http.BuildUrl(new InlinePlaceholderSectionQuery());

        url.Should().Be("/inline-placeholders/sections/all/details");
    }

    [Theory]
    [InlineData(null, null, "/inline-placeholders/reports")]
    [InlineData(2026, null, "/inline-placeholders/reports/2026")]
    [InlineData(2026, 12, "/inline-placeholders/reports/2026/12")]
    public void The_typed_client_leaves_out_a_run_of_trailing_optional_placeholders(int? year, int? month, string expected)
    {
        var url = InlinePlaceholderReportQuery.Http.BuildUrl(new InlinePlaceholderReportQuery { Year = year, Month = month });

        url.Should().Be(expected);
    }

    [Fact]
    public void The_typed_client_builds_no_url_for_an_optional_placeholder_left_out_before_one_that_is_set()
    {
        // /reports/12 would bind 12 to Year, so there is no URL that carries Month without Year.
        var url = InlinePlaceholderReportQuery.Http.BuildUrl(new InlinePlaceholderReportQuery { Month = 12 });

        url.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, "/inline-placeholders/revisions/abc")]
    [InlineData("txt", null, "/inline-placeholders/revisions/abc.txt")]
    [InlineData(null, "2", "/inline-placeholders/revisions/abc/2")]
    [InlineData("txt", "2", "/inline-placeholders/revisions/abc.txt/2")]
    public void The_typed_client_leaves_out_an_optional_placeholder_that_shares_its_segment_before_one_that_is_set(string? ext, string? revision, string expected)
    {
        // Name keeps the segment, so Revision cannot move into Ext's place; /revisions/abc/2 used to build no URL.
        var url = InlinePlaceholderRevisionQuery.Http.BuildUrl(new InlinePlaceholderRevisionQuery { Name = "abc", Ext = ext, Revision = revision });

        url.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "2", "name=abc;ext=(none);revision=2")]
    [InlineData("txt", "2", "name=abc;ext=txt;revision=2")]
    public async Task A_url_the_typed_client_builds_without_an_optional_placeholder_in_a_shared_segment_reaches_the_handler(string? ext, string? revision, string expected)
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var url = InlinePlaceholderRevisionQuery.Http.BuildUrl(new InlinePlaceholderRevisionQuery { Name = "abc", Ext = ext, Revision = revision });
        var (status, body) = await host.GetAsync(url!.TrimStart('/'));

        status.Should().Be(200);
        body.Should().Be("\"" + expected + "\"");
    }

    [Fact]
    public async Task A_legacy_format_with_parentheses_binds_its_property_from_the_path()
    {
        // The placeholder pattern used to stop at the format's first ')', so Price was bound from the query string instead.
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync("inline-placeholders/prices/1.50");

        status.Should().Be(200);
        body.Should().Be("\"price=1.50\"");
    }

    [Theory]
    [InlineData(null, "/inline-placeholders/labels/%28any%29")]
    [InlineData("x", "/inline-placeholders/labels/x")]
    public void The_typed_client_reads_a_default_in_parentheses(string? label, string expected)
    {
        var url = InlinePlaceholderLabelQuery.Http.BuildUrl(new InlinePlaceholderLabelQuery { Label = label });

        url.Should().Be(expected);
    }

    [Fact]
    public void The_typed_client_reads_a_default_after_a_constraint_with_arguments()
    {
        var url = InlinePlaceholderTagQuery.Http.BuildUrl(new InlinePlaceholderTagQuery());

        url.Should().Be("/inline-placeholders/tags/%28any%29");
    }

    [Theory]
    [InlineData("kind=all")]
    [InlineData("section=all")]
    [InlineData("label=(any)")]
    [InlineData("tag=(any)")]
    public async Task A_url_the_typed_client_builds_for_a_null_defaulted_placeholder_reaches_the_handler_with_the_default(string expected)
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var url = expected.Split('=')[0] switch
        {
            "kind" => InlinePlaceholderKindQuery.Http.BuildUrl(new InlinePlaceholderKindQuery()),
            "section" => InlinePlaceholderSectionQuery.Http.BuildUrl(new InlinePlaceholderSectionQuery()),
            "label" => InlinePlaceholderLabelQuery.Http.BuildUrl(new InlinePlaceholderLabelQuery()),
            _ => InlinePlaceholderTagQuery.Http.BuildUrl(new InlinePlaceholderTagQuery()),
        };
        var (status, body) = await host.GetAsync(url!.TrimStart('/'));

        status.Should().Be(200);
        body.Should().Be("\"" + expected + "\"");
    }

    [Theory]
    [InlineData("inline-placeholders/labels", "label=(any)")]
    [InlineData("inline-placeholders/labels/x", "label=x")]
    [InlineData("inline-placeholders/tags", "tag=(any)")]
    [InlineData("inline-placeholders/reports/2026/12", "year=2026;month=12")]
    public async Task A_placeholder_with_parentheses_or_an_equals_sign_binds_its_property_from_the_path(string url, string expected)
    {
        await using var host = await InlinePlaceholderHost.StartAsync();

        var (status, body) = await host.GetAsync(url);

        status.Should().Be(200);
        body.Should().Be("\"" + expected + "\"");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_document_lists_an_optional_placeholder_as_an_optional_path_parameter(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        var parameters = Parameters(document, "/inline-placeholders/files/{Name}.{Ext}");

        parameters.Should().BeEquivalentTo([("Name", "path", true), ("Ext", "path", false)]);
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_document_lists_a_defaulted_placeholder_as_an_optional_path_parameter(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Parameters(document, "/inline-placeholders/kinds/{Kind}").Should().BeEquivalentTo([("Kind", "path", false)]);
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_document_lists_a_constrained_placeholder_as_a_required_path_parameter(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Parameters(document, "/inline-placeholders/numbers/{Id:int}").Should().BeEquivalentTo([("Id", "path", true)]);
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_document_keys_routes_that_differ_only_by_constraint_as_separate_paths(OpenApiSchemaNaming naming)
    {
        // Keying both as /items/{Id} used to merge them into one path, which kept only one of the two operations.
        using var document = Render(naming);

        Parameters(document, "/inline-placeholders/items/{Id:int}").Should().BeEquivalentTo([("Id", "path", true)]);
        Parameters(document, "/inline-placeholders/items/{Id:guid}").Should().BeEquivalentTo([("Id", "path", true)]);
    }

    [Fact]
    public void An_equals_sign_in_a_constraints_argument_is_no_default()
    {
        // Read as a default, it used to make the document and the input schema say a request may leave Code out, which
        // the router cannot match. The integration app is served by slim hosts, which leave out the regex constraint, so
        // this reads the template directly rather than declaring an operation for it.
        OpenApiOperationRenderingContext.GetOptionalRouteParameterNames(["/codes/{Code:regex(^a=b$)}"]).Should().BeEmpty();
        OpenApiOperationRenderingContext.ToPathKey("codes/{Code:regex(^a=b$)}").Should().Be("/codes/{Code:regex(^a=b$)}");
    }

    [Fact]
    public void A_default_after_a_constraint_whose_argument_has_parentheses_is_a_default()
    {
        OpenApiOperationRenderingContext.GetOptionalRouteParameterNames(["/tags/{Tag:regex(^(a|b)$)=(any)}"]).Should().Equal("Tag");
        OpenApiOperationRenderingContext.ToPathKey("tags/{Tag:regex(^(a|b)$)=(any)}").Should().Be("/tags/{Tag:regex(^(a|b)$)}");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void The_document_lists_a_placeholder_with_a_default_in_parentheses_as_optional(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Parameters(document, "/inline-placeholders/labels/{Label}").Should().BeEquivalentTo([("Label", "path", false)]);
        Parameters(document, "/inline-placeholders/tags/{Tag:minlength(1)}").Should().BeEquivalentTo([("Tag", "path", false)]);
    }

    [Fact]
    public async Task The_input_schema_reads_an_optional_and_a_defaulted_placeholder_from_the_path()
    {
        await using var host = await InlinePlaceholderHost.StartAsync();
        var provider = host.Services.GetRequiredService<IOperationInputSchemaProvider>();

        // In the document's order: by location, then name.
        provider.GetSchema("InlinePlaceholderFile")!.Properties.Should().Equal(
            new OperationInputProperty("Ext", OperationInputLocation.Path),
            new OperationInputProperty("Name", OperationInputLocation.Path));
        provider.GetSchema("InlinePlaceholderKind")!.Properties.Should().Equal(
            new OperationInputProperty("Kind", OperationInputLocation.Path));
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new Generators.OperationDescriptors.IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }

    private static List<(string Name, string In, bool Required)> Parameters(JsonDocument document, string path)
    {
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty(path, out var pathItem).Should().BeTrue(
            "the document should key the path {0}; it has {1}",
            path,
            string.Join(", ", paths.EnumerateObject().Select(static p => p.Name).Where(static p => p.Contains("inline-placeholders", StringComparison.Ordinal))));

        return [.. pathItem.GetProperty("get").GetProperty("parameters").EnumerateArray().Select(static p => (
            p.GetProperty("name").GetString()!,
            p.GetProperty("in").GetString()!,
            p.TryGetProperty("required", out var required) && required.GetBoolean()))];
    }

    /// <summary>The integration app, registered and mapped as a consumer does, served by Kestrel.</summary>
    private sealed class InlinePlaceholderHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private InlinePlaceholderHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public IServiceProvider Services => this._app.Services;

        public static async Task<InlinePlaceholderHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<Generators.OperationDescriptors.IntegrationTestBlueprintApp, Generators.OperationDescriptors.IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<Generators.OperationDescriptors.IntegrationTestBlueprintApp, Generators.OperationDescriptors.IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new InlinePlaceholderHost(app, client);
        }

        public async Task<(int Status, string Body)> GetAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

/// <summary>A GET whose extension is an optional placeholder, after a period in the same segment.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/files/{Name}.{Ext?}")]
public partial class InlinePlaceholderFileQuery : IQuery<string>
{
    public string Name { get; set; } = null!;

    public string? Ext { get; set; }

    public static string Handle(InlinePlaceholderFileQuery query)
    {
        return "name=" + query.Name + ";ext=" + (query.Ext ?? "(none)");
    }
}

/// <summary>A GET whose last segment is a placeholder with a default value.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/kinds/{Kind=all}")]
public partial class InlinePlaceholderKindQuery : IQuery<string>
{
    public string? Kind { get; set; }

    public static string Handle(InlinePlaceholderKindQuery query)
    {
        return "kind=" + (query.Kind ?? "(none)");
    }
}

/// <summary>A GET whose placeholder carries an ASP.NET route constraint.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/numbers/{Id:int}")]
public partial class InlinePlaceholderNumberQuery : IQuery<string>
{
    public int Id { get; set; }

    public static string Handle(InlinePlaceholderNumberQuery query)
    {
        return "id=" + query.Id;
    }
}

/// <summary>A GET whose catch-all placeholder is spelled in lower case, unlike its property.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/rest/{*rest}")]
public partial class InlinePlaceholderRestQuery : IQuery<string>
{
    public string Rest { get; set; } = null!;

    public static string Handle(InlinePlaceholderRestQuery query)
    {
        return "rest=" + query.Rest;
    }
}

/// <summary>A GET whose defaulted placeholder is followed by a literal segment.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/sections/{Kind=all}/details")]
public partial class InlinePlaceholderSectionQuery : IQuery<string>
{
    public string? Kind { get; set; }

    public static string Handle(InlinePlaceholderSectionQuery query)
    {
        return "section=" + (query.Kind ?? "(none)");
    }
}

/// <summary>A GET with an optional placeholder after a period, then an optional segment.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/revisions/{Name}.{Ext?}/{Revision?}")]
public partial class InlinePlaceholderRevisionQuery : IQuery<string>
{
    public string Name { get; set; } = null!;

    public string? Ext { get; set; }

    public string? Revision { get; set; }

    public static string Handle(InlinePlaceholderRevisionQuery query)
    {
        return "name=" + query.Name + ";ext=" + (query.Ext ?? "(none)") + ";revision=" + (query.Revision ?? "(none)");
    }
}

/// <summary>A GET whose self link formats its placeholder with a legacy format that holds parentheses.</summary>
[AllowAnonymous]
[SelfLink(typeof(InlinePlaceholderPriceResource), "/inline-placeholders/prices/{Price(0.00;(0.00))}")]
public partial class InlinePlaceholderPriceQuery : IQuery<string>
{
    public string Price { get; set; } = null!;

    public static string Handle(InlinePlaceholderPriceQuery query)
    {
        return "price=" + query.Price;
    }
}

/// <summary>The resource <see cref="InlinePlaceholderPriceQuery"/> links to.</summary>
public sealed class InlinePlaceholderPriceResource : ApiResource
{
    public override string Object => "inline_placeholder_price";

    public decimal Price { get; set; }
}

/// <summary>A GET whose last two segments are optional placeholders.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/reports/{Year?}/{Month?}")]
public partial class InlinePlaceholderReportQuery : IQuery<string>
{
    public int? Year { get; set; }

    public int? Month { get; set; }

    public static string Handle(InlinePlaceholderReportQuery query)
    {
        return "year=" + query.Year + ";month=" + query.Month;
    }
}

/// <summary>A GET whose placeholder's default is written in parentheses.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/labels/{Label=(any)}")]
public partial class InlinePlaceholderLabelQuery : IQuery<string>
{
    public string? Label { get; set; }

    public static string Handle(InlinePlaceholderLabelQuery query)
    {
        return "label=" + (query.Label ?? "(none)");
    }
}

/// <summary>A GET whose placeholder has a default after a constraint with arguments.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/tags/{Tag:minlength(1)=(any)}")]
public partial class InlinePlaceholderTagQuery : IQuery<string>
{
    public string? Tag { get; set; }

    public static string Handle(InlinePlaceholderTagQuery query)
    {
        return "tag=" + (query.Tag ?? "(none)");
    }
}

/// <summary>A GET at the same path as <see cref="InlinePlaceholderGuidItemQuery"/> but for a different constraint.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/items/{Id:int}")]
public partial class InlinePlaceholderIntItemQuery : IQuery<string>
{
    public int Id { get; set; }

    public static string Handle(InlinePlaceholderIntItemQuery query)
    {
        return "int=" + query.Id;
    }
}

/// <summary>A GET at the same path as <see cref="InlinePlaceholderIntItemQuery"/> but for a different constraint.</summary>
[AllowAnonymous]
[RootLink("/inline-placeholders/items/{Id:guid}")]
public partial class InlinePlaceholderGuidItemQuery : IQuery<string>
{
    public Guid Id { get; set; }

    public static string Handle(InlinePlaceholderGuidItemQuery query)
    {
        return "guid=" + query.Id;
    }
}

