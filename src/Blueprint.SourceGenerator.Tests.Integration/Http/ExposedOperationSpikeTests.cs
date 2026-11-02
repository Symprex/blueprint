using System.Net;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Spike for Symprex/blueprint#75 (D25): an operation declared only as <c>[Expose&lt;TInner&gt;] partial class</c>, whose
/// inner operation is in a referenced assembly, has the inner operation's properties at runtime, binds a request onto them,
/// and documents them in OpenAPI as the inner operation does.
/// </summary>
public sealed class ExposedOperationSpikeTests
{
    [Fact]
    public async Task An_exposed_operation_binds_the_query_header_and_cookie_onto_its_contributed_properties()
    {
        await using var host = await ExposedHost.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "exposed/templates/search?q=abc&page=2&pageSize=5&sortBy=CreatedDate&tags=a&tags=b");
        request.Headers.Add("X-Correlation-Id", "corr-1");
        request.Headers.Add("Cookie", "pref-locale=en-GB");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("q=abc;page=2;pageSize=5;sortBy=CreatedDate;tags=a,b;correlationId=corr-1;locale=en-GB");
    }

    [Fact]
    public async Task An_exposed_operation_is_validated_with_the_inner_operations_rules()
    {
        await using var host = await ExposedHost.StartAsync();

        using var response = await host.Client.GetAsync("exposed/templates/search?page=1&pageSize=500");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("q").And.Contain("pageSize");
    }

    [Fact]
    public async Task An_inner_initialiser_is_not_carried_to_the_exposed_property()
    {
        // SearchTemplatesQuery.Page is "= 1" and [GreaterThan(0)]. An initialiser is compiled into the inner type's
        // constructor, so the emitted property starts at 0 and a caller who leaves page out fails validation at the
        // gateway, where the inner operation would have used 1. #65 has to decide how defaults are carried.
        await using var host = await ExposedHost.StartAsync();

        using var response = await host.Client.GetAsync("exposed/templates/search?q=abc");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("page");
    }

    [Fact]
    public void An_exposed_operations_OpenAPI_parameters_equal_the_inner_operations()
    {
        var exposed = FindOperation(new IntegrationTestBlueprintApp(), typeof(ExposedSearchTemplatesQuery));
        var inner = FindOperation(new SampleBlueprintApp(), typeof(SearchTemplatesQuery));

        exposed.Parameters.Should().NotBeEmpty();
        exposed.Parameters.Should().BeEquivalentTo(inner.Parameters, static options => options.WithStrictOrdering());
        exposed.RequestBody.Should().BeNull();
    }

    [Fact]
    public void Contributed_property_summaries_cannot_be_read_from_a_project_referenced_assembly_at_compile_time()
    {
        // D15 left open whether GetDocumentationCommentXml() works on metadata symbols in a real build. For a project
        // reference it does not: the compiler is given the contract's reference assembly (obj/refint), which has no XML
        // file beside it, so the model's documentation is null and the descriptor carries no property summaries. The
        // summary here is this class's own.
        var descriptor = ExposedSearchTemplatesQuery.Configure();

        descriptor.TryGetFeatureData<OperationDocumentationFeatureData>(out var documentation).Should().BeTrue();
        documentation!.PropertySummaries.Should().NotContainKey("Q");
    }

    private static OpenApiOperationModel FindOperation(BlueprintApp app, Type operationType)
    {
        var documentContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaContext = OpenApiSchemaProjectionContext.Create(documentContext);
        var responseSchemaContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);

        return operationContext.Operations.Single(operation => operation.OperationTypeName == operationType.FullName);
    }

    /// <summary>The integration app, served by Kestrel.</summary>
    private sealed class ExposedHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ExposedHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this.Client = client;
        }

        public System.Net.Http.HttpClient Client { get; }

        public static async Task<ExposedHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
                static blueprint => blueprint.AddResourceAuthorizationProvider<PermitAllResourceAuthorizationProvider>());

            var app = builder.Build();
            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ExposedHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

/// <summary>
/// Exposes the Sample contract's <see cref="SearchTemplatesQuery"/>, from a referenced assembly, with no properties of its
/// own: every property is contributed by the shared operation property model and emitted by the descriptor generator.
/// </summary>
[Expose<SearchTemplatesQuery>]
[AllowAnonymous]
[RootLink("/exposed/templates/search")]
[HttpGet]
public partial class ExposedSearchTemplatesQuery : IQuery<PlainTextResult>;

/// <summary>A hand-written handler standing in for the forward #65 generates, echoing what was bound.</summary>
public partial class ExposedSearchTemplatesQuery
{
    public static PlainTextResult Handle(ExposedSearchTemplatesQuery query)
    {
        return new PlainTextResult(
            $"q={query.Q};page={query.Page};pageSize={query.PageSize};sortBy={query.SortBy};tags={string.Join(",", query.Tags ?? [])};"
            + $"correlationId={query.CorrelationId};locale={query.PreferredLocale}");
    }
}
