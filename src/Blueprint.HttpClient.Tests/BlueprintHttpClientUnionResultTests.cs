using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Sample;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves that <see cref="BlueprintHttpClient{TApp}"/> reads a response declared as a <see cref="Results{T1,T2}"/> into
/// the member the host wrote, rather than deserialising every body as JSON: a status-only result by its status and
/// empty body, a <see cref="PlainTextResult"/> by its text content type, and any other member from JSON.
/// </summary>
/// <remarks>
/// The fake responses are shaped as <c>HttpOperationResultExecutor</c> writes them: a <see cref="StatusCodeResult"/>
/// sets only the status, and a <see cref="PlainTextResult"/> is written as <c>text/plain; charset=utf-8</c>.
/// </remarks>
public class BlueprintHttpClientUnionResultTests : IClassFixture<SampleAppFixture>
{
    private const string TemplateJson = """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""";

    private readonly SampleAppFixture _fixture;

    public BlueprintHttpClientUnionResultTests(SampleAppFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task When_the_host_wrote_the_status_only_member_then_returns_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task When_the_host_wrote_the_plain_text_member_then_returns_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("The return content", Encoding.UTF8, "text/plain"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Should().Be("The return content");
        result.AsT2.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task When_the_host_wrote_plain_text_with_the_status_only_members_status_then_returns_the_text()
    {
        // A status-only result has no body, so a 202 that has one is the other member.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("Queued", Encoding.UTF8, "text/plain"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Should().Be("Queued");
    }

    [Fact]
    public async Task When_the_host_wrote_a_json_member_then_deserialises_it()
    {
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"id":"t-1","name":"Found Template","htmlSource":"<p>x</p>","plainTextSource":"x","isArchived":false}""",
                Encoding.UTF8,
                "application/json"),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, TemplateApiResource>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_the_host_wrote_no_content_that_no_member_claims_then_throws()
    {
        // A 204 neither member claims is a contract mismatch like any other response none claims (below): a default union
        // holds no member at all, so every IsT* is false and every AsT* throws, far from the call that went wrong. Legacy's
        // client, which had no unions, threw for any response it could not understand (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:228).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var act = () => client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*204*matches none of*");
    }

    [Fact]
    public async Task When_the_response_matches_no_member_then_throws()
    {
        // 200 with a JSON body is neither the 202 nor text: returning a default union would hide a contract mismatch.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", new MediaTypeHeaderValue("application/json")),
        }));

        var act = () => client.SendAsync<Results<StatusCodeResult.Accepted, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*matches none of*");
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_event_member_then_deserialises_it()
    {
        // The host writes a legacy resource event as a 200 JSON envelope (HttpOperationResultExecutor). Here it is an
        // OperationResult; legacy's was a plain object its client read from any JSON body (Signature365 8b40ecc961,
        // Common.Core/Api/BlueprintHttpClient.cs:199-211).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"$object\":\"event\",\"eventId\":\"template.created\",\"changeType\":\"Created\",\"resourceObject\":\"template\",\"data\":" + TemplateJson + "}",
                new MediaTypeHeaderValue("application/json")),
        }));

        var result = await client.SendAsync<Results<StatusCodeResult.Accepted, ResourceEvent<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.EventId.Should().Be("template.created");
        result.AsT2.Data!.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_the_host_wrote_a_status_only_response_for_the_base_status_code_member_then_returns_that_status()
    {
        // Legacy's client made a StatusCodeResult of the response's status for a response that was neither JSON nor text
        // (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:223-226). The base type is abstract here, so it is
        // the status's own result.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.Accepted)));

        var result = await client.SendAsync<Results<StatusCodeResult, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.StatusCode.Should().Be(202);
        result.AsT1.Should().BeSameAs(StatusCodeResult.Accepted.Instance);
    }

    [Fact]
    public async Task When_the_host_wrote_an_ok_member_then_deserialises_its_content()
    {
        // The host writes an OkResult<T> as its content, a 200 JSON body; the Sample's GetTemplateQuery is such a union.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TemplateJson, new MediaTypeHeaderValue("application/json")),
        }));

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("Found Template");
    }

    [Fact]
    public async Task When_two_members_could_be_the_json_the_host_wrote_then_throws()
    {
        // Both members are written as a 200 JSON body, so the body cannot say which one the host wrote: reading it as the
        // first would silently hide a contract no response can satisfy.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TemplateJson, new MediaTypeHeaderValue("application/json")),
        }));

        var act = () => client.SendAsync<Results<TemplateApiResource, OkResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*more than one*");
    }

    [Fact]
    public async Task When_the_host_wrote_no_content_for_an_http_no_content_member_then_returns_it()
    {
        // The host writes an HttpNoContentResult as an empty 204 with no content type (HttpOperationResultExecutor); a union
        // may declare one (BP112 keeps it apart from NoResultOperationResult), so a 204 is that member, not an unmatched one.
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, HttpNoContentResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Should().BeOfType<HttpNoContentResult>();
    }

    [Fact]
    public async Task When_the_host_wrote_an_empty_200_for_a_no_result_member_then_returns_it()
    {
        // The host writes a NoResultOperationResult as an empty 200 with no content type (HttpOperationResultExecutor).
        var client = this._fixture.CreateClient(new FakeHttpHandler(static _ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await client.SendAsync<Results<NoResultOperationResult, PlainTextResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Should().BeSameAs(NoResultOperationResult.Instance);
    }

    // KeyOnlyResourceEventResult and HttpAcceptedResult are new-only (Signature365 declares neither), so these rest on what
    // this host writes, not on legacy: each response below is the one HttpOperationResultExecutor writes for the member.
    [Fact]
    public async Task When_the_host_wrote_an_accepted_member_without_a_body_then_returns_it()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(new HttpAcceptedResult());

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, HttpAcceptedResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Should().BeOfType<HttpAcceptedResult>();
    }

    [Theory]
    [InlineData(ChangeType.Created)]
    [InlineData(ChangeType.Updated)]
    [InlineData(ChangeType.Deleted)]
    public async Task When_the_host_wrote_a_key_only_resource_event_member_then_returns_it_with_its_change_type(ChangeType changeType)
    {
        // The host writes one as an empty 201, 200 or 204 by its change type; the body carries neither id, so neither is read.
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(new KeyOnlyResourceEventResult(changeType, "template.changed", "t-1"));

        var result = await client.SendAsync<Results<OkResult<TemplateApiResource>, KeyOnlyResourceEventResult>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.ChangeType.Should().Be(changeType);
        result.AsT2.EventId.Should().BeEmpty("an empty response carries no event id");
        result.AsT2.ResourceId.Should().BeEmpty("an empty response carries no resource id");
    }

    // The host writes a legacy resource event as a 200 whatever its change type (OperationResultContractHelper, "Legacy
    // Blueprint.Http.ResourceEvent<T> ... the host sends a 200"; BlueprintHttpClientResourceEventTests pins it), and a plain
    // value as an OkResult's 200. So a legacy event or a plain type is the JSON body of a 200 only. These are new-only
    // unions (Signature365 declares no Results<,>), so the expected members come from the host's own writes.
    [Fact]
    public async Task When_the_host_wrote_the_created_result_beside_a_legacy_created_event_then_the_201_is_the_created_result()
    {
        // A union the generator accepts (no BP109: a 200 JSON body and a 201 JSON body).
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(new CreatedResult<TemplateApiResource>(new TemplateApiResource("t-1", "Created Template", "<p>x</p>", "x")));

        var result = await client.SendAsync<Results<ResourceCreated<TemplateApiResource>, CreatedResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Name.Should().Be("Created Template");
    }

    [Fact]
    public async Task When_the_host_wrote_the_legacy_created_event_beside_a_created_result_then_the_200_is_the_event()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(new ResourceCreated<TemplateApiResource>(new TemplateApiResource("t-1", "Evented Template", "<p>x</p>", "x")));

        var result = await client.SendAsync<Results<ResourceCreated<TemplateApiResource>, CreatedResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Data!.Name.Should().Be("Evented Template");
    }

    [Fact]
    public async Task When_the_host_wrote_the_created_result_beside_a_plain_type_then_the_201_is_the_created_result()
    {
        // A union only a caller chooses (a plain type is no server union member: BP111), read by the same rule.
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(new CreatedResult<TemplateApiResource>(new TemplateApiResource("t-1", "Created Template", "<p>x</p>", "x")));

        var result = await client.SendAsync<Results<TemplateApiResource, CreatedResult<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Name.Should().Be("Created Template");
    }

    // Two legacy events of distinct resources, a union the generator accepts (their [ResourceEvent] ids differ), are both a
    // 200 JSON body. The host writes each with its eventId as "{resource}.{sub-id}", chosen by the handler at run time, and
    // its resourceObject as its resource's type name (HttpOperationResultExecutor, ApiResource.GetTypeName), so the
    // resourceObject is what tells them apart. New-only: Signature365 declares no Results<,>.
    [Fact]
    public async Task When_the_host_wrote_one_of_two_legacy_events_of_distinct_resources_then_its_resource_object_says_which()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceUpdated<TemplateVersionApiResource>(new TemplateVersionApiResource(3, DateTimeOffset.UnixEpoch, "t-1")));

        var result = await client.SendAsync<Results<ResourceCreated<TemplateApiResource>, ResourceUpdated<TemplateVersionApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Data!.Version.Should().Be(3);
    }

    [Fact]
    public async Task When_the_host_wrote_the_other_of_two_legacy_events_of_distinct_resources_then_its_resource_object_says_which()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceCreated<TemplateApiResource>(new TemplateApiResource("t-1", "Evented Template", "<p>x</p>", "x")));

        var result = await client.SendAsync<Results<ResourceCreated<TemplateApiResource>, ResourceUpdated<TemplateVersionApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Data!.Name.Should().Be("Evented Template");
    }

    [Fact]
    public async Task When_two_legacy_events_are_of_the_same_resource_then_which_the_host_wrote_still_cannot_be_told()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceCreated<TemplateApiResource>(new TemplateApiResource("t-1", "Evented Template", "<p>x</p>", "x")));

        var act = () => client.SendAsync<Results<ResourceCreated<TemplateApiResource>, ResourceUpdated<TemplateApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*could be more than one*");
    }

    // ResourceEventResult<T> is new-only (Signature365 declares none), so these rest on what this host writes: the event as
    // its envelope ($object, eventId, changeType, created, resourceObject, href, data, and the dictionaries and correlation
    // id when set) with a 201 when created and a 200 when updated, and a deleted one as an empty 204
    // (HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync, MapChangeTypeToStatusCode). The envelope carries no
    // resource id.
    [Theory]
    [InlineData(ChangeType.Created)]
    [InlineData(ChangeType.Updated)]
    public async Task When_the_host_wrote_a_resource_event_result_member_then_returns_it_from_its_envelope(ChangeType changeType)
    {
        var written = new ResourceEventResult<TemplateApiResource>(changeType, "template.changed", "t-1", new TemplateApiResource("t-1", "Evented Template", "<p>x</p>", "x"))
            .WithMetadata("source", "import")
            .WithSecureData("token", "secret")
            .WithChangedValue("name", "Old Name");
        written.CorrelationId = "corr-1";
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(written);

        var result = await client.SendAsync<Results<ResourceEventResult<TemplateApiResource>, NotFoundResult<TemplateVersionApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.ChangeType.Should().Be(changeType);
        result.AsT1.EventId.Should().Be("template.changed");
        result.AsT1.ResourceId.Should().BeEmpty("the envelope carries no resource id");
        result.AsT1.Content.Name.Should().Be("Evented Template");
        result.AsT1.Metadata.Should().Equal(new Dictionary<string, object> { ["source"] = "import" });
        result.AsT1.ChangedValues.Should().Equal(new Dictionary<string, object> { ["name"] = "Old Name" });
        result.AsT1.SecureData.Should().Equal(new Dictionary<string, object> { ["token"] = "secret" });
        result.AsT1.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task When_the_host_wrote_a_deleted_resource_event_result_member_then_no_member_claims_its_empty_204()
    {
        // A deleted event is an empty 204, from which no content can be read; as for a resource event read alone, the client
        // throws rather than make a result with no content.
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceEventResult<TemplateApiResource>(ChangeType.Deleted, "template.deleted", "t-1", new TemplateApiResource("t-1", "Gone", "<p>x</p>", "x")));

        var act = () => client.SendAsync<Results<ResourceEventResult<TemplateApiResource>, NotFoundResult<TemplateVersionApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*matches none of the results*");
    }

    [Fact]
    public async Task When_the_host_wrote_one_of_two_resource_event_results_of_distinct_resources_then_its_resource_object_says_which()
    {
        // Both are a 201 JSON body. The host writes the resourceObject as the content's type name (ApiResource.GetTypeName),
        // so it tells them apart, as it does two legacy events.
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceEventResult<TemplateVersionApiResource>(ChangeType.Created, "template-version.created", "t-1", new TemplateVersionApiResource(3, DateTimeOffset.UnixEpoch, "t-1")));

        var result = await client.SendAsync<Results<ResourceEventResult<TemplateApiResource>, ResourceEventResult<TemplateVersionApiResource>>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Version.Should().Be(3);
    }

    [Fact]
    public async Task When_the_host_wrote_a_resource_event_result_alone_then_returns_it_from_its_envelope()
    {
        var client = await this.CreateClientAnsweringAsTheHostWritesAsync(
            new ResourceEventResult<TemplateApiResource>(ChangeType.Created, "template.created", "t-1", new TemplateApiResource("t-1", "Evented Template", "<p>x</p>", "x")));

        var result = await client.SendAsync<ResourceEventResult<TemplateApiResource>>(new ArchiveTemplateCommand { Id = "t-1" });

        result.Should().NotBeNull();
        result!.ChangeType.Should().Be(ChangeType.Created);
        result.EventId.Should().Be("template.created");
        result.Content.Name.Should().Be("Evented Template");
    }

    private async Task<BlueprintHttpClient<SampleBlueprintApp>> CreateClientAnsweringAsTheHostWritesAsync(OperationResult written)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddBlueprintApi<SampleBlueprintApp, SampleBlueprintAppExecutor>()
            .BuildServiceProvider();
        var app = services.GetRequiredService<SampleBlueprintApp>();
        var context = new ApiOperationContext(services, app, app.FindOperation(typeof(ArchiveTemplateCommand)), CancellationToken.None);
        var httpContext = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        (await new HttpOperationResultExecutor().TryExecuteAsync(httpContext, context, written)).Should().BeTrue();

        var status = (HttpStatusCode)httpContext.Response.StatusCode;
        var contentType = httpContext.Response.ContentType;
        var bytes = body.ToArray();

        return this._fixture.CreateClient(new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(status);

            if (contentType is not null || bytes.Length > 0)
            {
                response.Content = new ByteArrayContent(bytes);
                response.Content.Headers.ContentType = contentType is null ? null : MediaTypeHeaderValue.Parse(contentType);
            }

            return response;
        }));
    }
}