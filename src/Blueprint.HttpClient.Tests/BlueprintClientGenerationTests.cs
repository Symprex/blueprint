using System.Net;
using System.Text;
using Blueprint.Http;
using Blueprint.HttpClient.Tests.Clients;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

#pragma warning disable CS0618 // GetTemplateVersionsQuery is obsolete, and a client may still include it.

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// The contract of a <c>[BlueprintClient]</c> typed client (#60): for every operation it includes, it sends the same method,
/// path and body as <see cref="BlueprintHttpClient{TApp}"/> does for <see cref="SampleBlueprintApp"/>, and it returns the
/// declared result type, with no client app, no runtime scan and no per-call reflection.
/// </summary>
public sealed class BlueprintClientGenerationTests : IClassFixture<SampleAppFixture>
{
    private const string NotFoundProblem = """{"title":"Not Found","status":404}""";

    private static readonly Dictionary<string, ParityCase> _parityCases = new()
    {
        ["SearchTemplates"] = new(
            static api => api.SearchTemplatesAsync(new SearchTemplatesQuery { Q = "a&b", Page = 2, Tags = ["x", "y"] }),
            static app => app.SendAsync<TemplateApiResource[]>(new SearchTemplatesQuery { Q = "a&b", Page = 2, Tags = ["x", "y"] })),
        ["GetTemplate"] = new(
            static api => api.GetTemplateAsync(new GetTemplateQuery { Id = "t 1" }),
            static app => app.SendAsync<Blueprint.Model.Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new GetTemplateQuery { Id = "t 1" })),
        ["GetTemplateByStrongId"] = new(
            static api => api.GetTemplateByStrongIdAsync(new GetTemplateByStrongIdQuery { Id = new TemplateId(Guid.Parse("8f3c1b4e-0000-0000-0000-000000000001")) }),
            static app => app.SendAsync<Blueprint.Model.Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new GetTemplateByStrongIdQuery { Id = new TemplateId(Guid.Parse("8f3c1b4e-0000-0000-0000-000000000001")) })),
        ["GetTemplates"] = new(
            static api => api.GetTemplatesAsync(new GetTemplatesQuery()),
            static app => app.SendAsync<TemplateApiResource[]>(new GetTemplatesQuery())),
        ["GetPagedTemplates"] = new(
            static api => api.GetPagedTemplatesAsync(new GetPagedTemplatesQuery { NameFilter = "n" }),
            static app => app.SendAsync<PagedApiResource<TemplateApiResource>>(new GetPagedTemplatesQuery { NameFilter = "n" })),
        ["GetTemplateHistory"] = new(
            static api => api.GetTemplateHistoryAsync(new GetTemplateHistoryQuery { Id = "t-2", AuthorFilter = "ann" }),
            static app => app.SendAsync<TemplateVersionApiResource[]>(new GetTemplateHistoryQuery { Id = "t-2", AuthorFilter = "ann" })),
        ["GetTemplateNotificationRules"] = new(
            static api => api.GetTemplateNotificationRulesAsync(new GetTemplateNotificationRulesQuery { Id = "t-3" }),
            static app => app.SendAsync<TemplateNotificationRuleApiResource[]>(new GetTemplateNotificationRulesQuery { Id = "t-3" })),
        ["GetTemplateVersions"] = new(
            static api => api.GetTemplateVersionsAsync(new GetTemplateVersionsQuery { Id = "t-4" }),
            static app => app.SendAsync<TemplateVersionApiResource[]>(new GetTemplateVersionsQuery { Id = "t-4" })),
        ["GetMyAuthManifest"] = new(
            static api => api.GetMyAuthManifestAsync(new GetMyAuthManifestQuery()),
            static app => app.SendAsync<AuthManifestApiResource>(new GetMyAuthManifestQuery())),
        ["HealthCheck"] = new(
            static api => api.HealthCheckAsync(new HealthCheckQuery()),
            static app => app.SendAsync<PlainTextResult>(new HealthCheckQuery())),
        ["ArchiveTemplate"] = new(
            static api => api.ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-5" }),
            static app => app.SendAsync<KeyOnlyResourceEventResult>(new ArchiveTemplateCommand { Id = "t-5" })),
        ["BulkArchiveTemplates"] = new(
            static api => api.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a", "b"], Reason = "r" }),
            static app => app.SendAsync<StatusCodeResult.Accepted>(new BulkArchiveTemplatesCommand { Ids = ["a", "b"], Reason = "r" })),
        ["ChangePassword"] = new(
            static api => api.ChangePasswordAsync(new ChangePasswordCommand { CurrentPassword = "c", NewPassword = "n", ConfirmNewPassword = "n" }),
            static app => app.SendAsync<NoResultOperationResult>(new ChangePasswordCommand { CurrentPassword = "c", NewPassword = "n", ConfirmNewPassword = "n" })),
        ["CloneTemplate"] = new(
            static api => api.CloneTemplateAsync(new CloneTemplateCommand { Id = "t-6", NewName = "copy" }),
            static app => app.SendAsync<ResourceEventResult<TemplateApiResource>>(new CloneTemplateCommand { Id = "t-6", NewName = "copy" })),
        ["CreateTemplate"] = new(
            static api => api.CreateTemplateAsync(new CreateTemplateCommand { Name = "n", Priority = 7, Tags = ["t"] }),
            static app => app.SendAsync<ResourceEventResult<TemplateApiResource>>(new CreateTemplateCommand { Name = "n", Priority = 7, Tags = ["t"] })),
        ["PatchTemplateSettings"] = new(
            static api => api.PatchTemplateSettingsAsync(new PatchTemplateSettingsCommand { Id = "t-7", Opacity = 0.5 }),
            static app => app.SendAsync<ResourceEventResult<TemplateApiResource>>(new PatchTemplateSettingsCommand { Id = "t-7", Opacity = 0.5 })),
        ["PreviewTemplate"] = new(
            static api => api.PreviewTemplateAsync(new PreviewTemplateCommand { Id = "t-8" }),
            static app => app.SendAsync<TemplatePreviewApiResource>(new PreviewTemplateCommand { Id = "t-8" })),
        ["PublishTemplate"] = new(
            static api => api.PublishTemplateAsync(new PublishTemplateCommand { Id = "t-9" }),
            static app => app.SendAsync<Blueprint.Model.Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>>(new PublishTemplateCommand { Id = "t-9" })),
        ["SetTemplateNotificationPriority"] = new(
            static api => api.SetTemplateNotificationPriorityAsync(new SetTemplateNotificationPriorityCommand { Id = "t-10", Level = 3, Reason = "why" }),
            static app => app.SendAsync<KeyOnlyResourceEventResult>(new SetTemplateNotificationPriorityCommand { Id = "t-10", Level = 3, Reason = "why" })),
        ["UpdateTemplate"] = new(
            static api => api.UpdateTemplateAsync(new UpdateTemplateCommand { Id = "t-11", Name = "u" }),
            static app => app.SendAsync<ResourceEventResult<TemplateApiResource>>(new UpdateTemplateCommand { Id = "t-11", Name = "u" })),
    };

    /// <summary>
    /// One success response per result shape the sample operations declare, read by the typed client and by the app
    /// client. A union is compared by the member it holds.
    /// </summary>
    private static readonly Dictionary<string, ReadCase> _readCases = new()
    {
        ["Json array"] = new(
            static () => Json(HttpStatusCode.OK, """[{"id":"t-1","name":"One"}]"""),
            static async api => await api.GetTemplatesAsync(new GetTemplatesQuery()),
            static async app => await app.SendAsync<TemplateApiResource[]>(new GetTemplatesQuery())),
        ["Json object"] = new(
            static () => Json(HttpStatusCode.OK, """{"templateId":"t-1","renderedHtml":"<p>x</p>","renderedPlainText":"x"}"""),
            static async api => await api.PreviewTemplateAsync(new PreviewTemplateCommand { Id = "t-1" }),
            static async app => await app.SendAsync<TemplatePreviewApiResource>(new PreviewTemplateCommand { Id = "t-1" })),
        ["PlainTextResult"] = new(
            static () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok", Encoding.UTF8, "text/plain") },
            static async api => await api.HealthCheckAsync(new HealthCheckQuery()),
            static async app => await app.SendAsync<PlainTextResult>(new HealthCheckQuery())),
        ["StatusCodeResult.Accepted"] = new(
            static () => new HttpResponseMessage(HttpStatusCode.Accepted),
            static async api => await api.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a"] }),
            static async app => await app.SendAsync<StatusCodeResult.Accepted>(new BulkArchiveTemplatesCommand { Ids = ["a"] })),
        ["KeyOnlyResourceEventResult deleted"] = new(
            static () => new HttpResponseMessage(HttpStatusCode.NoContent),
            static async api => await api.ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-1" }),
            static async app => await app.SendAsync<KeyOnlyResourceEventResult>(new ArchiveTemplateCommand { Id = "t-1" })),
        ["KeyOnlyResourceEventResult updated"] = new(
            static () => new HttpResponseMessage(HttpStatusCode.OK),
            static async api => await api.SetTemplateNotificationPriorityAsync(new SetTemplateNotificationPriorityCommand { Id = "t-1", Level = 1 }),
            static async app => await app.SendAsync<KeyOnlyResourceEventResult>(new SetTemplateNotificationPriorityCommand { Id = "t-1", Level = 1 })),
        ["NoResultOperationResult"] = new(
            static () => new HttpResponseMessage(HttpStatusCode.OK),
            static async api => await api.ChangePasswordAsync(new ChangePasswordCommand()),
            static async app => await app.SendAsync<NoResultOperationResult>(new ChangePasswordCommand())),
        ["ResourceEventResult"] = new(
            static () => Json(HttpStatusCode.Created, """{"changeType":"Created","eventId":"template.cloned","data":{"id":"t-2","name":"Copy"},"metadata":{"source":"t-1","n":2},"correlationId":"c-1"}"""),
            static async api => await api.CloneTemplateAsync(new CloneTemplateCommand { Id = "t-1" }),
            static async app => await app.SendAsync<ResourceEventResult<TemplateApiResource>>(new CloneTemplateCommand { Id = "t-1" })),
        ["Union of ResourceEventResult and ConflictResult"] = new(
            static () => Json(HttpStatusCode.OK, """{"changeType":"Updated","eventId":"template.published","data":{"id":"t-1","name":"One"}}"""),
            static async api => Member(await api.PublishTemplateAsync(new PublishTemplateCommand { Id = "t-1" })),
            static async app => Member(await app.SendAsync<Blueprint.Model.Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>>(new PublishTemplateCommand { Id = "t-1" }))),
        ["Union of OkResult and NotFoundResult"] = new(
            static () => Json(HttpStatusCode.OK, """{"id":"t-1","name":"One"}"""),
            static async api => Member(await api.GetTemplateAsync(new GetTemplateQuery { Id = "t-1" })),
            static async app => Member(await app.SendAsync<Blueprint.Model.Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new GetTemplateQuery { Id = "t-1" }))),
    };

    private readonly SampleAppFixture _fixture;

    public BlueprintClientGenerationTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    public static TheoryData<string> ParityCaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var name in _parityCases.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(ParityCaseNames))]
    public async Task Typed_client_sends_the_same_request_as_the_app_client(string caseName)
    {
        var parityCase = _parityCases[caseName];

        var typed = await CaptureAsync(handler => parityCase.Typed(CreateApi(handler)));
        var app = await CaptureAsync(handler => parityCase.App(this._fixture.CreateClient(handler)));

        typed.Should().BeEquivalentTo(app);
    }

    public static TheoryData<string> ReadCaseNames()
    {
        var names = new TheoryData<string>();

        foreach (var name in _readCases.Keys)
        {
            names.Add(name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(ReadCaseNames))]
    public async Task Typed_client_reads_a_success_response_as_the_app_client_does(string caseName)
    {
        var (respond, typedCall, appCall) = _readCases[caseName];

        var typed = await typedCall(CreateApi(new FakeHttpHandler(_ => respond())));
        var app = await appCall(this._fixture.CreateClient(new FakeHttpHandler(_ => respond())));

        typed.Should().NotBeNull();
        typed!.GetType().Should().Be(app!.GetType());
        typed.Should().BeEquivalentTo(app, static options => options.PreferringRuntimeMemberTypes());
    }

    [Fact]
    public async Task Typed_client_puts_a_route_value_in_the_path()
    {
        var sent = await CaptureAsync(handler => CreateApi(handler).GetTemplateAsync(new GetTemplateQuery { Id = "t 1/x" }));

        sent.Method.Should().Be("GET");
        sent.PathAndQuery.Should().Be("/templates/t%201%2Fx");
        sent.Body.Should().BeNull();
    }

    [Fact]
    public async Task Typed_client_puts_a_get_queries_properties_in_the_query_string()
    {
        var sent = await CaptureAsync(handler => CreateApi(handler).SearchTemplatesAsync(new SearchTemplatesQuery { Q = "hello", Page = 3, PageSize = 5, MinResults = 0, MaxResults = 9 }));

        sent.Method.Should().Be("GET");
        sent.PathAndQuery.Should().Be("/templates/search?q=hello&page=3&pageSize=5&sortBy=Name&minResults=0&maxResults=9");
    }

    [Fact]
    public async Task Typed_client_sends_a_delete_commands_properties_as_its_body()
    {
        var sent = await CaptureAsync(handler => CreateApi(handler).ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-9" }));

        sent.Method.Should().Be("DELETE");
        sent.PathAndQuery.Should().Be("/templates/t-9");
        sent.Body.Should().Be("""{"id":"t-9"}""");
    }

    [Fact]
    public async Task A_declared_resource_created_comes_back_as_a_resource_created()
    {
        var api = CreateApi(new FakeHttpHandler(static _ => Json(
            HttpStatusCode.OK,
            """{"$object":"event","eventId":"template.created","changeType":"Created","resourceObject":"template","data":{"id":"t-1","name":"New"}}""")));

        var result = await api.RegisterTemplateAsync(new RegisterTemplateCommand { Name = "New" });

        result.Should().BeOfType<ResourceCreated<TemplateApiResource>>();
        result.EventId.Should().Be("template.created");
        result.ChangeType.Should().Be(ResourceEventChangeType.Created);
        result.Data!.Id.Should().Be("t-1");
        result.Data.Name.Should().Be("New");
    }

    [Fact]
    public async Task A_declared_accepted_status_comes_back_as_its_instance()
    {
        var api = CreateApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await api.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a"] });

        result.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task A_declared_plain_text_result_comes_back_with_its_text()
    {
        var api = CreateApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("all good", Encoding.UTF8, "text/plain"),
        }));

        var result = await api.HealthCheckAsync(new HealthCheckQuery());

        result.Content.Should().Be("all good");
        result.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task A_declared_union_comes_back_holding_the_member_the_host_wrote()
    {
        var api = CreateApi(new FakeHttpHandler(static _ => Json(HttpStatusCode.OK, """{"id":"t-1","name":"One"}""")));

        var result = await api.GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("One");
    }

    [Fact]
    public async Task Options_then_request_modifiers_in_the_order_added_then_the_credential_mutate_one_request()
    {
        HttpRequestMessage? captured = null;
        var api = CreateApi(new FakeHttpHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        await api
            .WithStep("modifier-1")
            .WithCredential(new OrderRecordingCredential())
            .WithStep("modifier-2")
            .BulkArchiveTemplatesAsync(
                new BulkArchiveTemplatesCommand { Ids = ["a"] },
                new CallOptions { ModifyRequest = static request => request.Headers.Add("X-Order", "options") });

        captured!.Headers.GetValues("X-Order").Should().Equal("options", "modifier-1", "modifier-2", "credential");
    }

    [Fact]
    public async Task A_bearer_token_credential_sets_the_authorization_header_after_the_callers_options()
    {
        HttpRequestMessage? captured = null;
        var api = CreateApi(new FakeHttpHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        await api.WithCredential(new BearerTokenCredential("jwt-1")).BulkArchiveTemplatesAsync(
            new BulkArchiveTemplatesCommand { Ids = ["a"] },
            new CallOptions { ModifyRequest = static request => request.Headers.Authorization = new("Basic", "caller") });

        captured!.Headers.Authorization!.ToString().Should().Be("Bearer jwt-1");
    }

    [Fact]
    public async Task A_second_credential_replaces_the_first()
    {
        HttpRequestMessage? captured = null;
        var api = CreateApi(new FakeHttpHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        await api
            .WithCredential(new BearerTokenCredential("first"))
            .WithCredential(new BearerTokenCredential("second"))
            .BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a"] });

        captured!.Headers.Authorization!.ToString().Should().Be("Bearer second");
    }

    [Fact]
    public async Task A_clone_leaves_the_original_unchanged()
    {
        var requests = new List<HttpRequestMessage>();
        var api = CreateApi(new FakeHttpHandler(request =>
        {
            requests.Add(request);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));

        _ = api.WithCredential(new BearerTokenCredential("jwt"));
        _ = api.WithStep("cloned");

        await api.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a"] });

        requests.Should().ContainSingle();
        requests[0].Headers.Authorization.Should().BeNull();
        requests[0].Headers.Contains("X-Order").Should().BeFalse();
    }

    [Fact]
    public async Task A_readonly_field_is_a_constructor_parameter_that_clones_carry()
    {
        var api = CreateApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var clone = (TemplatesApi)api.WithCredential(new BearerTokenCredential("jwt"));

        clone.Region.Should().Be("eu");
        await clone.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["a"] });
    }

    [Fact]
    public async Task OnConfigure_can_replace_the_call_options_of_its_operation()
    {
        HttpRequestMessage? captured = null;
        var api = CreateApi(new FakeHttpHandler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, """{"id":"t-1","name":"One"}""");
        }));

        await api.GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        captured!.Headers.GetValues("X-Configured").Should().Equal("t-1");
    }

    [Fact]
    public async Task A_serializer_context_is_consulted_first_once_per_options_and_the_options_converters_still_apply()
    {
        var jsonOptions = BlueprintJsonOptions.CreateDefaultOptions();

        // First, ahead of the default options' string enum converter, which would otherwise claim the enum.
        jsonOptions.Converters.Insert(0, new UpperCaseSortOrderConverter());

        var bodies = new List<string>();
        var handler = new FakeHttpHandler(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(NotFoundProblem, Encoding.UTF8, "application/problem+json"),
            };
        });
        var api = new RecordingContextApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, jsonOptions));
        var command = new CreateTemplateCommand { Name = "n", DefaultSortOrder = TemplateSortOrder.LastModified };

        RecordingJsonContext.Default.Requested.TryRemove(typeof(CreateTemplateCommand), out _);

        for (var call = 0; call < 2; call++)
        {
            var act = () => api.CreateTemplateAsync(command);
            await act.Should().ThrowAsync<BlueprintApiException>();
        }

        RecordingJsonContext.Default.Requested[typeof(CreateTemplateCommand)].Should().Be(1);
        bodies.Should().HaveCount(2).And.AllSatisfy(static body => body.Should().Contain("\"defaultSortOrder\":\"LASTMODIFIED\""));
    }

    private static TemplatesApi CreateApi(FakeHttpHandler handler)
    {
        var httpClient = new System.Net.Http.HttpClient(handler) { BaseAddress = new Uri("https://localhost") };

        return new TemplatesApi(new BlueprintHttpClient(httpClient), "eu");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// Sends one call, answering it with a not-found problem, which every result type raises, and captures the request.
    /// </summary>
    private static async Task<SentRequest> CaptureAsync(Func<FakeHttpHandler, Task> call)
    {
        SentRequest? sent = null;
        var handler = new FakeHttpHandler(request =>
        {
            sent = new SentRequest(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                request.Content?.ReadAsStringAsync().GetAwaiter().GetResult());

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(NotFoundProblem, Encoding.UTF8, "application/problem+json"),
            };
        });

        Func<Task> act = () => call(handler);
        await act.Should().ThrowAsync<BlueprintApiException>();

        return sent!;
    }

    private sealed record ParityCase(Func<ITemplatesApi, Task> Typed, Func<BlueprintHttpClient<SampleBlueprintApp>, Task> App);

    private sealed record SentRequest(string Method, string PathAndQuery, string? Body);

    private sealed record ReadCase(
        Func<HttpResponseMessage> Respond,
        Func<ITemplatesApi, Task<object?>> Typed,
        Func<BlueprintHttpClient<SampleBlueprintApp>, Task<object?>> App);

    private static object? Member<T1, T2>(Blueprint.Model.Results<T1, T2> union)
        where T1 : notnull
        where T2 : notnull
    {
        return union.IsT1 ? union.AsT1 : union.AsT2;
    }

    private sealed class UpperCaseSortOrderConverter : System.Text.Json.Serialization.JsonConverter<TemplateSortOrder>
    {
        public override TemplateSortOrder Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            return Enum.Parse<TemplateSortOrder>(reader.GetString()!, ignoreCase: true);
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, TemplateSortOrder value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString().ToUpperInvariant());
        }
    }

    private sealed class OrderRecordingCredential : IUpstreamCredential
    {
        public ValueTask ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Order", "credential");
            return ValueTask.CompletedTask;
        }
    }
}
