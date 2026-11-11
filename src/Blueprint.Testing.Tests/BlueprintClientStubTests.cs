using System.Net.Http.Headers;
using Blueprint.HttpClient;
using Blueprint.Model;
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
public partial class StubbedTemplatesApi;

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
