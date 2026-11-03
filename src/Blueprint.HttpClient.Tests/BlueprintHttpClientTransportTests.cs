using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.HttpClient.Readers;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using Blueprint.Validation;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves the concrete <see cref="BlueprintHttpClient"/> transport on its own, with no <see cref="BlueprintApp"/>: its
/// one send primitive takes the HTTP method, the URL and the reader from its caller, which is how a generated typed
/// client calls it, and each reader reads a response from what it was constructed with.
/// </summary>
/// <remarks>
/// The app-driven <see cref="BlueprintHttpClient{TApp}"/> routes through the same primitive and readers; the rest of
/// this project's tests pin that every response it read before still reads the same.
/// </remarks>
public sealed class BlueprintHttpClientTransportTests
{
    private const string TemplateJson = """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""";

    [Fact]
    public async Task A_GET_is_sent_to_the_url_relative_to_the_base_address_path_with_no_body()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(
            req =>
            {
                captured = req;
                return Json(HttpStatusCode.OK, """{"ok":true}""");
            },
            "https://localhost/api/");

        var result = await client.SendAsync(
            HttpMethod.Get,
            "/templates/search?q=hello",
            new SearchTemplatesQuery { Q = "hello", Page = 1, PageSize = 5 },
            operationTypeInfo: null,
            new JsonResponseReader<JsonElement>(),
            "SearchTemplatesQuery",
            options: null);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri.Should().Be(new Uri("https://localhost/api/templates/search?q=hello"), "the leading '/' is relative to the base address's path");
        captured.Content.Should().BeNull("a GET carries its operation in the URL");
        result.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Any_other_method_sends_the_operation_as_its_JSON_body()
    {
        string? mediaType = null;
        string? body = null;
        var client = CreateClient(req =>
        {
            mediaType = req.Content?.Headers.ContentType?.MediaType;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await client.SendAsync(
            HttpMethod.Delete,
            "/templates/t-9",
            new ArchiveTemplateCommand { Id = "t-9" },
            operationTypeInfo: null,
            new BodilessResultResponseReader<object>(),
            "ArchiveTemplateCommand",
            options: null);

        mediaType.Should().Be("application/json");
        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("id").GetString().Should().Be("t-9");
    }

    [Fact]
    public async Task The_body_is_written_with_the_operations_type_info_when_one_is_given()
    {
        string? body = null;
        var client = CreateClient(req =>
        {
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        // The transport's own options are camelCase, so a snake_case key can only have come from the type info.
        var typeInfo = (JsonTypeInfo<CreateTemplateCommand>)SnakeCaseOptions().GetTypeInfo(typeof(CreateTemplateCommand));

        await client.SendAsync(
            HttpMethod.Post,
            "/templates",
            new CreateTemplateCommand { Name = "Snake", HtmlSource = "<p>x</p>" },
            typeInfo,
            new BodilessResultResponseReader<object>(),
            "CreateTemplateCommand",
            options: null);

        using var document = JsonDocument.Parse(body!);
        document.RootElement.TryGetProperty("html_source", out var htmlSource).Should().BeTrue();
        htmlSource.GetString().Should().Be("<p>x</p>");
        document.RootElement.TryGetProperty("htmlSource", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_null_url_throws_before_anything_is_sent()
    {
        var sent = false;
        var client = CreateClient(_ =>
        {
            sent = true;
            return Json(HttpStatusCode.OK, TemplateJson);
        });

        var act = () => client.SendAsync(
            HttpMethod.Get,
            url: null,
            new GetTemplateQuery { Id = null! },
            operationTypeInfo: null,
            new JsonResponseReader<JsonElement>(),
            "GetTemplateQuery",
            options: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Compiled URL builder returned null for 'Blueprint.SourceGenerator.Sample.Contract.Api.GetTemplateQuery'*");
        sent.Should().BeFalse();
    }

    [Fact]
    public async Task The_callers_ModifyRequest_runs_before_the_request_is_sent()
    {
        HttpRequestMessage? captured = null;
        var client = CreateClient(req =>
        {
            captured = req;
            return Json(HttpStatusCode.OK, "{}");
        });

        await client.SendAsync(
            HttpMethod.Get,
            "/templates/search",
            new SearchTemplatesQuery { Q = "x", Page = 1, PageSize = 5 },
            operationTypeInfo: null,
            new JsonResponseReader<JsonElement>(),
            "SearchTemplatesQuery",
            new CallOptions { ModifyRequest = static req => req.Headers.Add("X-Trace", "trace-1") });

        captured!.Headers.GetValues("X-Trace").Should().ContainSingle().Which.Should().Be("trace-1");
    }

    [Fact]
    public async Task An_error_response_is_mapped_before_the_reader_sees_it()
    {
        const string body = """{"type":"not_found","title":"Not Found","status":404,"detail":"No such template."}""";
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });

        var act = () => client.SendAsync(
            HttpMethod.Get,
            "/templates/missing",
            new GetTemplateQuery { Id = "missing" },
            operationTypeInfo: null,
            new StatusCodeResultResponseReader<StatusCodeResult.Accepted>(StatusCodeResult.Accepted.Instance),
            "GetTemplateQuery",
            options: null);

        var exception = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        exception.StatusCode.Should().Be(HttpStatusCode.NotFound);
        exception.Detail.Should().Be("No such template.");
        exception.ResponseBody.Should().Be(body);
    }

    [Fact]
    public async Task A_422_is_a_validation_exception()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"title":"Validation Failed","status":422,"errors":{"Name":["Name is required."]}}""", Encoding.UTF8, "application/problem+json"),
        });

        var act = () => client.SendAsync(
            HttpMethod.Post,
            "/templates",
            new CreateTemplateCommand(),
            operationTypeInfo: null,
            new JsonResponseReader<TemplateApiResource>(),
            "CreateTemplateCommand",
            options: null);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task A_timeout_is_a_RequestTimeout_api_exception()
    {
        var client = CreateClient(static _ => throw new TaskCanceledException("Timeout", new TimeoutException("Request timed out")));

        var act = () => client.SendAsync(
            HttpMethod.Get,
            "/templates/search",
            new SearchTemplatesQuery { Q = "slow", Page = 1, PageSize = 5 },
            operationTypeInfo: null,
            new JsonResponseReader<JsonElement>(),
            "SearchTemplatesQuery",
            options: null);

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.RequestTimeout);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_an_InternalServerError_api_exception()
    {
        var client = CreateClient(static _ => throw new HttpRequestException("Connection refused"));

        var act = () => client.SendAsync(
            HttpMethod.Get,
            "/templates/search",
            new SearchTemplatesQuery { Q = "down", Page = 1, PageSize = 5 },
            operationTypeInfo: null,
            new JsonResponseReader<JsonElement>(),
            "SearchTemplatesQuery",
            options: null);

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task The_status_code_result_reader_returns_the_instance_it_was_given_for_its_own_status()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await SendArchiveAsync(client, new StatusCodeResultResponseReader<StatusCodeResult.Accepted>(StatusCodeResult.Accepted.Instance));

        result.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task The_status_code_result_reader_returns_null_for_another_status()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await SendArchiveAsync(client, new StatusCodeResultResponseReader<StatusCodeResult.Accepted>(StatusCodeResult.Accepted.Instance));

        result.Should().BeNull();
    }

    [Fact]
    public async Task The_plain_text_reader_returns_the_text_with_its_media_type_and_status()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("BEGIN:VCARD\nEND:VCARD", Encoding.UTF8, "text/vcard"),
        });

        var result = await SendArchiveAsync(client, new PlainTextResponseReader());

        result.Content.Should().Be("BEGIN:VCARD\nEND:VCARD");
        result.ContentType.Should().Be("text/vcard");
        result.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_JSON_reader_of_a_string_returns_a_text_body_as_it_is()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not \"json\"", Encoding.UTF8, "text/plain"),
        });

        var result = await SendArchiveAsync(client, new JsonResponseReader<string>());

        result.Should().Be("not \"json\"");
    }

    [Fact]
    public async Task The_JSON_reader_reads_with_the_type_info_it_was_given()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, """{"display_name":"Snake"}"""));
        var typeInfo = (JsonTypeInfo<SnakeCaseProbe>)SnakeCaseOptions().GetTypeInfo(typeof(SnakeCaseProbe));

        var result = await SendArchiveAsync(client, new JsonResponseReader<SnakeCaseProbe>(typeInfo));

        result.DisplayName.Should().Be("Snake", "the transport's camelCase options would not bind display_name");
    }

    [Fact]
    public async Task The_JSON_reader_throws_response_empty_for_a_JSON_null()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, "null"));

        var act = () => SendArchiveAsync(client, new JsonResponseReader<TemplateApiResource>());

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.Type.Should().Be("response_empty");
    }

    [Fact]
    public async Task The_bodiless_result_reader_of_object_returns_the_status_code_singleton_of_a_response_with_no_media_type()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await SendArchiveAsync(client, new BodilessResultResponseReader<object>());

        result.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task The_bodiless_result_reader_of_HttpResult_returns_a_status_only_result()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await SendArchiveAsync(client, new BodilessResultResponseReader<HttpResult>());

        result.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    public static TheoryData<StatusCodeResult> NestedSuccessStatusCodeResults()
    {
        // Every nested singleton StatusCodeResult declares, found the way the client found them before its readers, so a
        // singleton added there and not to the readers' own list fails here. Only a success reaches a reader at all.
        var data = new TheoryData<StatusCodeResult>();

        foreach (var nested in typeof(StatusCodeResult).GetNestedTypes(BindingFlags.Public).Where(static t => typeof(StatusCodeResult).IsAssignableFrom(t)))
        {
            var singleton = (StatusCodeResult)nested.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            if (singleton.StatusCode is >= 200 and <= 299)
            {
                data.Add(singleton);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NestedSuccessStatusCodeResults))]
    public async Task The_bodiless_result_reader_of_the_base_StatusCodeResult_returns_each_nested_singleton_for_its_status(StatusCodeResult expected)
    {
        var client = CreateClient(_ => new HttpResponseMessage((HttpStatusCode)expected.StatusCode));

        var result = await SendArchiveAsync(client, new BodilessResultResponseReader<StatusCodeResult>());

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void The_bodiless_result_reader_refuses_a_type_it_does_not_read()
    {
        var act = static () => new BodilessResultResponseReader<TemplateApiResource>();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_resource_event_reader_makes_the_declared_event_with_only_what_the_wire_carries()
    {
        // No "created" on the wire: a constructor-made event would carry the time it was read instead.
        var client = CreateClient(static _ => Json(
            HttpStatusCode.OK,
            "{\"$object\":\"event\",\"eventId\":\"template.created\",\"changeType\":\"Created\",\"resourceObject\":\"template\",\"data\":" + TemplateJson + "}"));

        var result = await SendArchiveAsync(client, new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>());

        result.Should().BeOfType<ResourceCreated<TemplateApiResource>>();
        result.EventId.Should().Be("template.created");
        result.ChangeType.Should().Be(ResourceEventChangeType.Created);
        result.Created.Should().Be(default, "an event the wire carries no created date for has none");
        result.SelfQuery.Should().BeNull();
        result.Data!.Name.Should().Be("Found Template");
        result.Metadata.Should().BeEmpty("an event the wire carries no metadata for has none");
    }

    [Fact]
    public async Task The_resource_event_reader_makes_each_of_blueprints_own_event_types_as_exactly_that_type()
    {
        // Each of the four is a subclass of the first, so an event of the wrong one of them would still cast.
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, """{"eventId":"template.changed","changeType":"Updated","data":""" + TemplateJson + "}"));

        (await SendArchiveAsync(client, new ResourceEventResponseReader<ResourceEvent<TemplateApiResource>, TemplateApiResource>()))
            .GetType().Should().Be(typeof(ResourceEvent<TemplateApiResource>));
        (await SendArchiveAsync(client, new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>()))
            .GetType().Should().Be(typeof(ResourceCreated<TemplateApiResource>));
        (await SendArchiveAsync(client, new ResourceEventResponseReader<ResourceUpdated<TemplateApiResource>, TemplateApiResource>()))
            .GetType().Should().Be(typeof(ResourceUpdated<TemplateApiResource>));
        (await SendArchiveAsync(client, new ResourceEventResponseReader<ResourceDeleted<TemplateApiResource>, TemplateApiResource>()))
            .GetType().Should().Be(typeof(ResourceDeleted<TemplateApiResource>));
    }

    [Fact]
    public async Task The_resource_event_reader_makes_a_new_event_for_each_read()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, """{"eventId":"template.created","changeType":"Created","data":""" + TemplateJson + "}"));
        var reader = new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>();

        var first = await SendArchiveAsync(client, reader);
        var second = await SendArchiveAsync(client, reader);

        second.Should().NotBeSameAs(first, "a reader shared by every call must not hand two callers one event");
    }

    [Fact]
    public async Task The_resource_event_reader_reads_the_data_with_the_type_info_it_was_given()
    {
        var client = CreateClient(static _ => Json(
            HttpStatusCode.OK,
            """{"eventId":"template.created","changeType":"Created","data":{"id":"t-1","name":"Snake","html_source":"<p>snake</p>","plain_text_source":"x"}}"""));
        var typeInfo = (JsonTypeInfo<TemplateApiResource>)SnakeCaseOptions().GetTypeInfo(typeof(TemplateApiResource));

        var result = await SendArchiveAsync(
            client,
            new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>(typeInfo));

        result.Data!.HtmlSource.Should().Be("<p>snake</p>");
    }

    [Fact]
    public async Task The_resource_event_reader_throws_response_empty_for_a_JSON_null()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, "null"));

        var act = () => SendArchiveAsync(client, new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>());

        (await act.Should().ThrowAsync<BlueprintApiException>()).Which.Type.Should().Be("response_empty");
    }

    [Fact]
    public async Task The_resource_event_reader_makes_an_event_type_declared_elsewhere_without_running_its_constructor()
    {
        // Blueprint's own event types are made by a constructor that sets nothing; a subclass declared elsewhere has none the
        // reader can call, so it is made uninitialised, as legacy's converter made every event: its constructor, which
        // stamps the time it ran, never runs.
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, """{"eventId":"template.archived","changeType":"Deleted","data":""" + TemplateJson + "}"));

        var result = await SendArchiveAsync(client, new ResourceEventResponseReader<TemplateArchivedEvent, TemplateApiResource>());

        result.Should().BeOfType<TemplateArchivedEvent>();
        result.EventId.Should().Be("template.archived");
        result.ChangeType.Should().Be(ResourceEventChangeType.Deleted);
        result.Created.Should().Be(default, "the subclass's constructor did not run");
        result.Data!.Name.Should().Be("Found Template");
    }

    [Fact]
    public void The_resource_event_reader_refuses_an_abstract_event_type_it_could_never_make()
    {
        var act = static () => new ResourceEventResponseReader<AbstractTemplateEvent, TemplateApiResource>();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_resource_event_result_reader_reads_the_envelope()
    {
        var client = CreateClient(static _ => Json(
            HttpStatusCode.Created,
            "{\"$object\":\"event\",\"eventId\":\"template.created\",\"changeType\":\"Created\",\"metadata\":{\"source\":\"import\"},\"correlationId\":\"corr-1\",\"data\":" + TemplateJson + "}"));

        var result = await SendArchiveAsync(client, new ResourceEventResultResponseReader<TemplateApiResource>());

        result.ChangeType.Should().Be(ChangeType.Created);
        result.EventId.Should().Be("template.created");
        result.ResourceId.Should().BeEmpty("the envelope carries no resource id");
        result.Content.Name.Should().Be("Found Template");
        result.Metadata.Should().Equal(new Dictionary<string, object> { ["source"] = "import" });
        result.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task The_union_reader_returns_the_status_only_member_for_a_bodiless_response_of_its_status()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var result = await SendArchiveAsync(client, AcceptedOrText());

        result.IsT1.Should().BeTrue();
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task The_union_reader_returns_the_text_member_for_a_text_body()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("Queued", Encoding.UTF8, "text/plain"),
        });

        var result = await SendArchiveAsync(client, AcceptedOrText());

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Should().Be("Queued");
    }

    [Fact]
    public async Task The_union_reader_throws_when_no_member_claims_the_response()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var act = () => SendArchiveAsync(client, AcceptedOrText());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*204*matches none of*");
    }

    [Fact]
    public async Task The_union_reader_reads_an_ok_member_from_its_content()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, TemplateJson));
        var reader = new UnionResponseReader<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>(
            new ContentResultResponseReader<OkResult<TemplateApiResource>, TemplateApiResource>(),
            new JsonResponseReader<NotFoundResult<TemplateApiResource>>());

        var result = await SendArchiveAsync(client, reader);

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task The_union_reader_reads_a_created_member_from_its_content_at_201()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.Created, TemplateJson));
        var reader = new UnionResponseReader<ResourceCreated<TemplateApiResource>, CreatedResult<TemplateApiResource>>(
            new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>(),
            new ContentResultResponseReader<CreatedResult<TemplateApiResource>, TemplateApiResource>());

        var result = await SendArchiveAsync(client, reader);

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task The_union_reader_tells_two_events_apart_by_the_resource_object_the_body_names()
    {
        var client = CreateClient(static _ => Json(
            HttpStatusCode.OK,
            """{"eventId":"templateVersion.updated","changeType":"Updated","resourceObject":"templateVersion","data":{"version":3,"publishedAt":"1970-01-01T00:00:00+00:00","templateId":"t-1"}}"""));
        var reader = new UnionResponseReader<ResourceCreated<TemplateApiResource>, ResourceUpdated<TemplateVersionApiResource>>(
            new ResourceEventResponseReader<ResourceCreated<TemplateApiResource>, TemplateApiResource>(),
            new ResourceEventResponseReader<ResourceUpdated<TemplateVersionApiResource>, TemplateVersionApiResource>());

        var result = await SendArchiveAsync(client, reader);

        result.IsT2.Should().BeTrue();
        result.AsT2.Data!.Version.Should().Be(3);
    }

    [Fact]
    public async Task The_union_reader_throws_when_two_members_could_be_the_JSON_body()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, TemplateJson));
        var reader = new UnionResponseReader<TemplateApiResource, OkResult<TemplateApiResource>>(
            new JsonResponseReader<TemplateApiResource>(),
            new ContentResultResponseReader<OkResult<TemplateApiResource>, TemplateApiResource>());

        var act = () => SendArchiveAsync(client, reader);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could be more than one*");
    }

    [Fact]
    public async Task The_app_client_reads_a_non_JSON_response_to_a_union_of_one_type_twice_as_its_first_member()
    {
        // Both members claim a bodiless 202. The client used to make the union through the reflected implicit conversion
        // from the member's type, which a union of one type twice declares twice, so it threw AmbiguousMatchException; the
        // union reader converts to the first member it read, as it does for any union whose first member claims the response.
        var client = new SampleAppFixture().CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, StatusCodeResult.Accepted>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.IsT2.Should().BeFalse();
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task A_standalone_content_result_is_read_with_the_wrapper_metadata_it_was_given()
    {
        // The transport's own options are camelCase, so "html_source" can only be read by the snake_case metadata the reader
        // was given for the wrapper, which a standalone read deserialises (it has no union to take it out of).
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, """{"content":{"id":"t-1","name":"N","html_source":"<p>snake</p>"}}"""));
        var wrapperTypeInfo = (JsonTypeInfo<OkResult<TemplateApiResource>>)SnakeCaseOptions().GetTypeInfo(typeof(OkResult<TemplateApiResource>));
        var reader = new ContentResultResponseReader<OkResult<TemplateApiResource>, TemplateApiResource>(resultTypeInfo: wrapperTypeInfo);

        var result = await SendArchiveAsync(client, reader);

        result.Content.HtmlSource.Should().Be("<p>snake</p>");
    }

    [Fact]
    public void The_content_result_reader_refuses_a_result_that_is_not_written_as_its_content()
    {
        var act = static () => new ContentResultResponseReader<NotFoundResult<TemplateApiResource>, TemplateApiResource>();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_compatibility_CallAsync_of_a_transport_with_no_app_throws()
    {
        var client = CreateClient(static _ => Json(HttpStatusCode.OK, "[]"));

        var act = () => client.CallAsync<TemplateApiResource[]>(new SearchTemplatesQuery { Q = "x", Page = 1, PageSize = 5 });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*BlueprintApp*");
    }

    [Fact]
    public async Task The_compatibility_SendVoidAsync_of_a_transport_with_no_app_throws()
    {
        var client = CreateClient(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var act = () => client.SendVoidAsync(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*BlueprintApp*");
    }

    [Fact]
    public void Constructor_throws_when_httpClient_is_null()
    {
        var act = static () => new BlueprintHttpClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static UnionResponseReader<StatusCodeResult.Accepted, PlainTextResult> AcceptedOrText()
    {
        return new UnionResponseReader<StatusCodeResult.Accepted, PlainTextResult>(
            new StatusCodeResultResponseReader<StatusCodeResult.Accepted>(StatusCodeResult.Accepted.Instance),
            new PlainTextResponseReader());
    }

    private static Task<TResult> SendArchiveAsync<TResult>(BlueprintHttpClient client, ResponseReader<TResult> reader)
    {
        return client.SendAsync(
            HttpMethod.Delete,
            "/templates/t-1",
            new ArchiveTemplateCommand { Id = "t-1" },
            operationTypeInfo: null,
            reader,
            "ArchiveTemplateCommand",
            options: null);
    }

    [Fact]
    public void Transports_given_no_options_share_one_read_only_default()
    {
        var first = new BlueprintHttpClient(new System.Net.Http.HttpClient());
        var second = new BlueprintHttpClient(new System.Net.Http.HttpClient());

        first.JsonOptions.Should().BeSameAs(second.JsonOptions, "metadata resolved from options is cached by the options, so each transport's own copy would resolve it again");
        first.JsonOptions.IsReadOnly.Should().BeTrue("options every transport shares cannot be open to change");
        first.JsonOptions.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    [Fact]
    public void Metadata_is_resolved_once_from_options_no_call_has_used_yet()
    {
        // Options with no resolver of their own, as JsonSerializerDefaults.Web and Blueprint's defaults are, have one only once
        // a serializer call has used them; metadata asked of them before then, as a typed client asks when it is made, has none.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var client = new BlueprintHttpClient(new System.Net.Http.HttpClient(), options);

        var typeInfo = client.ResolveTypeInfo<ArchiveTemplateCommand>();

        typeInfo.Should().BeSameAs(client.ResolveTypeInfo<ArchiveTemplateCommand>(), "metadata of locked options is cached by them");
        options.IsReadOnly.Should().BeTrue("the options are locked, as their first use by a serializer call locks them");
    }

    [Fact]
    public async Task An_operation_of_a_type_derived_from_the_declared_one_is_written_as_its_own_type()
    {
        // The declared type's metadata writes only the declared type's members; the app-driven client writes the operation as
        // its run-time type, so the typed client writes a derived instance that way too.
        string? body = null;
        var client = CreateClient(
            req =>
            {
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        await client.SendAsync(
            HttpMethod.Delete,
            "/templates/t-1",
            new AnnotatedArchiveTemplateCommand { Id = "t-1", Note = "tidy" },
            client.ResolveTypeInfo<ArchiveTemplateCommand>(),
            new JsonResponseReader<KeyOnlyResourceEventResult>(),
            "ArchiveTemplateCommand",
            options: null);

        using var document = JsonDocument.Parse(body!);
        document.RootElement.GetProperty("note").GetString().Should().Be("tidy");
        document.RootElement.GetProperty("id").GetString().Should().Be("t-1");
    }

    private static BlueprintHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond, string baseAddress = "https://localhost")
    {
        return new BlueprintHttpClient(new System.Net.Http.HttpClient(new FakeHttpHandler(respond)) { BaseAddress = new Uri(baseAddress) });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, new MediaTypeHeaderValue("application/json")) };
    }

    private static JsonSerializerOptions SnakeCaseOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
    }

    /// <summary>A body whose one property's wire name differs between camelCase and snake_case.</summary>
    public sealed class SnakeCaseProbe
    {
        /// <summary>Gets or sets the probe's value.</summary>
        public string? DisplayName { get; set; }
    }

    /// <summary>An event type declared outside Blueprint, as a consumer declares one.</summary>
    public sealed class TemplateArchivedEvent : ResourceEvent<TemplateApiResource>
    {
        /// <summary>Initialises a new instance of the <see cref="TemplateArchivedEvent"/> class.</summary>
        /// <param name="data">The template.</param>
        public TemplateArchivedEvent(TemplateApiResource data)
            : base(ResourceEventChangeType.Deleted, "template.archived", data)
        {
        }
    }

    /// <summary>An event type no instance of which can be made, so no reader of it can read one.</summary>
    public abstract class AbstractTemplateEvent : ResourceEvent<TemplateApiResource>
    {
        /// <summary>Initialises a new instance of the <see cref="AbstractTemplateEvent"/> class.</summary>
        /// <param name="data">The template.</param>
        protected AbstractTemplateEvent(TemplateApiResource data)
            : base(ResourceEventChangeType.Created, "template.created", data)
        {
        }
    }
}
