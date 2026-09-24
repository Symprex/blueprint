using System.Net;
using System.Text;
using System.Text.Json;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that <see cref="BlueprintHttpClient{TApp}"/> reports a failed call as a <see cref="ApiException"/> with the
/// problem's title, type, status and detail, as legacy Signature365's client did: an upstream's problem details are
/// carried through, an upstream 5xx's detail and a connection failure's message are kept out of the detail (and so out
/// of any response a gateway writes from it) but preserved in the inner exception, and a response that is not problem
/// details still maps to a stable problem type.
/// </summary>
/// <remarks>
/// The expected titles, types and details are legacy's, from
/// <c>Symprex.Signature365.Common.Core/Api/BlueprintHttpClient.cs</c> before the framework replaced it.
/// </remarks>
public class BlueprintHttpClientFailureTests : IClassFixture<SampleAppFixture>
{
    private const string InternalHost = "templates.apps-backend.svc.cluster.local";

    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientFailureTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task When_the_remote_server_cannot_be_reached_then_throws_service_unavailable_without_the_host_in_the_detail()
    {
        var connectionError = $"Name or service not known ({InternalHost}:80)";
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => throw new HttpRequestException(connectionError)));

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("The remote server did not respond");
        exception.Type.Should().Be("service_unavailable");
        exception.HttpStatus.Should().Be(500);
        exception.Detail.Should().Be("The remote server did not respond");
        exception.Message.Should().NotContain(InternalHost);
        exception.InnerException.Should().BeOfType<HttpRequestException>().Which.Message.Should().Be(connectionError);
    }

    [Fact]
    public async Task When_the_request_times_out_then_throws_service_timeout()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException());
        var client = this._fixture.CreateClient(new FakeHttpHandler(_ => throw timeout));

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("Timed out waiting for service response");
        exception.Type.Should().Be("service_timeout");
        exception.HttpStatus.Should().Be(408);
        exception.Detail.Should().Be(timeout.Message);
    }

    [Fact]
    public async Task When_a_not_found_response_is_not_json_then_throws_not_found_naming_the_path_but_not_the_host()
    {
        var client = this.CreateClient(HttpStatusCode.NotFound, "<html><body>Not found</body></html>", "text/html", $"https://{InternalHost}");

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "missing" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("The remote server responded Not Found");
        exception.Type.Should().Be("not_found");
        exception.HttpStatus.Should().Be(404);
        exception.Detail.Should().Be("Response from /templates/missing was 404");
    }

    [Fact]
    public async Task When_an_error_response_is_not_json_then_throws_invalid_json_with_the_response_status()
    {
        var client = this.CreateClient(HttpStatusCode.BadGateway, "<html><body>Bad gateway</body></html>", "text/html");

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("The remote server did not produce a valid JSON response");
        exception.Type.Should().Be("invalid_json");
        exception.HttpStatus.Should().Be(502);
    }

    [Fact]
    public async Task When_a_server_error_response_is_problem_details_then_keeps_its_title_and_type_and_moves_its_detail_to_the_inner_exception()
    {
        var leakedDetail = $"Name or service not known ({InternalHost}.:80)";
        var client = this.CreateClient(
            HttpStatusCode.InternalServerError,
            $$"""{"type":"upstream_error","title":"Upstream service failed","status":500,"detail":"{{leakedDetail}}"}""",
            "application/problem+json");

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("Upstream service failed");
        exception.Type.Should().Be("upstream_error");
        exception.HttpStatus.Should().Be(500);
        exception.Detail.Should().Be("The remote server returned an internal error response");
        exception.Message.Should().NotContain(InternalHost);
        exception.InnerException.Should().NotBeNull();
        exception.InnerException!.Message.Should().Be(leakedDetail);
    }

    [Fact]
    public async Task When_a_client_error_response_is_problem_details_then_carries_all_of_it()
    {
        var client = this.CreateClient(
            HttpStatusCode.Conflict,
            """{"type":"template_conflict","title":"Template version conflict","status":409,"detail":"Template 't-1' was changed by someone else.","currentVersion":7}""",
            "application/problem+json");

        var act = () => client.SendAsync(new ArchiveTemplateCommand { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("Template version conflict");
        exception.Type.Should().Be("template_conflict");
        exception.HttpStatus.Should().Be(409);
        exception.Detail.Should().Be("Template 't-1' was changed by someone else.");
        exception.InnerException.Should().BeNull();
        exception.Extensions.Should().ContainKey("currentVersion")
            .WhoseValue.Should().BeOfType<JsonElement>().Which.GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task When_an_error_response_is_json_null_then_throws_response_empty()
    {
        var client = this.CreateClient(HttpStatusCode.BadRequest, "null", "application/json");

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<ApiException>()).Which;
        exception.Title.Should().Be("Response deserialisation failed");
        exception.Type.Should().Be("response_empty");
        exception.HttpStatus.Should().Be(502);
        exception.Detail.Should().Be("The error response could not be deserialised");
    }

    [Fact]
    public async Task Failure_is_still_a_BlueprintApiException_with_the_response_status_and_body()
    {
        // The type the typed client threw before it carried the problem, which callers already catch by status.
        const string body = """{"type":"not_found","title":"Not Found","status":404,"detail":"No such template."}""";
        var client = this.CreateClient(HttpStatusCode.NotFound, body, "application/problem+json");

        var act = () => client.SendAsync<JsonElement>(new GetTemplateQuery { Id = "missing" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.ResponseBody.Should().Be(body);
        exception.Should().BeAssignableTo<ApiException>().Which.Detail.Should().Be("No such template.");
    }

    private BlueprintHttpClient<SampleBlueprintApp> CreateClient(HttpStatusCode status, string body, string mediaType, string baseAddress = "https://localhost")
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });

        var httpClient = new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri(baseAddress) };

        return new BlueprintHttpClient<SampleBlueprintApp>(httpClient, this._fixture.App);
    }
}
