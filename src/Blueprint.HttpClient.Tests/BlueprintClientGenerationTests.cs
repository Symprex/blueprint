using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

#pragma warning disable CS0618 // GetTemplateVersionsQuery is obsolete; a client of the contract still calls it.

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves the typed clients the source generator completes from a <see cref="BlueprintClientAttribute"/> class: each call
/// sends the request <see cref="BlueprintHttpClient{TApp}"/> sends for the same operation, and returns the result type its
/// operation declares, read by the reader chosen for that type when the client was compiled.
/// </summary>
/// <remarks>
/// <see cref="SampleContractApi"/> calls operations of a referenced assembly; <see cref="LocalOperationsApi"/> calls
/// operations declared in this one, whose request builders the same build generates.
/// </remarks>
public sealed class BlueprintClientGenerationTests : IClassFixture<SampleAppFixture>
{
    private static readonly Uri _baseAddress = new("https://localhost");

    private readonly SampleAppFixture _fixture;

    public BlueprintClientGenerationTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// Gets one case per operation <see cref="SampleContractApi"/> includes, each with an operation whose every property the
    /// request carries is set, and the result type the operation declares, written out from its declaration.
    /// </summary>
    public static TheoryData<ClientCase> SampleCases => new()
    {
        ClientCase.Of<ArchiveTemplateCommand, KeyOnlyResourceEventResult>(
            "ArchiveTemplateAsync",
            () => new ArchiveTemplateCommand { Id = "t-1" },
            static (api, op) => api.ArchiveTemplateAsync(op)),
        ClientCase.Of<BulkArchiveTemplatesCommand, StatusCodeResult.Accepted>(
            "BulkArchiveTemplatesAsync",
            () => new BulkArchiveTemplatesCommand { Ids = ["t-1", "t-2"], Reason = "tidy up" },
            static (api, op) => api.BulkArchiveTemplatesAsync(op)),
        ClientCase.Of<ChangePasswordCommand, NoResultOperationResult>(
            "ChangePasswordAsync",
            () => new ChangePasswordCommand { CurrentPassword = "old-secret", NewPassword = "new-secret-1", ConfirmNewPassword = "new-secret-1" },
            static (api, op) => api.ChangePasswordAsync(op)),
        ClientCase.Of<CloneTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            "CloneTemplateAsync",
            () => new CloneTemplateCommand { Id = "t-1", NewName = "Copy of welcome" },
            static (api, op) => api.CloneTemplateAsync(op)),
        ClientCase.Of<CreateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            "CreateTemplateAsync",
            () => new CreateTemplateCommand
            {
                Name = "Welcome",
                Slug = "welcome",
                Visibility = "public",
                Priority = 42,
                HtmlSource = "<p>Hi</p>",
                Tags = ["onboarding", "email"],
                MergeFields = [new TemplateMergeFieldDto { Key = "firstName", DefaultValue = "there" }],
                DefaultSortOrder = TemplateSortOrder.LastModified,
            },
            static (api, op) => api.CreateTemplateAsync(op)),
        ClientCase.Of<ExportTemplatesCommand, AcceptedResult<ExportJobApiResource>>(
            "ExportTemplatesAsync",
            () => new ExportTemplatesCommand { NameFilter = "wel", NotifyOnCompletion = true, NotifyEmail = "ops@example.com", RestrictFormats = true, Formats = ["html"] },
            static (api, op) => api.ExportTemplatesAsync(op)),
        ClientCase.Of<PatchTemplateSettingsCommand, ResourceEventResult<TemplateApiResource>>(
            "PatchTemplateSettingsAsync",
            () => new PatchTemplateSettingsCommand { Id = "t-1", Opacity = 0.5, MinWeight = 10, MaxWeight = 90, CssClassName = "hero" },
            static (api, op) => api.PatchTemplateSettingsAsync(op)),
        ClientCase.Of<PreviewTemplateCommand, TemplatePreviewApiResource>(
            "PreviewTemplateAsync",
            () => new PreviewTemplateCommand { Id = "t-1", MergeFields = new Dictionary<string, string> { ["firstName"] = "Ada" } },
            static (api, op) => api.PreviewTemplateAsync(op)),
        ClientCase.Of<PublishTemplateCommand, Results<ResourceEventResult<TemplateApiResource>, ConflictResult<TemplateApiResource>>>(
            "PublishTemplateAsync",
            () => new PublishTemplateCommand { Id = "t-1" },
            static (api, op) => api.PublishTemplateAsync(op)),
        ClientCase.Of<SetTemplateNotificationPriorityCommand, KeyOnlyResourceEventResult>(
            "SetTemplateNotificationPriorityAsync",
            () => new SetTemplateNotificationPriorityCommand
            {
                Id = "t-1",
                Level = 3,
                Reason = "launch",
                Rule = new TemplateNotificationRuleApiResource { Enabled = true, RecipientEmail = "ops@example.com", Subject = "Changed" },
            },
            static (api, op) => api.SetTemplateNotificationPriorityAsync(op)),
        ClientCase.Of<UpdateTemplateCommand, ResourceEventResult<TemplateApiResource>>(
            "UpdateTemplateAsync",
            () => new UpdateTemplateCommand { Id = "t-1", Name = "Welcome back", HtmlSource = "<p>Hello</p>", PlainTextSource = "Hello" },
            static (api, op) => api.UpdateTemplateAsync(op)),
        ClientCase.Of<UpdateTemplateSettingsCommand, OkResult>(
            "UpdateTemplateSettingsAsync",
            () => new UpdateTemplateSettingsCommand { TenantId = "tenant 7", Id = "t-1", DisplayName = "Welcome" },
            static (api, op) => api.UpdateTemplateSettingsAsync(op)),
        ClientCase.Of<DownloadTemplateQuery, FileStreamResult>(
            "DownloadTemplateAsync",
            () => new DownloadTemplateQuery { Id = "t-1" },
            static (api, op) => api.DownloadTemplateAsync(op)),
        ClientCase.Of<GetFolderContentsQuery, OkResult>(
            "GetFolderContentsAsync",
            () => new GetFolderContentsQuery { FolderPath = "shared/2026 drafts" },
            static (api, op) => api.GetFolderContentsAsync(op)),
        ClientCase.Of<GetMyAuthManifestQuery, AuthManifestApiResource>(
            "GetMyAuthManifestAsync",
            () => new GetMyAuthManifestQuery(),
            static (api, op) => api.GetMyAuthManifestAsync(op)),
        ClientCase.Of<GetPagedTemplatesQuery, PagedApiResource<TemplateApiResource>>(
            "GetPagedTemplatesAsync",
            () => new GetPagedTemplatesQuery { NameFilter = "wel", Page = 3, PageSize = 25, OrderBy = "name", OrderByDescending = true },
            static (api, op) => api.GetPagedTemplatesAsync(op)),
        ClientCase.Of<GetTemplateByStrongIdQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            "GetTemplateByStrongIdAsync",
            () => new GetTemplateByStrongIdQuery
            {
                Id = new TemplateId(Guid.Parse("6f1c9a52-0d4e-4b8e-9a51-2f7c3d9e8b10")),
                ParentId = new TemplateId(Guid.Parse("0a2b4c6d-8e0f-4a1b-9c3d-5e7f9a1b3c5d")),
            },
            static (api, op) => api.GetTemplateByStrongIdAsync(op)),
        ClientCase.Of<GetTemplateHistoryQuery, TemplateVersionApiResource[]>(
            "GetTemplateHistoryAsync",
            () => new GetTemplateHistoryQuery { Id = "t-1", StartDate = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), EndDate = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc), AuthorFilter = "Ada L" },
            static (api, op) => api.GetTemplateHistoryAsync(op)),
        ClientCase.Of<GetTemplateNotificationRulesQuery, TemplateNotificationRuleApiResource[]>(
            "GetTemplateNotificationRulesAsync",
            () => new GetTemplateNotificationRulesQuery { Id = "t-1" },
            static (api, op) => api.GetTemplateNotificationRulesAsync(op)),
        ClientCase.Of<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
            "GetTemplateAsync",
            () => new GetTemplateQuery { Id = "t/42" },
            static (api, op) => api.GetTemplateAsync(op)),
        ClientCase.Of<GetTemplateVersionsQuery, TemplateVersionApiResource[]>(
            "GetTemplateVersionsAsync",
            () => new GetTemplateVersionsQuery { Id = "t-1" },
            static (api, op) => api.GetTemplateVersionsAsync(op)),
        ClientCase.Of<GetTemplatesQuery, TemplateApiResource[]>(
            "GetTemplatesAsync",
            () => new GetTemplatesQuery(),
            static (api, op) => api.GetTemplatesAsync(op)),
        ClientCase.Of<HealthCheckQuery, PlainTextResult>(
            "HealthCheckAsync",
            () => new HealthCheckQuery(),
            static (api, op) => api.HealthCheckAsync(op)),
        ClientCase.Of<RedirectToTemplateDocsQuery, RedirectResult>(
            "RedirectToTemplateDocsAsync",
            () => new RedirectToTemplateDocsQuery { Id = "t-1" },
            static (api, op) => api.RedirectToTemplateDocsAsync(op)),
        ClientCase.Of<SearchTemplatesQuery, TemplateApiResource[]>(
            "SearchTemplatesAsync",
            () => new SearchTemplatesQuery
            {
                Q = "hello world",
                Page = 2,
                PageSize = 10,
                SortBy = TemplateSortOrder.CreatedDate,
                MinResults = 5,
                MaxResults = 50,
                CorrelationId = "c-1",
                Tags = ["a", "b c"],
                PreferredLocale = "en-GB",
            },
            static (api, op) => api.SearchTemplatesAsync(op)),
    };

    [Theory]
    [MemberData(nameof(SampleCases))]
    public async Task Each_call_sends_the_request_the_app_client_sends_for_the_same_operation(ClientCase clientCase)
    {
        var operation = clientCase.CreateOperation();

        var typed = await CaptureAsync(handler => clientCase.SendAsync(CreateSampleApi(handler), operation));
        var app = await CaptureAsync(handler => this._fixture.CreateClient(handler).SendAsync(operation));

        typed.Should().Be(app);
    }

    [Fact]
    public void The_interface_has_a_method_for_each_included_operation_named_for_it()
    {
        OperationMethods(typeof(ISampleContractApi)).Select(static m => m.Name)
            .Should().BeEquivalentTo(SampleCases.Select(static c => ((ClientCase)c[0]).MethodName));

        OperationMethods(typeof(ILocalOperationsApi)).Select(static m => m.Name)
            .Should().BeEquivalentTo("CopyTemplateAsync", "FindTemplateCopiesAsync");
    }

    [Fact]
    public void An_operation_of_this_assembly_is_called_through_a_method_returning_its_declared_result()
    {
        typeof(ILocalOperationsApi).GetMethod("CopyTemplateAsync")!.ReturnType.Should().Be<Task<ResourceCreated<TemplateApiResource>>>();
        typeof(ILocalOperationsApi).GetMethod("FindTemplateCopiesAsync")!.ReturnType.Should().Be<Task<TemplateApiResource[]>>();
    }

    [Theory]
    [MemberData(nameof(SampleCases))]
    public void Each_method_takes_the_operation_and_optional_call_options_and_returns_the_declared_result(ClientCase clientCase)
    {
        var method = typeof(ISampleContractApi).GetMethod(clientCase.MethodName)!;
        var nullability = new NullabilityInfoContext();

        method.ReturnType.Should().Be(typeof(Task<>).MakeGenericType(clientCase.DeclaredResult));
        nullability.Create(method.ReturnParameter).GenericTypeArguments[0].ReadState
            .Should().Be(NullabilityState.NotNull, "the declared result is returned as declared, not as nullable");

        var parameters = method.GetParameters();
        parameters.Select(static p => p.Name).Should().Equal("operation", "options", "cancellationToken");
        parameters.Select(static p => p.ParameterType).Should().Equal(clientCase.OperationType, typeof(CallOptions), typeof(CancellationToken));
        parameters[0].IsOptional.Should().BeFalse();
        parameters[1].IsOptional.Should().BeTrue();
        parameters[1].DefaultValue.Should().BeNull();
        nullability.Create(parameters[1]).ReadState.Should().Be(NullabilityState.Nullable);
        parameters[2].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void The_client_implements_its_interface_which_exposes_no_transport()
    {
        typeof(SampleContractApi).Should().Implement<ISampleContractApi>();
        typeof(ISampleContractApi).GetProperties().Should().BeEmpty("the interface carries no BlueprintHttpClient property");
        typeof(ISampleContractApi).IsPublic.Should().BeTrue();
        typeof(ILocalOperationsApi).IsPublic.Should().BeFalse("an internal client's interface is internal too");
    }

    [Fact]
    public async Task A_search_is_sent_as_a_GET_with_its_query_string()
    {
        var sent = await CaptureAsync(handler => CreateSampleApi(handler).SearchTemplatesAsync(new SearchTemplatesQuery
        {
            Q = "hello world",
            Page = 2,
            PageSize = 10,
            SortBy = TemplateSortOrder.CreatedDate,
            MinResults = 5,
            MaxResults = 50,
            Tags = ["a", "b"],
        }));

        sent.Should().Be(new SentRequest("GET", "/templates/search?q=hello%20world&page=2&pageSize=10&sortBy=CreatedDate&minResults=5&maxResults=50&tags=a&tags=b", null));
    }

    [Fact]
    public async Task A_create_is_sent_as_a_POST_with_the_operation_as_its_body()
    {
        var sent = await CaptureAsync(handler => CreateSampleApi(handler).CreateTemplateAsync(new CreateTemplateCommand { Name = "Welcome", Priority = 42 }));

        sent.Method.Should().Be("POST");
        sent.PathAndQuery.Should().Be("/templates");

        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("name").GetString().Should().Be("Welcome");
        body.RootElement.GetProperty("priority").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task An_archive_is_sent_as_a_DELETE_to_the_url_with_its_id()
    {
        var sent = await CaptureAsync(handler => CreateSampleApi(handler).ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-99" }));

        sent.Method.Should().Be("DELETE");
        sent.PathAndQuery.Should().Be("/templates/t-99");
    }

    [Fact]
    public async Task An_operation_of_this_assembly_is_sent_with_the_builders_this_build_generated_for_it()
    {
        var post = await CaptureAsync(handler => CreateLocalApi(handler).CopyTemplateAsync(new CopyTemplateCommand { Id = "t-1", Name = "Copy" }));
        var get = await CaptureAsync(handler => CreateLocalApi(handler).FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1", NameStartsWith = "Wel" }));

        post.Should().Be(new SentRequest("POST", "/templates/t-1/copies", """{"id":"t-1","name":"Copy"}"""));
        get.Should().Be(new SentRequest("GET", "/templates/t-1/copies/search?nameStartsWith=Wel", null));
    }

    [Fact]
    public async Task A_declared_resource_created_event_is_returned_as_one()
    {
        var written = new ResourceCreated<TemplateApiResource>(new TemplateApiResource("t-2", "Copy", "<p>Hi</p>", "Hi"));
        var api = CreateLocalApi(await RespondAsTheHostWritesAsync(written));

        var created = await api.CopyTemplateAsync(new CopyTemplateCommand { Id = "t-1" });

        created.Should().BeOfType<ResourceCreated<TemplateApiResource>>();
        created.EventId.Should().Be("template.created");
        created.ChangeType.Should().Be(ResourceEventChangeType.Created);
        created.Data!.Id.Should().Be("t-2");
    }

    [Fact]
    public async Task A_declared_accepted_status_is_returned_as_its_singleton()
    {
        var api = CreateSampleApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var accepted = await api.BulkArchiveTemplatesAsync(new BulkArchiveTemplatesCommand { Ids = ["t-1"] });

        accepted.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task A_declared_plain_text_result_is_returned_with_its_text()
    {
        var api = CreateSampleApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("healthy", Encoding.UTF8, "text/plain"),
        }));

        var text = await api.HealthCheckAsync(new HealthCheckQuery());

        text.Content.Should().Be("healthy");
        text.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task A_declared_resource_event_result_is_read_from_the_envelope_the_host_writes()
    {
        var written = new ResourceEventResult<TemplateApiResource>(ChangeType.Created, "template.created", "t-3", new TemplateApiResource("t-3", "Welcome", "<p>Hi</p>", "Hi"));
        var api = CreateSampleApi(await RespondAsTheHostWritesAsync(written));

        var result = await api.CreateTemplateAsync(new CreateTemplateCommand { Name = "Welcome" });

        result.ChangeType.Should().Be(ChangeType.Created);
        result.EventId.Should().Be("template.created");
        result.Content.Id.Should().Be("t-3");
    }

    [Fact]
    public async Task A_declared_union_is_returned_as_the_member_the_host_wrote()
    {
        var api = CreateSampleApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"t-42","name":"Welcome","htmlSource":"<p>Hi</p>","plainTextSource":"Hi"}""", Encoding.UTF8, "application/json"),
        }));

        var result = await api.GetTemplateAsync(new GetTemplateQuery { Id = "t-42" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Id.Should().Be("t-42");
    }

    [Fact]
    public async Task A_declared_key_only_event_is_read_from_the_status_the_host_writes_it_as()
    {
        var api = CreateSampleApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await api.ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-1" });

        result.ChangeType.Should().Be(ChangeType.Deleted);
    }

    [Fact]
    public async Task A_declared_array_is_read_as_json()
    {
        var api = CreateLocalApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"t-5","name":"Welcome","htmlSource":"","plainTextSource":""}]""", Encoding.UTF8, "application/json"),
        }));

        var copies = await api.FindTemplateCopiesAsync(new FindTemplateCopiesQuery { Id = "t-1" });

        copies.Select(static c => c.Id).Should().Equal("t-5");
    }

    [Fact]
    public async Task A_route_placeholder_with_no_value_fails_the_call_before_anything_is_sent()
    {
        var sent = false;
        var api = CreateSampleApi(new FakeHttpHandler(_ =>
        {
            sent = true;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));

        var act = () => api.ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = null! });

        await act.Should().ThrowAsync<InvalidOperationException>();
        sent.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_operation_is_refused()
    {
        var api = CreateSampleApi(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var act = () => api.ArchiveTemplateAsync(null!);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("operation");
    }

    [Fact]
    public void A_client_refuses_a_missing_transport()
    {
        var act = () => new SampleContractApi(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("client");
    }

    [Theory]
    [MemberData(nameof(SampleCases))]
    public void Each_call_reads_with_the_reader_the_app_client_chooses_for_its_result(ClientCase clientCase)
    {
        var plan = PlanOf(new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient())));

        var reader = PlanMember(plan, clientCase.OperationType.Name + "Reader");

        DescribeReader(reader).Should().Be(DescribeReader(AppReaderFor(clientCase.DeclaredResult)));
    }

    [Fact]
    public void Clients_over_the_same_options_share_one_plan_which_their_clones_keep()
    {
        var options = BlueprintJsonOptions.CreateDefaultOptions();
        var first = new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(), options));
        var second = new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(), options));
        var other = new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(), BlueprintJsonOptions.CreateDefaultOptions()));

        PlanOf(first).Should().NotBeNull().And.BeSameAs(PlanOf(second), "a client resolved per call must not resolve its metadata again");
        PlanOf(other).Should().NotBeSameAs(PlanOf(first), "metadata is that of the options it was resolved from");
        PlanOf((SampleContractApi)first.WithCredential(new BearerTokenCredential("t"))).Should().BeSameAs(PlanOf(first));
    }

    [Fact]
    public void The_plan_holds_each_body_s_metadata_and_readers_given_the_metadata_of_what_they_read()
    {
        var transport = new BlueprintHttpClient(new System.Net.Http.HttpClient());
        var plan = PlanOf(new SampleContractApi(transport));

        // Resolved from the transport's options, so a call passes it rather than looking it up.
        PlanMember(plan, "CreateTemplateCommandRequest").Should().BeSameAs(transport.ResolveTypeInfo<CreateTemplateCommand>());
        PrivateField(PlanMember(plan, "SearchTemplatesQueryReader"), "_typeInfo").Should().BeSameAs(transport.ResolveTypeInfo<TemplateApiResource[]>());

        // A GET sends no body, so has no body metadata.
        plan.GetType().GetField("SearchTemplatesQueryRequest", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Should().BeNull();
    }

    private static SampleContractApi CreateSampleApi(FakeHttpHandler handler)
    {
        return new SampleContractApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = _baseAddress }));
    }

    private static LocalOperationsApi CreateLocalApi(FakeHttpHandler handler)
    {
        return new LocalOperationsApi(new BlueprintHttpClient(new System.Net.Http.HttpClient(handler) { BaseAddress = _baseAddress }));
    }

    // The plan a client holds: the generated half's private state, read by reflection because it is no public surface.
    private static object PlanOf(SampleContractApi client)
    {
        return PrivateField(client, "_plan")!;
    }

    private static object? PlanMember(object plan, string name)
    {
        var field = plan.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        field.Should().NotBeNull($"the plan holds {name}");

        return field!.GetValue(plan);
    }

    private static object? PrivateField(object? instance, string name)
    {
        var field = instance!.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull($"{instance.GetType().Name} has a field {name}");

        return field!.GetValue(instance);
    }

    // The reader BlueprintHttpClient<TApp> reads a response type with, chosen at run time by the internal AppResponseReaders.
    private static object AppReaderFor(Type resultType)
    {
        return typeof(BlueprintHttpClient).Assembly.GetType("Blueprint.HttpClient.Readers.AppResponseReaders", throwOnError: true)!
            .GetMethod("For", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(null, null)!;
    }

    // A reader's type, and for a union, its members' readers' in turn, which is the whole of what reads a response.
    private static string DescribeReader(object? reader)
    {
        var type = reader!.GetType();

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Readers.UnionResponseReader<,>)
            ? $"{type}({DescribeReader(PrivateField(reader, "_first"))}, {DescribeReader(PrivateField(reader, "_second"))})"
            : type.ToString();
    }

    private static IEnumerable<MethodInfo> OperationMethods(Type clientInterface)
    {
        return clientInterface.GetMethods().Where(static m => m.GetParameters().FirstOrDefault()?.Name == "operation");
    }

    // Sends one call to a handler that records the request and answers with a not-found problem, which the transport raises
    // whatever the result type, so only the request is under test.
    private static async Task<SentRequest> CaptureAsync(Func<FakeHttpHandler, Task> send)
    {
        var captured = new List<SentRequest>();
        var handler = new FakeHttpHandler(req =>
        {
            captured.Add(new SentRequest(req.Method.Method, req.RequestUri!.PathAndQuery, req.Content?.ReadAsStringAsync().GetAwaiter().GetResult()));

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"title":"Not Found","status":404}""", Encoding.UTF8, "application/problem+json"),
            };
        });

        var act = () => send(handler);

        await act.Should().ThrowAsync<BlueprintApiException>();

        return captured.Should().ContainSingle().Subject;
    }

    // Answers every request with the result as the HTTP host writes it.
    private static async Task<FakeHttpHandler> RespondAsTheHostWritesAsync(OperationResult written)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>()
            .BuildServiceProvider();
        var app = services.GetRequiredService<SampleBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(CreateTemplateCommand)), CancellationToken.None);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        (await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, written)).Should().BeTrue();

        var status = (HttpStatusCode)httpContext.Response.StatusCode;
        var bytes = body.ToArray();
        var mediaType = MediaTypeHeaderValue.Parse(httpContext.Response.ContentType!);

        return new FakeHttpHandler(_ => new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(bytes) { Headers = { ContentType = mediaType } },
        });
    }

    /// <summary>
    /// What a request sent: its method, its path and query, and its body.
    /// </summary>
    public sealed record SentRequest(string Method, string PathAndQuery, string? Body);

    /// <summary>
    /// One operation a typed client includes: the method it is called through, an operation to send, and the result type
    /// the operation declares, which the method must return.
    /// </summary>
    public sealed class ClientCase
    {
        private readonly Func<IOperation> _createOperation;
        private readonly Func<ISampleContractApi, IOperation, Task> _send;

        private ClientCase(string methodName, Type operationType, Type declaredResult, Func<IOperation> createOperation, Func<ISampleContractApi, IOperation, Task> send)
        {
            this.MethodName = methodName;
            this.OperationType = operationType;
            this.DeclaredResult = declaredResult;
            this._createOperation = createOperation;
            this._send = send;
        }

        public string MethodName { get; }

        public Type OperationType { get; }

        public Type DeclaredResult { get; }

        // The call is typed to return TResult, so a method returning any other type does not compile.
        public static ClientCase Of<TOperation, TResult>(string methodName, Func<TOperation> createOperation, Func<ISampleContractApi, TOperation, Task<TResult>> send)
            where TOperation : IOperation
        {
            return new ClientCase(methodName, typeof(TOperation), typeof(TResult), () => createOperation(), (api, op) => send(api, (TOperation)op));
        }

        public IOperation CreateOperation()
        {
            return this._createOperation();
        }

        public Task SendAsync(ISampleContractApi api, IOperation operation)
        {
            return this._send(api, operation);
        }

        public override string ToString()
        {
            return this.MethodName;
        }
    }
}
