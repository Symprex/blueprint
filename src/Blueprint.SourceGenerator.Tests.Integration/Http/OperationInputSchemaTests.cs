using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves <see cref="IOperationInputSchemaProvider" /> gives each exposed operation one flattened JSON Schema: its route,
/// query, header and body properties as the properties of one object, with their constraints and descriptions, and none
/// the caller does not send (auth-context, system and cookie properties), failing for an operation two of whose
/// properties would take the same name (D21).
/// </summary>
/// <remarks>
/// The expected names, constraints and descriptions are read off the attributes declared on the fixtures below, not
/// from the projection, so agreeing with them is a claim about the contract.
/// </remarks>
public sealed class OperationInputSchemaTests
{
    private const string NoteOperationName = "AddOperationInputSchemaNote";

    [Fact]
    public void Route_query_header_and_body_properties_are_merged_into_one_object()
    {
        var schema = GetSchema(NoteOperationName);

        schema.Schema.GetProperty("type").GetString().Should().Be("object");

        // In the document's order: its parameters (by location, then name), then its body's properties.
        PropertyNames(schema.Schema).Should().Equal("X-Correlation-Id", "AccountId", "priority", "text", "address");

        schema.Properties.Should().Equal(
            new OperationInputProperty("X-Correlation-Id", OperationInputLocation.Header),
            new OperationInputProperty("AccountId", OperationInputLocation.Path),
            new OperationInputProperty("priority", OperationInputLocation.Query),
            new OperationInputProperty("text", OperationInputLocation.Body),
            new OperationInputProperty("address", OperationInputLocation.Body));
    }

    [Fact]
    public void Constraints_and_descriptions_are_carried_from_every_source()
    {
        var properties = GetSchema(NoteOperationName).Schema.GetProperty("properties");

        var accountId = properties.GetProperty("AccountId");
        accountId.GetProperty("maxLength").GetInt32().Should().Be(12);
        accountId.GetProperty("description").GetString().Should().Be("The account the note is added to.");

        var priority = properties.GetProperty("priority");
        priority.GetProperty("type").GetString().Should().Be("integer");
        priority.GetProperty("minimum").GetInt32().Should().Be(1);
        priority.GetProperty("maximum").GetInt32().Should().Be(10);

        var text = properties.GetProperty("text");
        text.GetProperty("minLength").GetInt32().Should().Be(3);
        text.GetProperty("maxLength").GetInt32().Should().Be(200);
        text.GetProperty("description").GetString().Should().Be("The text of the note.");
    }

    [Fact]
    public void Required_lists_the_route_property_and_the_required_body_property()
    {
        var required = GetSchema(NoteOperationName).Schema.GetProperty("required").EnumerateArray().Select(static e => e.GetString());

        required.Should().BeEquivalentTo("AccountId", "text");
    }

    [Fact]
    public void Auth_context_and_cookie_properties_are_not_in_the_schema()
    {
        var schema = GetSchema(NoteOperationName);
        var names = PropertyNames(schema.Schema);

        names.Should().NotContain(static name => name.Equals("TenantId", StringComparison.OrdinalIgnoreCase), "the auth pipeline sets it, never the caller");
        names.Should().NotContain("session", "a cookie is not something a tool call can carry");
        schema.Properties.Should().NotContain(static p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_nested_body_type_is_defined_within_the_schema_itself()
    {
        var schema = GetSchema(NoteOperationName).Schema;
        var reference = FindReference(schema.GetProperty("properties").GetProperty("address"));

        reference.Should().StartWith("#/$defs/");

        var definition = schema.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);
        definition.GetProperty("properties").GetProperty("postCode").GetProperty("maxLength").GetInt32().Should().Be(8);

        schema.GetRawText().Should().NotContain("#/components/", "the schema must stand alone, outside the OpenAPI document");
    }

    [Fact]
    public void A_nullable_property_admits_null_as_JSON_Schema_spells_it()
    {
        var schema = GetSchema(NoteOperationName).Schema;
        var correlationId = schema.GetProperty("properties").GetProperty("X-Correlation-Id");

        correlationId.GetProperty("type").EnumerateArray().Select(static e => e.GetString()).Should().BeEquivalentTo("string", "null");
        schema.GetRawText().Should().NotContain("\"nullable\"", "nullable is OpenAPI 3.0's keyword, which JSON Schema does not have");
    }

    [Fact]
    public void The_operation_id_is_the_one_the_document_gives_the_operations_first_declared_url()
    {
        GetSchema(NoteOperationName).OperationId.Should().Be(NoteOperationName);

        // Declared at "z-first" and then "a-second", whose document ids are CheckOperationIdDomains and ...2.
        GetSchema("CheckOperationIdDomains").OperationId.Should().Be("CheckOperationIdDomains");
    }

    [Fact]
    public void An_operation_a_processor_renames_is_still_had_by_its_descriptor_name_with_its_first_declared_urls_id()
    {
        var provider = CreateProvider(configureOpenApiOperations: static o => o.Processors.Add(
            static (operation, _) => operation.OperationName == "CheckOperationIdDomains" ? operation with { OperationName = "PublicCheckDomains" } : operation));

        var schema = provider.GetSchema("CheckOperationIdDomains");

        schema.Should().NotBeNull();
        schema!.OperationName.Should().Be("CheckOperationIdDomains");

        // Declared at "z-first" and then "a-second", which the renamed operation's ids are written for in that order.
        schema.OperationId.Should().Be("PublicCheckDomains");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_read_only_property_of_a_body_type_is_not_required_of_an_input(OpenApiSchemaNaming naming)
    {
        // A non-nullable property is required in the document, as in a response; one with no setter is not sent in a request.
        var schema = CreateProvider(o =>
        {
            o.RequiredProperties = OpenApiRequiredProperties.NonNullable;
            o.SchemaNaming = naming;
        }).GetSchema(NoteOperationName)!.Schema;
        var reference = FindReference(schema.GetProperty("properties").GetProperty("address"));
        var address = schema.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);

        address.GetProperty("properties").GetProperty("total").GetProperty("readOnly").GetBoolean().Should().BeTrue();
        address.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Contain("postCode").And.NotContain("total");
    }

    [Fact]
    public void An_operation_two_of_whose_properties_take_one_name_fails_naming_both_sources()
    {
        var provider = CreateProvider();

        var act = () => provider.GetSchema(CollidingOperationName);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{CollidingOperationName}*'name'*query*body*");
    }

    [Fact]
    public void An_operation_two_of_whose_body_properties_take_one_name_fails_naming_both_and_no_other_operation_fails()
    {
        // The generator gives two body properties one name when its naming policy does (Label and LABEL), which this app
        // cannot declare as its typed client would then take two parameters of one name. A processor gives the model
        // that shape instead.
        var provider = CreateProvider(configureOpenApiOperations: static o => o.Processors.Add(RenameCaptionToLabelInBody));

        var act = () => provider.GetSchema(DuplicateBodyNameOperationName);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{DuplicateBodyNameOperationName}*'label'*body property Label*body property Caption*");
        provider.GetSchema(NoteOperationName).Should().NotBeNull("one operation's collision is that operation's alone");
    }

    [Fact]
    public void An_operation_whose_rendered_body_has_one_name_twice_fails_and_no_other_operation_fails()
    {
        var provider = CreateProvider(configureOpenApiSchemas: static o => o.Processors.Add(RenameCaptionToLabelInComponent));

        var act = () => provider.GetSchema(DuplicateBodyNameOperationName);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{DuplicateBodyNameOperationName}*");
        provider.GetSchema(NoteOperationName).Should().NotBeNull("one operation's broken document entry is that operation's alone");
    }

    [Fact]
    public void A_component_with_a_read_only_property_and_one_name_twice_fails_only_the_operations_that_use_it()
    {
        // The address has a read-only property, so the input schemas look at its properties before any one operation's.
        var provider = CreateProvider(configureOpenApiSchemas: static o => o.Processors.Add(RenamePostCodeToTotalInAddress));

        var act = () => provider.GetSchema(NoteOperationName);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{NoteOperationName}*");
        provider.GetSchema(DuplicateBodyNameOperationName).Should().NotBeNull("an operation that does not use the address is not broken by it");
    }

    [Fact]
    public void A_conditional_requirement_of_a_read_only_property_is_not_asked_of_an_input()
    {
        var schema = GetSchema(NoteOperationName).Schema;
        var reference = FindReference(schema.GetProperty("properties").GetProperty("address"));
        var address = schema.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);

        address.GetProperty("properties").GetProperty("district").GetProperty("readOnly").GetBoolean().Should().BeTrue();
        ConditionallyRequired(address).Should().NotContain("district");
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_conditional_requirement_of_a_writable_property_marked_read_only_is_still_asked_of_an_input(OpenApiSchemaNaming naming)
    {
        // [ReadOnly(true)] leaves the setter, so the body reader still sets the region and validation still requires it.
        var schema = CreateProvider(o => o.SchemaNaming = naming).GetSchema(NoteOperationName)!.Schema;
        var reference = FindReference(schema.GetProperty("properties").GetProperty("address"));
        var address = schema.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);

        address.GetProperty("properties").GetProperty("region").GetProperty("readOnly").GetBoolean().Should().BeTrue();
        ConditionallyRequired(address).Should().Contain("region");
    }

    [Fact]
    public async Task A_request_body_without_a_writable_property_validation_requires_is_refused_however_it_is_documented()
    {
        // What the schema above requires when the post code is SW1A 1AA is what validation requires of the request.
        await using var host = await KestrelTestHost.StartAsync();

        using var without = new StringContent("""{"text":"abc","address":{"postCode":"SW1A 1AA","previous":null}}""", System.Text.Encoding.UTF8, "application/json");
        using var refused = await host.Client.PostAsync("api/operation-input-schema/acc/notes?priority=1", without);
        using var with = new StringContent("""{"text":"abc","address":{"postCode":"SW1A 1AA","previous":null,"region":"London"}}""", System.Text.Encoding.UTF8, "application/json");
        using var accepted = await host.Client.PostAsync("api/operation-input-schema/acc/notes?priority=1", with);

        ((int)refused.StatusCode).Should().BeInRange(400, 499);
        accepted.IsSuccessStatusCode.Should().BeTrue(await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_operation_answering_GET_and_POST_is_described_at_the_method_that_reads_its_inputs()
    {
        // Its unannotated properties are bound from the body, which a GET is not read for, so the schema's operationId must
        // name the POST, and a request made from the schema at that slot must reach the handler with both values.
        var schema = CreateProvider().GetSchema(MultiMethodOperationName)!;

        schema.Properties.Should().Equal(
            new OperationInputProperty("name", OperationInputLocation.Body),
            new OperationInputProperty("count", OperationInputLocation.Body));

        await using var host = await KestrelTestHost.StartAsync();
        using var document = JsonDocument.Parse(await host.Client.GetStringAsync("api/.blueprint/openapi.json"));
        var slot = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(static path => path.Value.EnumerateObject().Select(method => (Path: path.Name, Method: method.Name, Operation: method.Value)))
            .Single(s => s.Operation.ValueKind == JsonValueKind.Object
                && s.Operation.TryGetProperty("operationId", out var id)
                && id.GetString() == schema.OperationId);

        slot.Method.Should().Be("post");

        using var body = new StringContent("""{"name":"echo","count":2}""", System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync("api" + slot.Path, body);

        (await response.Content.ReadAsStringAsync()).Should().Contain("echo:2");
    }

    [Fact]
    public void Listing_every_schema_fails_while_one_operation_collides()
    {
        var provider = CreateProvider();

        var act = () => provider.GetSchemas();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{CollidingOperationName}*");
    }

    [Fact]
    public void A_property_legacy_naming_documents_as_a_parameter_and_in_the_body_is_one_property()
    {
        // Under Legacy naming an inherited route property of a non-GET is in the body as well (R153): "Id" and "id".
        var schema = CreateProvider(static o => o.SchemaNaming = OpenApiSchemaNaming.Legacy).GetSchema(InheritedOperationName);

        schema.Should().NotBeNull();
        schema!.Properties.Should().Equal(
            new OperationInputProperty("Id", OperationInputLocation.Path),
            new OperationInputProperty("name", OperationInputLocation.Body));
    }

    [Fact]
    public void Legacy_body_copies_of_inherited_header_and_cookie_properties_are_not_body_inputs()
    {
        // Legacy writes an inherited header property in the body under its own name ("mode"), not the header's, and an
        // inherited cookie property there too ("session"), though the operation binds neither from the body.
        var schema = CreateProvider(static o => o.SchemaNaming = OpenApiSchemaNaming.Legacy).GetSchema(LegacySourcesOperationName);

        schema.Should().NotBeNull();
        schema!.Properties.Should().Equal(
            new OperationInputProperty("X-Input-Schema-Mode", OperationInputLocation.Header),
            new OperationInputProperty("note", OperationInputLocation.Body),
            new OperationInputProperty("other", OperationInputLocation.Body));
        PropertyNames(schema.Schema).Should().Equal("X-Input-Schema-Mode", "note", "other");
    }

    [Fact]
    public void A_legacy_body_conditional_on_a_dropped_copy_follows_the_property_or_is_dropped_with_it()
    {
        var schema = CreateProvider(static o => o.SchemaNaming = OpenApiSchemaNaming.Legacy).GetSchema(LegacySourcesOperationName)!.Schema;

        // "note" is required when the header says strict; "other" depended on the cookie, which the schema cannot carry.
        var conditional = schema.GetProperty("allOf").EnumerateArray().Should().ContainSingle().Subject;
        conditional.GetProperty("if").GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Equal("X-Input-Schema-Mode");
        conditional.GetProperty("if").GetProperty("properties").EnumerateObject().Select(static p => p.Name).Should().Equal("X-Input-Schema-Mode");
        conditional.GetProperty("then").GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Equal("note");
    }

    [Fact]
    public void An_operation_the_app_does_not_expose_has_no_schema()
    {
        CreateProvider().GetSchema("NoSuchOperation").Should().BeNull();
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_read_only_property_the_serializer_requires_is_still_required_of_an_input(OpenApiSchemaNaming naming)
    {
        // [JsonRequired] with a setter: the body reader rejects a request without it, read-only or not. Admitting null moves
        // the reference into an anyOf branch; read-only says what the whole property is, so it stays beside.
        var schema = CreateProvider(o => o.SchemaNaming = naming).GetSchema(NoteOperationName)!.Schema;
        var reference = FindReference(schema.GetProperty("properties").GetProperty("address"));
        var address = schema.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);

        address.GetProperty("properties").GetProperty("previous").GetProperty("readOnly").GetBoolean().Should().BeTrue();
        address.GetProperty("properties").GetProperty("previous").EnumerateObject().Select(static p => p.Name)
            .Should().NotContain(static name => name.StartsWith("x-", StringComparison.Ordinal));
        address.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Contain("previous");
    }

    [Fact]
    public async Task A_request_body_without_a_property_the_serializer_requires_is_refused_however_it_is_documented()
    {
        // What the schema above requires is what binding requires: the body reader refuses an address without "previous".
        await using var host = await KestrelTestHost.StartAsync();

        using var without = new StringContent("""{"text":"abc","address":{"postCode":"SW1"}}""", System.Text.Encoding.UTF8, "application/json");
        using var refused = await host.Client.PostAsync("api/operation-input-schema/acc/notes?priority=1", without);
        using var with = new StringContent("""{"text":"abc","address":{"postCode":"SW1","previous":null}}""", System.Text.Encoding.UTF8, "application/json");
        using var accepted = await host.Client.PostAsync("api/operation-input-schema/acc/notes?priority=1", with);

        ((int)refused.StatusCode).Should().BeInRange(400, 499);
        accepted.IsSuccessStatusCode.Should().BeTrue(await accepted.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(OpenApiParameterNaming.BindingName)]
    [InlineData(OpenApiParameterNaming.PropertyName)]
    public void A_query_property_is_an_input_by_the_name_the_request_is_bound_by(OpenApiParameterNaming queryNaming)
    {
        // [FromQuery("validationToken")] binds only that name, whichever name the document gives the parameter.
        var schema = CreateProvider(o => o.QueryParameterNaming = queryNaming).GetSchema("AliasedTokenOperationInputSchema")!;

        schema.Properties.Should().Equal(new OperationInputProperty("validationToken", OperationInputLocation.Query));
        PropertyNames(schema.Schema).Should().Equal("validationToken");
    }

    [Fact]
    public void The_provider_fails_until_the_api_is_mapped()
    {
        using var services = BuildServices();
        var provider = services.GetRequiredService<IOperationInputSchemaProvider>();

        var act = () => provider.GetSchema(NoteOperationName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*MapBlueprintApi*");
    }

    private const string CollidingOperationName = "CollidingOperationInputSchema";

    private const string InheritedOperationName = "InheritedOperationInputSchema";

    private const string LegacySourcesOperationName = "LegacySourcesOperationInputSchema";

    private const string DuplicateBodyNameOperationName = "DuplicateBodyNameOperationInputSchema";

    private const string MultiMethodOperationName = "MultiMethodEchoOperationInputSchema";

    private static IEnumerable<string?> ConditionallyRequired(JsonElement schema)
    {
        return (schema.TryGetProperty("allOf", out var conditionals) ? conditionals.EnumerateArray() : [])
            .SelectMany(static c => c.TryGetProperty("then", out var then) && then.TryGetProperty("required", out var required) ? required.EnumerateArray() : [])
            .Select(static e => e.GetString())
            .ToList();
    }

    private static OperationInputSchema GetSchema(string operationName)
    {
        var schema = CreateProvider().GetSchema(operationName);

        schema.Should().NotBeNull();

        return schema!;
    }

    private static IOperationInputSchemaProvider CreateProvider(
        Action<OpenApiInfoOptions>? configureOpenApi = null,
        Action<OpenApiSchemaProcessorOptions>? configureOpenApiSchemas = null,
        Action<OpenApiOperationProcessorOptions>? configureOpenApiOperations = null)
    {
        var services = BuildServices();
        var endpoints = new TestEndpointRouteBuilder(services);
        endpoints.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(
            "api/",
            configureOpenApi,
            configureOpenApiSchemas,
            configureOpenApiOperations);

        return services.GetRequiredService<IOperationInputSchemaProvider>();
    }

    private static OpenApiOperationModel RenameCaptionToLabelInBody(OpenApiOperationModel operation, ApiOperationDescriptor descriptor)
    {
        if (operation.OperationName != DuplicateBodyNameOperationName || operation.RequestBody is not { } body)
        {
            return operation;
        }

        return operation with
        {
            RequestBody = body with { Properties = [.. body.Properties.Select(static p => p.Name == "caption" ? p with { Name = "label" } : p)] },
        };
    }

    private static void RenameCaptionToLabelInComponent(OpenApiSchemaProcessorContext context)
    {
        if (context.Component.Properties.Any(static p => p.Name == "caption") && context.Component.Properties.Any(static p => p.Name == "label"))
        {
            context.Component = context.Component with
            {
                Properties = [.. context.Component.Properties.Select(static p => p.Name == "caption" ? p with { Name = "label" } : p)],
            };
        }
    }

    private static void RenamePostCodeToTotalInAddress(OpenApiSchemaProcessorContext context)
    {
        if (context.Component.Properties.Any(static p => p.Name == "postCode") && context.Component.Properties.Any(static p => p.Name == "total"))
        {
            context.Component = context.Component with
            {
                Properties = [.. context.Component.Properties.Select(static p => p.Name == "postCode" ? p with { Name = "total" } : p)],
            };
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Blueprint.Authorization.IResourceAuthorizationProvider, PermitAllResourceAuthorizationProvider>();

        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        return services.BuildServiceProvider();
    }

    private static List<string> PropertyNames(JsonElement schema)
    {
        return schema.GetProperty("properties").EnumerateObject().Select(static p => p.Name).ToList();
    }

    /// <summary>Returns the <c>$ref</c> of a schema, or of the non-null branch of a nullable one.</summary>
    private static string FindReference(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return reference.GetString()!;
        }

        return schema.GetProperty("anyOf").EnumerateArray()
            .Select(static branch => branch.TryGetProperty("$ref", out var r) ? r.GetString() : null)
            .Single(static r => r is not null)!;
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(this.ServiceProvider);
        }
    }
}

/// <summary>An address a note's body holds, which the schema must define for itself.</summary>
public sealed class OperationInputSchemaAddress
{
    [Required]
    [StringLength(8)]
    public string? PostCode { get; set; }

    /// <summary>Gets a value the server computes, which a request does not carry.</summary>
    public int Total => this.PostCode?.Length ?? 0;

    /// <summary>Gets or sets an address the server records, which a request does not carry either.</summary>
    [JsonRequired]
    [ReadOnly(true)]
    public OperationInputSchemaAddress? Previous { get; set; }

    /// <summary>
    /// Gets or sets the region of a post code, which the document marks read-only, though a request can still set it and
    /// validation still requires it.
    /// </summary>
    [RequiredIf(nameof(PostCode), "SW1A 1AA")]
    [ReadOnly(true)]
    public string? Region { get; set; }

    /// <summary>Gets the district the server finds for a post code, which a request cannot carry.</summary>
    [RequiredIf(nameof(PostCode), "SW1A 1AA")]
    public string? District => this.PostCode is null ? null : "Westminster";
}

/// <summary>A command taking a property from every source the caller sends, and two it does not.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/{AccountId}/notes")]
[HttpPost]
public partial class AddOperationInputSchemaNoteCommand : ICommand<string>
{
    [Description("The account the note is added to.")]
    [StringLength(12)]
    public string AccountId { get; set; } = string.Empty;

    [FromQuery]
    [Range(1, 10)]
    public int Priority { get; set; }

    [FromHeader("X-Correlation-Id")]
    public string? CorrelationId { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 3)]
    [Description("The text of the note.")]
    public string? Text { get; set; }

    public OperationInputSchemaAddress? Address { get; set; }

    [FromAuthContext]
    public string? TenantId { get; set; }

    [FromCookie("session")]
    public string? Session { get; set; }

    public static string Handle(AddOperationInputSchemaNoteCommand command)
    {
        return "added";
    }
}

/// <summary>A command whose query property is bound by the name its body property is written under.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/collision")]
[HttpPost]
public partial class CollidingOperationInputSchemaCommand : ICommand<string>
{
    [FromQuery("name")]
    public string? Filter { get; set; }

    public string? Name { get; set; }

    public static string Handle(CollidingOperationInputSchemaCommand command)
    {
        return "collided";
    }
}

/// <summary>A query whose query property is bound by an alias rather than its own name.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/aliased-token")]
public partial class AliasedTokenOperationInputSchemaQuery : IQuery<string>
{
    [FromQuery("validationToken")]
    public string? Token { get; set; }

    public static string Handle(AliasedTokenOperationInputSchemaQuery query)
    {
        return "validated";
    }
}

/// <summary>A query answering GET and POST, whose unannotated properties are bound from the body.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/multi-method")]
[HttpGet]
[HttpPost]
public partial class MultiMethodEchoOperationInputSchemaQuery : IQuery<string>
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public static string Handle(MultiMethodEchoOperationInputSchemaQuery query)
    {
        return $"{query.Name}:{query.Count}";
    }
}

/// <summary>A command whose two body properties a processor writes under one name.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/duplicate-body-name")]
[HttpPost]
public partial class DuplicateBodyNameOperationInputSchemaCommand : ICommand<string>
{
    public string? Label { get; set; }

    public string? Caption { get; set; }

    public static string Handle(DuplicateBodyNameOperationInputSchemaCommand command)
    {
        return "duplicated";
    }
}

/// <summary>The base of <see cref="InheritedOperationInputSchemaCommand" />, which declares its route property.</summary>
public abstract class OperationInputSchemaInheritedBase
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>A command whose route property is inherited, which legacy naming documents twice.</summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/inherited/{Id}")]
[HttpPost]
public partial class InheritedOperationInputSchemaCommand : OperationInputSchemaInheritedBase, ICommand<string>
{
    public string? Name { get; set; }

    public static string Handle(InheritedOperationInputSchemaCommand command)
    {
        return "inherited";
    }
}

/// <summary>The base of <see cref="LegacySourcesOperationInputSchemaCommand" />, which declares a header and a cookie property.</summary>
public abstract class OperationInputSchemaLegacySourcesBase
{
    [FromHeader("X-Input-Schema-Mode")]
    public string? Mode { get; set; }

    [FromCookie("inputSchemaSession")]
    public string? Session { get; set; }
}

/// <summary>
/// A command inheriting a header and a cookie property, which legacy naming also writes in the body, with body properties
/// required on each.
/// </summary>
[AllowAnonymous]
[RootLink("/operation-input-schema/legacy-sources")]
[HttpPost]
public partial class LegacySourcesOperationInputSchemaCommand : OperationInputSchemaLegacySourcesBase, ICommand<string>
{
    [RequiredIf(nameof(Mode), "strict")]
    public string? Note { get; set; }

    [RequiredIf(nameof(Session), "admin")]
    public string? Other { get; set; }

    public static string Handle(LegacySourcesOperationInputSchemaCommand command)
    {
        return "sourced";
    }
}
