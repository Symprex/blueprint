using System.Net.Http.Headers;
using Blueprint.Http;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.Testing.Tests;

/// <summary>
/// <see cref="BlueprintClientStubServiceCollectionExtensions.StubBlueprintClient{TClient}"/>: a <c>[BlueprintClient]</c>'s
/// upstream answered and recorded by inner operation type, for a typed-client call and a gateway's forward alike, with no
/// route restated.
/// </summary>
public sealed class BlueprintClientStubTests
{
    private static readonly Uri _registeredAddress = new("https://templates.example.test/api/");

    [Fact]
    public async Task A_typed_client_call_is_answered_and_recorded_by_operation_type()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static q => new TemplateApiResource { Id = q.Id, Name = "From the stub" });

        var result = await provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        result.IsT1.Should().BeTrue();
        result.AsT1.Content.Name.Should().Be("From the stub");
        result.AsT1.Content.Id.Should().Be("t-1");
        upstream.Received<GetTemplateQuery>().Should().ContainSingle().Which.Id.Should().Be("t-1");
    }

    [Fact]
    public async Task A_forward_through_the_clients_transport_is_answered_by_operation_type()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplatesQuery>(static _ => new[] { new TemplateApiResource { Id = "t-2", Name = "Listed" } });

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<GetTemplatesQuery, TemplateApiResource[]>(new GetTemplatesQuery());

        result.Should().ContainSingle().Which.Name.Should().Be("Listed");
        upstream.Received<GetTemplatesQuery>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_client_registered_without_a_base_address_is_given_one()
    {
        var services = new ServiceCollection();
        var upstream = services.StubBlueprintClient<StubbedTemplatesApi>();
        upstream.Respond<GetTemplatesQuery>(static _ => Array.Empty<TemplateApiResource>());
        await using var provider = services.BuildServiceProvider();

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<GetTemplatesQuery, TemplateApiResource[]>(new GetTemplatesQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_is_recorded_with_its_wire_request()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<AddTenantNoteCommand>(static c => new TenantNoteApiResource { TenantId = c.TenantId, Text = c.Text });

        var note = await provider.GetRequiredService<IStubbedTemplatesApi>()
            .AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "tenant-a", Text = "Hello" });

        note.Text.Should().Be("Hello");
        var request = upstream.Requests.Should().ContainSingle().Subject;
        request.Operation.Should().BeOfType<AddTenantNoteCommand>().Which.TenantId.Should().Be("tenant-a");
        request.Method.Should().Be(HttpMethod.Post);
        request.Body.Should().Contain("\"text\":\"Hello\"");
    }

    [Fact]
    public async Task A_not_found_result_is_read_as_the_declared_not_found_member()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static q => new NotFoundResult<TemplateApiResource>(new TemplateApiResource { Id = q.Id, Name = "Missing" }));

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<GetTemplateQuery, Results<OkResult<TemplateApiResource>, NotFoundResult<TemplateApiResource>>>(
                new GetTemplateQuery { Id = "t-404" });

        result.IsT2.Should().BeTrue();
        result.AsT2.Content.Id.Should().Be("t-404");
    }

    [Fact]
    public async Task A_thrown_api_exception_is_answered_as_its_problem()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static _ => throw new ApiException(
            "Template archived",
            "template_archived",
            "The template was archived.",
            409,
            new Dictionary<string, object> { ["ArchivedBy"] = "someone" }));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        var thrown = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        thrown.Type.Should().Be("template_archived");
        thrown.Detail.Should().Be("The template was archived.");
        thrown.HttpStatus.Should().Be(409);
        thrown.Extensions.Should().ContainKey("archivedBy");
    }

    [Fact]
    public async Task A_thrown_validation_exception_is_answered_as_a_422()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<AddTenantNoteCommand>(static _ => throw new Blueprint.Validation.ValidationException("Text", "The note is required."));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "t" });

        var thrown = (await act.Should().ThrowAsync<Blueprint.Validation.ValidationException>()).Which;
        thrown.Errors.Should().ContainKey("text").WhoseValue.Should().Equal("The note is required.");
    }

    [Fact]
    public async Task An_operation_with_no_response_fails_naming_it_and_the_client()
    {
        await using var provider = BuildProvider(out var upstream);

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*StubbedTemplatesApi*GetTemplateQuery*Respond<GetTemplateQuery>*");
        upstream.Received<GetTemplateQuery>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_request_the_client_did_not_build_from_an_operation_is_refused()
    {
        await using var provider = BuildProvider(out _);
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(StubbedTemplatesApi));

        var act = () => http.GetAsync("templates/raw");

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*GET*templates/raw*");
    }

    [Fact]
    public async Task The_request_authorization_is_recorded_apart_from_the_clients_default_header()
    {
        var services = new ServiceCollection();
        services.AddBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) =>
        {
            client.BaseAddress = _registeredAddress;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "default");
        });
        var upstream = services.StubBlueprintClient<StubbedTemplatesApi>();
        upstream.Respond<GetTemplatesQuery>(static _ => Array.Empty<TemplateApiResource>());
        await using var provider = services.BuildServiceProvider();
        var transport = BlueprintClientTransport.Create<StubbedTemplatesApi>(provider);

        await transport.ForwardAsync<GetTemplatesQuery, TemplateApiResource[]>(new GetTemplatesQuery());
        await transport.ForwardAsync<GetTemplatesQuery, TemplateApiResource[]>(
            new GetTemplatesQuery(),
            new CallOptions { ModifyRequest = static r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller") });

        upstream.Requests[0].Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "default"));
        upstream.Requests[0].RequestAuthorization.Should().BeNull();
        upstream.Requests[1].Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "caller"));
        upstream.Requests[1].RequestAuthorization.Should().Be(new AuthenticationHeaderValue("Bearer", "caller"));
    }

    [Fact]
    public async Task Concurrent_calls_are_each_answered_and_recorded()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static q => new TemplateApiResource { Id = q.Id, Name = q.Id });
        var api = provider.GetRequiredService<IStubbedTemplatesApi>();

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() => api.GetTemplateAsync(new GetTemplateQuery { Id = $"t-{i}" }))));

        results.Select(static r => r.AsT1.Content.Name).Should().BeEquivalentTo(Enumerable.Range(0, 64).Select(static i => $"t-{i}"));
        upstream.Received<GetTemplateQuery>().Select(static q => q.Id).Should().HaveCount(64).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Stubbing_the_same_client_twice_returns_the_same_stub()
    {
        var services = new ServiceCollection();

        services.StubBlueprintClient<StubbedTemplatesApi>().Should().BeSameAs(services.StubBlueprintClient<StubbedTemplatesApi>());
    }

    [Fact]
    public async Task A_plain_text_result_keeps_its_content_type()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<HealthCheckQuery>(static _ => new PlainTextResult("BEGIN:VCARD") { ContentType = "text/vcard" });

        var result = await provider.GetRequiredService<IStubbedTemplatesApi>().HealthCheckAsync(new HealthCheckQuery());

        result.Content.Should().Be("BEGIN:VCARD");
        result.ContentType.Should().Be("text/vcard");
    }

    [Fact]
    public async Task An_api_exception_extension_the_clients_context_does_not_register_is_still_written()
    {
        // Options whose only metadata is a context that does not list the extension's type, as a client with a strict
        // serializer context has; a host writes such an extension through its reflection fallback.
        await using var provider = BuildProvider(
            out var upstream,
            static json => json.TypeInfoResolver = StrictStubJsonContext.Default);
        upstream.Respond<GetTemplateQuery>(static _ => throw new ApiException(
            "Template archived",
            "template_archived",
            "The template was archived.",
            409,
            new Dictionary<string, object> { ["archivedBy"] = new UnregisteredArchiver("someone") }));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        var thrown = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        thrown.Type.Should().Be("template_archived");
        thrown.Extensions.Should().ContainKey("archivedBy");
    }

    [Fact]
    public async Task Extension_keys_a_policy_names_alike_keep_the_hosts_precedence()
    {
        // The host keeps the key already so named, whatever the dictionary's order: here the later one would otherwise win.
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static _ => throw new ApiException(
            "Clash",
            "clash",
            null,
            409,
            new Dictionary<string, object> { ["errors"] = "canonical", ["Errors"] = "other" }));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        var thrown = (await act.Should().ThrowAsync<BlueprintApiException>()).Which;
        thrown.Extensions["errors"].Should().Be("canonical");
    }

    [Fact]
    public async Task A_returned_validation_failure_with_no_member_is_a_form_level_error()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<AddTenantNoteCommand>(static _ => new ValidationFailedResult([new System.ComponentModel.DataAnnotations.ValidationResult("Invalid")]));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "t" });

        var thrown = (await act.Should().ThrowAsync<Blueprint.Validation.ValidationException>()).Which;
        thrown.Errors.Should().ContainKey("form").WhoseValue.Should().Equal("Invalid");
    }

    [Fact]
    public async Task A_validation_key_is_named_as_the_host_names_it_leaving_an_indexers_key_alone()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<AddTenantNoteCommand>(static _ => throw new Blueprint.Validation.ValidationException(
            new Dictionary<string, string[]> { ["Items[Foo.Bar].Name"] = ["Bad name."] }));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "t" });

        var thrown = (await act.Should().ThrowAsync<Blueprint.Validation.ValidationException>()).Which;
        thrown.Errors.Should().ContainKey("items[Foo.Bar].name");
    }

    [Fact]
    public async Task The_form_level_key_is_not_renamed_by_the_policy()
    {
        await using var provider = BuildProvider(
            out var upstream,
            static json => json.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseUpper);
        upstream.Respond<AddTenantNoteCommand>(static _ => throw new Blueprint.Validation.ValidationException("The note is not allowed."));

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "t" });

        var thrown = (await act.Should().ThrowAsync<Blueprint.Validation.ValidationException>()).Which;
        thrown.Errors.Should().ContainKey("form");
    }

    [Fact]
    public async Task An_async_enumerable_body_is_written_as_the_host_streams_it()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplatesQuery>(static _ => Stream());

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<GetTemplatesQuery, TemplateApiResource[]>(new GetTemplatesQuery());

        result.Select(static t => t.Id).Should().Equal("t-1", "t-2");

        static async IAsyncEnumerable<TemplateApiResource> Stream()
        {
            await Task.Yield();
            yield return new TemplateApiResource { Id = "t-1", Name = "One" };
            yield return new TemplateApiResource { Id = "t-2", Name = "Two" };
        }
    }

    [Fact]
    public async Task A_plain_text_result_at_a_bodiless_status_is_written_without_its_text()
    {
        // The host writes a 204, 205 or 304 plain-text result as its status alone (TryGetBodilessStatusCode).
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<HealthCheckQuery>(static _ => new PlainTextResult("ignored", System.Net.HttpStatusCode.NoContent));

        // A forward declaring both members reads the host's empty 204 as the no-content member, never as text.
        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<HealthCheckQuery, Results<Blueprint.Runtime.Http.HttpNoContentResult, PlainTextResult>>(new HealthCheckQuery());

        result.IsT1.Should().BeTrue();
    }

    [Fact]
    public async Task A_no_result_is_the_hosts_empty_200()
    {
        // The host writes NoResultOperationResult as an empty 200, the only status the client reads it from.
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<ChangePasswordCommand>(static _ => NoResultOperationResult.Instance);

        var result = await provider.GetRequiredService<IStubbedTemplatesApi>().ChangePasswordAsync(new ChangePasswordCommand());

        result.Should().BeSameAs(NoResultOperationResult.Instance);
    }

    [Fact]
    public async Task A_commands_content_headers_are_recorded()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<AddTenantNoteCommand>(static c => new TenantNoteApiResource { TenantId = c.TenantId, Text = c.Text });

        await provider.GetRequiredService<IStubbedTemplatesApi>().AddTenantNoteAsync(new AddTenantNoteCommand { TenantId = "t", Text = "Hi" });

        upstream.Requests.Single().Headers.Should().ContainKey("Content-Type")
            .WhoseValue.Should().ContainSingle().Which.Should().StartWith("application/json");
    }

    [Fact]
    public async Task A_resource_event_is_written_as_the_host_writes_it_and_read_back_as_the_event()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<IssueTemplateKeyCommand>(static c => new ResourceEvent<TemplateApiResource>(
                ResourceEventChangeType.Created,
                "template.keyIssued",
                new TemplateApiResource { Id = c.TemplateId, Name = "Keyed" })
            .WithMetadata("issuedBy", "someone"));

        var resourceEvent = await provider.GetRequiredService<IStubbedTemplatesApi>().IssueTemplateKeyAsync(new IssueTemplateKeyCommand { TemplateId = "t-1" });

        resourceEvent.ChangeType.Should().Be(ResourceEventChangeType.Created);
        resourceEvent.EventId.Should().Be("template.keyIssued");
        resourceEvent.Data!.Id.Should().Be("t-1");
        resourceEvent.Data.Name.Should().Be("Keyed");
        resourceEvent.Metadata.Should().ContainKey("issuedBy").WhoseValue.ToString().Should().Be("someone");
    }

    [Fact]
    public async Task A_resource_event_is_a_200_whatever_its_change_type()
    {
        // Legacy, and so the host, sent every resource event as a 200, a created one included: it is read as the 200 member.
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<IssueTemplateKeyCommand>(static c => new ResourceCreated<TemplateApiResource>(new TemplateApiResource { Id = c.TemplateId, Name = "Keyed" }));

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<IssueTemplateKeyCommand, Results<OkResult<TemplateApiResource>, CreatedResult<TemplateApiResource>>>(new IssueTemplateKeyCommand { TemplateId = "t-1" });

        result.IsT1.Should().BeTrue();
    }

    [Fact]
    public async Task A_resource_event_result_is_written_as_the_hosts_envelope_at_its_change_types_status()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<CreateTemplateCommand>(static c => new ResourceEventResult<TemplateApiResource>(
                ChangeType.Created,
                "template.created",
                "t-9",
                new TemplateApiResource { Id = "t-9", Name = c.Name! })
            .WithMetadata("source", "stub")
            .WithChangedValue("name", "Before"));

        var created = await provider.GetRequiredService<IStubbedTemplatesApi>().CreateTemplateAsync(new CreateTemplateCommand { Name = "Created" });

        created.ChangeType.Should().Be(ChangeType.Created);
        created.EventId.Should().Be("template.created");
        created.Content.Id.Should().Be("t-9");
        created.Content.Name.Should().Be("Created");
        created.Metadata.Should().ContainKey("source").WhoseValue.ToString().Should().Be("stub");
        created.ChangedValues.Should().ContainKey("name").WhoseValue.ToString().Should().Be("Before");
    }

    [Fact]
    public async Task A_resource_event_result_is_a_201_when_created_and_a_200_when_updated()
    {
        await using var provider = BuildProvider(out var upstream);
        var transport = BlueprintClientTransport.Create<StubbedTemplatesApi>(provider);

        upstream.Respond<CreateTemplateCommand>(static _ => new ResourceEventResult<TemplateApiResource>(ChangeType.Created, "template.created", "t-1", new TemplateApiResource { Id = "t-1", Name = "n" }));
        var created = await transport.ForwardAsync<CreateTemplateCommand, Results<OkResult<TemplateApiResource>, CreatedResult<TemplateApiResource>>>(new CreateTemplateCommand { Name = "n" });

        upstream.Respond<CreateTemplateCommand>(static _ => new ResourceEventResult<TemplateApiResource>(ChangeType.Updated, "template.updated", "t-1", new TemplateApiResource { Id = "t-1", Name = "n" }));
        var updated = await transport.ForwardAsync<CreateTemplateCommand, Results<OkResult<TemplateApiResource>, CreatedResult<TemplateApiResource>>>(new CreateTemplateCommand { Name = "n" });

        created.IsT2.Should().BeTrue();
        updated.IsT1.Should().BeTrue();
    }

    [Fact]
    public async Task A_deleted_resource_event_result_is_the_hosts_empty_204()
    {
        // The host writes a deleted resource event result as its status alone (TryGetBodilessStatusCode).
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<CreateTemplateCommand>(static _ => new ResourceEventResult<TemplateApiResource>(ChangeType.Deleted, "template.deleted", "t-1", new TemplateApiResource { Id = "t-1", Name = "n" }));

        var result = await BlueprintClientTransport.Create<StubbedTemplatesApi>(provider)
            .ForwardAsync<CreateTemplateCommand, Results<Blueprint.Runtime.Http.HttpNoContentResult, ResourceEventResult<TemplateApiResource>>>(new CreateTemplateCommand { Name = "n" });

        result.IsT1.Should().BeTrue();
    }

    [Fact]
    public async Task A_key_only_resource_event_result_is_its_change_types_status()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<ArchiveTemplateCommand>(static c => new KeyOnlyResourceEventResult(ChangeType.Deleted, "template.archived", c.Id));

        var archived = await provider.GetRequiredService<IStubbedTemplatesApi>().ArchiveTemplateAsync(new ArchiveTemplateCommand { Id = "t-1" });

        // The client reads the change type back from the status alone, so only a 204 is read as deleted.
        archived.ChangeType.Should().Be(ChangeType.Deleted);
        upstream.Received<ArchiveTemplateCommand>().Should().ContainSingle().Which.Id.Should().Be("t-1");
    }

    [Fact]
    public async Task A_result_the_stub_cannot_write_is_refused_without_suggesting_its_content()
    {
        await using var provider = BuildProvider(out var upstream);
        upstream.Respond<GetTemplateQuery>(static _ => new UnwritableResult());

        var act = () => provider.GetRequiredService<IStubbedTemplatesApi>().GetTemplateAsync(new GetTemplateQuery { Id = "t-1" });

        var thrown = (await act.Should().ThrowAsync<NotSupportedException>()).Which;
        thrown.Message.Should().Contain("UnwritableResult").And.Contain("ResourceEvent").And.NotContain("its content");
    }

    private static ServiceProvider BuildProvider(
        out BlueprintClientStub<StubbedTemplatesApi> upstream,
        Action<System.Text.Json.JsonSerializerOptions>? configureJson = null)
    {
        var services = new ServiceCollection();

        if (configureJson is not null)
        {
            services.Configure<Blueprint.Http.BlueprintJsonOptions>(options => configureJson(options.SerializerOptions));
        }

        services.AddBlueprintClient<IStubbedTemplatesApi, StubbedTemplatesApi>(static (_, client) => client.BaseAddress = _registeredAddress);
        upstream = services.StubBlueprintClient<StubbedTemplatesApi>();

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// The typed client <see cref="BlueprintClientStubTests"/> stubs.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(GetTemplateQuery))]
[BlueprintClientInclude(typeof(GetTemplatesQuery))]
[BlueprintClientInclude(typeof(AddTenantNoteCommand))]
[BlueprintClientInclude(typeof(HealthCheckQuery))]
[BlueprintClientInclude(typeof(ChangePasswordCommand))]
[BlueprintClientInclude(typeof(CreateTemplateCommand))]
[BlueprintClientInclude(typeof(ArchiveTemplateCommand))]
[BlueprintClientInclude(typeof(IssueTemplateKeyCommand))]
public partial class StubbedTemplatesApi;

/// <summary>
/// A command whose result is a legacy <see cref="ResourceEvent{TResource}"/>, the shape the stub once refused.
/// </summary>
[RootLink("/templates/{TemplateId}/keys")]
[HttpPost]
public partial class IssueTemplateKeyCommand : ICommand<ResourceEvent<TemplateApiResource>>
{
    /// <summary>
    /// Gets or sets the template the key is issued for.
    /// </summary>
    public required string TemplateId { get; set; }

    /// <summary>
    /// Handles it for <see cref="RouteFormatTestApp"/>, which scans this assembly; the stub never calls it.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <returns>The event.</returns>
    public static ResourceEvent<TemplateApiResource> Handle(IssueTemplateKeyCommand command)
    {
        return new ResourceCreated<TemplateApiResource>(new TemplateApiResource { Id = command.TemplateId, Name = "Keyed" });
    }
}

/// <summary>
/// A result no Blueprint host writes, so the stub cannot either.
/// </summary>
public sealed class UnwritableResult : OperationResult
{
    /// <inheritdoc />
    public override Task ExecuteAsync(ApiOperationContext context)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// An extension value no serializer context registers.
/// </summary>
/// <param name="Name">Who archived it.</param>
public sealed record UnregisteredArchiver(string Name);

/// <summary>
/// A context that registers only what the strict-context test reads, and not <see cref="UnregisteredArchiver"/>.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(TemplateApiResource))]
public sealed partial class StrictStubJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
