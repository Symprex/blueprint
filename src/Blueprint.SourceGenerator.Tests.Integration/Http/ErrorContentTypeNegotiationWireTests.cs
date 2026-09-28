using System.ComponentModel.DataAnnotations;
using System.Text;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that, with <see cref="BlueprintHttpOptions.NegotiateErrorContentType"/> on, every error response's content type
/// is chosen from the request's <c>Accept</c> header as legacy Blueprint chose it, and that the default is unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http</c>) wrote an error as it wrote a success: an exception, a
/// denial's included, through <c>UnhandledExceptionOperationResultExecutor.cs:40-43</c>, a validation failure through
/// <c>ValidationFailedOperationResultExecutor.cs:29-32</c>, and a failure while writing through
/// <c>OkResultOperationExecutor.cs:48-65</c>. Each reached <c>OkResultOperationExecutor.WriteContentAsync</c> (:75-97),
/// which asked <c>Formatters/DefaultOutputFormatterSelector.cs</c> for a formatter. Signature365 registered only the
/// System.Text.Json one and set neither <c>RespectBrowserAcceptHeader</c> nor <c>ReturnHttpNotAcceptable</c>.
/// </para>
/// <para>
/// The selector cleared the accepted media types when any of them was <c>*/*</c> (:102-114). With none left, it took the
/// formatter's first media type, <c>application/json</c> (:54-61, :121-136;
/// <c>SystemTextJsonResultOutputFormatter.cs:36-40</c>). Otherwise it went through them in descending quality (:116)
/// and took the first that was a subset of <c>application/json</c>, <c>text/json</c> or <c>application/*+json</c>
/// (:138-156; <c>SystemTextJsonResultOutputFormatter.cs:25-30</c>, :43-49). With none, it fell back to
/// <c>application/json</c> (:71-80). The content type written was that <c>MediaType</c>'s <c>ToString()</c>
/// (<c>SystemTextJsonResultOutputFormatter.cs:60</c>). For one made from an <c>Accept</c> segment, that string was the
/// segment's whole buffer, the header line (<c>MediaType.cs:34-37</c>, :71, :347-350; the segment is made at
/// <c>MediaType.cs:292-294</c> over <c>AcceptHeaderParser.cs:36</c>'s line). No charset was ever added.
/// </para>
/// </remarks>
public sealed class ErrorContentTypeNegotiationWireTests
{
    public static TheoryData<string> Errors => new(
        "denial-401",
        "denial-403",
        "validation-422",
        "binding-400",
        "api-exception-404",
        "unhandled-500",
        "unsupported-media-type-415",
        "unwritable-result-500");

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Every_error_is_plain_application_json_when_the_request_has_no_accept_header(string error)
    {
        await using var host = await ErrorHost.StartAsync(negotiate: true);

        var (status, contentType) = await host.SendAsync(error, accept: null);

        status.Should().Be(ExpectedStatus(error));
        contentType.Should().Be("application/json");
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Every_error_is_the_problem_type_an_accept_of_application_problem_json_asks_for(string error)
    {
        await using var host = await ErrorHost.StartAsync(negotiate: true);

        var (status, contentType) = await host.SendAsync(error, accept: "application/problem+json");

        status.Should().Be(ExpectedStatus(error));
        contentType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("*/*", "application/json")]
    [InlineData("application/json", "application/json")]
    [InlineData("text/json", "text/json")]
    [InlineData("application/json, text/plain, */*", "application/json")]
    [InlineData("text/html", "application/json")]
    [InlineData("text/plain, application/problem+json;q=0.5", "text/plain, application/problem+json;q=0.5")]
    public async Task An_error_takes_the_content_type_legacys_negotiation_gave_the_accept_header(string accept, string expected)
    {
        // "*/*" anywhere discards the header (DefaultOutputFormatterSelector.cs:109-113), as in the browser's and axios'
        // defaults. No JSON media type falls back to application/json (:71-80, :121-136). A matched one gives the whole
        // header line it came from (MediaType.cs:34-37, :71, :347-350).
        await using var host = await ErrorHost.StartAsync(negotiate: true);

        var (status, contentType) = await host.SendAsync("validation-422", accept);

        status.Should().Be(422);
        contentType.Should().Be(expected);
    }

    [Fact]
    public async Task An_accept_value_that_throws_something_other_than_format_or_overflow_is_still_skipped()
    {
        // "application/json; q=" throws ArgumentOutOfRangeException from MediaType.CreateMediaTypeSegmentWithQuality (an
        // empty parameter value), not FormatException or OverflowException. Legacy's AcceptHeaderParser.TryParseValue
        // caught everything (AcceptHeaderParser.cs:87-95 at Signature365 8b40ecc961) and skipped the unparseable value,
        // same as any other unparseable one — it did not crash the request.
        await using var host = await ErrorHost.StartAsync(negotiate: true);

        var (status, contentType) = await host.SendAsync("validation-422", "application/json; q=");

        status.Should().Be(422);
        contentType.Should().Be("application/json");
    }

    [Fact]
    public async Task The_body_is_still_the_problem_it_was()
    {
        await using var host = await ErrorHost.StartAsync(negotiate: true);

        var (_, _, body) = await host.SendWithBodyAsync("denial-403", accept: null);

        body.Should().Be("""{"type":"unauthorized","title":"You may not see this","status":403}""");
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Without_the_switch_every_error_is_still_application_problem_json(string error)
    {
        await using var host = await ErrorHost.StartAsync(negotiate: false);

        var (status, contentType) = await host.SendAsync(error, accept: "application/json");

        status.Should().Be(ExpectedStatus(error));
        contentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task Accept_header_lines_are_taken_in_descending_quality_across_lines()
    {
        // Legacy parsed every Accept line (AcceptHeaderParser.cs:33-57), sorted the whole list by quality
        // (DefaultOutputFormatterSelector.cs:116) and took the first JSON one. HttpClient joins header values into one
        // line, so this is proven on the executor, with a request carrying two lines.
        var app = new IntegrationTestBlueprintApp();
        var descriptor = app.FindOperation(typeof(GetErrorContentTypeQuery))!;
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = new StringValues(["application/problem+json;q=0.5", "text/json"]);
        httpContext.Response.Body = new MemoryStream();
        var context = new ApiOperationContext(new ServiceCollection().BuildServiceProvider(), app, descriptor, CancellationToken.None)
        {
            HttpContext = httpContext,
        };

        var written = await new HttpOperationResultExecutor().TryExecuteAsync(
            httpContext,
            context,
            new UnhandledExceptionResult(new NotFoundException("No such widget")),
            new BlueprintHttpOptions { NegotiateErrorContentType = true });

        written.Should().BeTrue();
        httpContext.Response.ContentType.Should().Be("text/json");
    }

    private static int ExpectedStatus(string error)
    {
        return int.Parse(error[(error.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Denies <see cref="GetErrorContentTypeQuery"/> when it asks to be denied.</summary>
    private sealed class ErrorOutcomeAuthoriser : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(GetErrorContentTypeQuery);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ((GetErrorContentTypeQuery)operation).Outcome == "denial-403"
                ? Task.FromResult(ExecutionAllowed.No("internal", "You may not see this", ExecutionAllowedFailureType.Authorisation))
                : ExecutionAllowed.YesTask;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    /// <summary>The integration app, served by Kestrel, with or without error content-type negotiation.</summary>
    private sealed class ErrorHost : IAsyncDisposable
    {
        private const string TestUserHeader = "X-Test-User";

        private readonly WebApplication _app;
        private readonly System.Net.Http.HttpClient _client;

        private ErrorHost(WebApplication app, System.Net.Http.HttpClient client)
        {
            this._app = app;
            this._client = client;
        }

        public static async Task<ErrorHost> StartAsync(bool negotiate)
        {
            var builder = WebApplication.CreateSlimBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton<IApiAuthoriser, ErrorOutcomeAuthoriser>();
            builder.Services.Configure<BlueprintHttpOptions>(options => options.NegotiateErrorContentType = negotiate);
            builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

            var app = builder.Build();

            app.Use(static (httpContext, next) =>
            {
                if (httpContext.Request.Headers.ContainsKey(TestUserHeader))
                {
                    httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "user-1")],
                        authenticationType: "Test"));
                }

                return next(httpContext);
            });

            app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

            await app.StartAsync();

            var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/api/") };

            return new ErrorHost(app, client);
        }

        public async Task<(int Status, string? ContentType)> SendAsync(string error, string? accept)
        {
            var (status, contentType, _) = await this.SendWithBodyAsync(error, accept);

            return (status, contentType);
        }

        public async Task<(int Status, string? ContentType, string Body)> SendWithBodyAsync(string error, string? accept)
        {
            using var request = CreateRequest(error);

            if (error != "denial-401")
            {
                request.Headers.Add(TestUserHeader, "user-1");
            }

            if (accept is not null)
            {
                request.Headers.TryAddWithoutValidation("Accept", accept);
            }

            using var response = await this._client.SendAsync(request);

            // The raw header, as written: HttpClient's parsed Content-Type would normalise it.
            var contentType = response.Content.Headers.TryGetValues("Content-Type", out var values) ? string.Join(",", values) : null;

            return ((int)response.StatusCode, contentType, await response.Content.ReadAsStringAsync());
        }

        public async ValueTask DisposeAsync()
        {
            this._client.Dispose();
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }

        private static System.Net.Http.HttpRequestMessage CreateRequest(string error)
        {
            return error switch
            {
                "validation-422" => new(System.Net.Http.HttpMethod.Get, "error-content-types/validation-422"),
                "binding-400" => new(System.Net.Http.HttpMethod.Get, "error-content-types/binding-400?required=x&count=not-a-number"),
                "unsupported-media-type-415" => new(System.Net.Http.HttpMethod.Post, "error-content-types")
                {
                    Content = new System.Net.Http.StringContent("name=x", Encoding.UTF8, "text/plain"),
                },
                "unwritable-result-500" => new(System.Net.Http.HttpMethod.Get, "error-content-types/unwritable"),
                _ => new(System.Net.Http.HttpMethod.Get, $"error-content-types/{error}?required=x"),
            };
        }
    }
}

/// <summary>A query that fails in the way its <see cref="Outcome"/> names.</summary>
[RootLink("/error-content-types/{Outcome}")]
public partial class GetErrorContentTypeQuery : IQuery<string>
{
    public string Outcome { get; set; } = null!;

    [Required]
    public string? Required { get; set; }

    public int? Count { get; set; }

    public static string Handle(GetErrorContentTypeQuery query)
    {
        return query.Outcome switch
        {
            "api-exception-404" => throw new NotFoundException("No such widget"),
            "unhandled-500" => throw new InvalidOperationException("The widget store is unavailable"),
            _ => "ok",
        };
    }
}

/// <summary>A command that takes a JSON body, so any other content type is refused.</summary>
[RootLink("/error-content-types")]
[HttpPost]
public partial class PostErrorContentTypeCommand : ICommand<string>
{
    public string? Name { get; set; }

    public static string Handle(PostErrorContentTypeCommand command)
    {
        return "ok";
    }
}

/// <summary>
/// A query whose result throws while it is being written. Its handler completes asynchronously, so the failure reaches the
/// HTTP host's handler for an exception that escaped the pipeline.
/// </summary>
[RootLink("/error-content-types/unwritable")]
public partial class GetUnwritableErrorContentTypeQuery : IQuery<Blueprint.Http.HttpResult>
{
    public static async Task<Blueprint.Http.HttpResult> HandleAsync(GetUnwritableErrorContentTypeQuery query)
    {
        await Task.Yield();

        return new UnwritableErrorContentResult();
    }
}

/// <summary>A result that fails while it is written, so the failure escapes the pipeline to the HTTP host.</summary>
internal sealed class UnwritableErrorContentResult : Blueprint.Http.HttpResult
{
    public UnwritableErrorContentResult()
        : base(System.Net.HttpStatusCode.OK)
    {
    }

    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new InvalidOperationException("This result cannot be written");
    }
}
