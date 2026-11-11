using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// <see cref="BlueprintHttpClient.ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/> sends an inner operation through its own generated request
/// builders, with no app or descriptor, and reads the upstream outcome as the result the caller declares: an upstream 404 the
/// host wrote as a <see cref="NotFoundResult{T}"/> becomes that member of a declared <see cref="Results{T1, T2}"/>, and every
/// other failure escapes as the <see cref="BlueprintApiException"/> the transport raises.
/// </summary>
/// <remarks>
/// Responses are shaped as <c>HttpOperationResultExecutor</c> writes them: a <see cref="NotFoundResult{T}"/> as its content,
/// JSON, at 404; a thrown not-found as a problem document at 404.
/// </remarks>
public class BlueprintHttpClientForwardTests
{
    private const string TemplateJson = """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""";

    [Fact]
    public async Task When_forwarding_a_get_then_it_is_sent_with_the_inner_operations_own_method_and_url()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        var result = await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t 1" });

        sent!.Method.Should().Be(HttpMethod.Get);
        sent.RequestUri!.AbsoluteUri.Should().Be("https://upstream.example/api/templates/t%201");
        sent.Content.Should().BeNull("a GET carries its properties in its URL");
        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_forwarding_a_command_then_its_body_is_the_inner_operation()
    {
        string? body = null;
        HttpMethod? method = null;
        var client = CreateClient(request =>
        {
            method = request.Method;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            return Json(HttpStatusCode.Created, $$"""{"$object":"event","eventId":"templateCreated","changeType":"Created","resourceObject":"template","resourceId":"t-1","data":{{TemplateJson}}}""");
        });

        await client.ForwardAsync<CreateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            new CreateTemplateCommand { Name = "Forwarded", HtmlSource = "<p>f</p>", PlainTextSource = "f" });

        method.Should().Be(HttpMethod.Post);
        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("name").GetString().Should().Be("Forwarded");
        document.RootElement.GetProperty("htmlSource").GetString().Should().Be("<p>f</p>");
    }

    [Fact]
    public async Task When_the_upstream_answers_its_not_found_result_then_it_becomes_the_declared_NotFoundResult()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.NotFound, """{"id":"missing","name":"","htmlSource":"","plainTextSource":""}"""));

        var result = await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Id.Should().Be("missing");
    }

    [Fact]
    public async Task When_the_not_found_result_is_the_first_member_then_the_404_still_becomes_it()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.NotFound, """{"id":"missing","name":"","htmlSource":"","plainTextSource":""}"""));

        var result = await client.ForwardAsync<GetTemplateQuery, Results<NotFoundResult<TemplateApiResource>, OkResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Id.Should().Be("missing");
    }

    [Fact]
    public async Task When_the_upstream_404_is_a_problem_then_it_escapes_even_with_a_declared_NotFoundResult()
    {
        // A thrown not-found is a problem, not the resource's content: reading it as a NotFoundResult would invent a resource.
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"type":"not_found","title":"The requested resource could not be found","status":404,"detail":"Template 'missing' not found."}""",
                Encoding.UTF8,
                "application/problem+json"),
        });

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Type.Should().Be("not_found");
    }

    [Fact]
    public async Task When_no_NotFoundResult_is_declared_then_an_upstream_404_escapes_unchanged()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.NotFound, """{"id":"missing","name":"","htmlSource":"","plainTextSource":""}"""));

        var act = () => client.ForwardAsync<GetTemplateQuery, TemplateApiResource>(new GetTemplateQuery { Id = "missing" });

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_the_upstream_fails_otherwise_then_the_exception_escapes_unchanged()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"type":"forbidden","title":"Forbidden","status":403}""", Encoding.UTF8, "application/problem+json"),
        });

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-1" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exception.Type.Should().Be("forbidden");
    }

    [Fact]
    public async Task When_the_inner_resource_carries_links_then_the_forwarded_resource_arrives_without_them()
    {
        // D6: a gateway regenerates links from its own routes, so the inner service's, which name the inner host, never pass.
        var client = CreateClient(static _ => Json(
            HttpStatusCode.OK,
            """{"$object":"template","$links":{"self":{"href":"https://inner.internal/api/templates/t-1","type":"template"}},"id":"t-1","name":"Linked","htmlSource":"","plainTextSource":""}"""));

        var result = await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-1" });

        result.AsT1.Content.Name.Should().Be("Linked");
        result.AsT1.Content.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_options_populate_read_only_members_then_a_forwarded_resource_still_arrives_without_inner_links()
    {
        // Populate fills a get-only collection in place, which once kept the inner $links that D6 drops.
        var client = CreateClient(
            static _ => Json(HttpStatusCode.OK, LinkedTemplateJson("t-1", "Linked")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate });

        var result = await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-1" });

        result.AsT1.Content.Name.Should().Be("Linked");
        result.AsT1.Content.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_options_populate_read_only_members_then_a_not_found_resource_still_arrives_without_inner_links()
    {
        var client = CreateClient(
            static _ => Json(HttpStatusCode.NotFound, LinkedTemplateJson("missing", string.Empty)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate });

        var result = await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        result.AsT2.Content.Id.Should().Be("missing");
        result.AsT2.Content.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task When_forwarding_then_it_asks_for_errors_as_problem_json()
    {
        // A host with NegotiateErrorContentType chooses an error's content type from Accept; asking for problem JSON is what
        // tells a thrown 404 from a NotFoundResult's content (ErrorContentTypeNegotiationWireTests proves the host side).
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-1" });

        sent!.Headers.Accept.Select(static a => a.ToString()).Should().Equal("application/problem+json");
    }

    [Fact]
    public async Task When_the_caller_sets_its_own_accept_then_the_forward_still_asks_for_errors_as_problem_json()
    {
        // A caller's Accept once survived, and with it a negotiating host's thrown 404 came back as plain JSON.
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;

            return Json(HttpStatusCode.OK, TemplateJson);
        });

        await client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "t-1" },
            new CallOptions { ModifyRequest = static request => request.Headers.Accept.ParseAdd("application/json") });

        sent!.Headers.Accept.Select(static a => a.ToString()).Should().Equal("application/problem+json");
    }

    [Fact]
    public async Task When_the_caller_asks_for_plain_json_then_a_negotiating_upstreams_thrown_404_still_escapes_as_the_upstream_problem()
    {
        var client = CreateClient(static request => NegotiatedError(
            request,
            HttpStatusCode.NotFound,
            """{"type":"not_found","title":"The requested resource could not be found","status":404,"detail":"Template 'missing' not found."}"""));

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" },
            new CallOptions { ModifyRequest = static request => request.Headers.Accept.ParseAdd("application/json") });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.Type.Should().Be("not_found");
        exception.Detail.Should().Be("Template 'missing' not found.");
    }

    [Fact]
    public async Task When_a_negotiating_upstream_throws_a_typed_404_then_it_escapes_as_the_upstream_problem()
    {
        // With NegotiateErrorContentType on, a forward that asked for nothing got this problem as application/json, which
        // was once read as a default-valued resource and turned into a NotFoundResult, losing the problem's type and detail.
        var client = CreateClient(static request => NegotiatedError(
            request,
            HttpStatusCode.NotFound,
            """{"type":"not_found","title":"The requested resource could not be found","status":404,"detail":"Template 'missing' not found."}"""));

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Type.Should().Be("not_found");
        exception.Detail.Should().Be("Template 'missing' not found.");
    }

    [Fact]
    public async Task When_a_negotiating_upstream_throws_a_message_only_ApiException_404_then_it_escapes_as_the_upstream_problem()
    {
        // new ApiException("Resource not found", 404) writes a problem with no type, which a check of the body's shape for
        // type, title and status once missed, reading it as a default-valued resource.
        var client = CreateClient(static request => NegotiatedError(
            request,
            HttpStatusCode.NotFound,
            """{"title":"Resource not found","status":404,"detail":"Resource not found"}"""));

        var act = () => client.ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            new GetTemplateQuery { Id = "missing" });

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Title.Should().Be("Resource not found");
        exception.Detail.Should().Be("Resource not found");
    }

    [Fact]
    public async Task When_given_an_instance_of_a_derived_operation_then_it_refuses_to_send_it()
    {
        // D4: a derived instance would be written as its run-time type, leaking the gateway's own properties upstream.
        var sent = false;
        var client = CreateClient(_ =>
        {
            sent = true;

            return Json(HttpStatusCode.Created, "{}");
        });

        var act = () => client.ForwardAsync<CreateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            new GatewayCreateTemplateCommand { Name = "Derived" });

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(nameof(GatewayCreateTemplateCommand));
        sent.Should().BeFalse();
    }

    private static BlueprintHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler, JsonSerializerOptions? jsonOptions = null)
    {
        return new BlueprintHttpClient(
            new System.Net.Http.HttpClient(new FakeHttpHandler(handler))
            {
                BaseAddress = new Uri("https://upstream.example/api/"),
            },
            jsonOptions);
    }

    private static string LinkedTemplateJson(string id, string name)
    {
        return $$$"""{"$object":"template","$links":{"self":{"href":"https://inner.internal/api/templates/{{{id}}}","type":"template"}},"id":"{{{id}}}","name":"{{{name}}}","htmlSource":"","plainTextSource":""}""";
    }

    /// <summary>
    /// An error as a host with <c>NegotiateErrorContentType</c> writes it: as <c>application/problem+json</c> to a request
    /// whose only <c>Accept</c> is that, and as <c>application/json</c> to one with none (<c>LegacyErrorContentTypeNegotiation</c>,
    /// proven on the wire by <c>ErrorContentTypeNegotiationWireTests</c>).
    /// </summary>
    private static HttpResponseMessage NegotiatedError(HttpRequestMessage request, HttpStatusCode status, string json)
    {
        var accept = string.Join(", ", request.Headers.Accept.Select(static a => a.ToString()));
        var contentType = accept == "application/problem+json" ? "application/problem+json" : "application/json";

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, contentType),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
