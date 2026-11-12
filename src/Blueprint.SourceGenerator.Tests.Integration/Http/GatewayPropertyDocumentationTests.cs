using System.Text.Json;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Upstream;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// A property <c>[Expose&lt;TInner&gt;]</c> contributes to a gateway operation is described by the inner property's
/// documentation, exactly as if the gateway had declared it with the same XML doc (Symprex/blueprint#174). The inner
/// operations are in another assembly, <c>Blueprint.SourceGenerator.Tests.Integration.Upstream</c>, so the generated
/// <c>&lt;inheritdoc cref&gt;</c> names a member documented only in that assembly's documentation file, not the gateway's.
/// </summary>
/// <remarks>
/// Each expected description is the inner property's XML <c>summary</c>, formatted as legacy formatted one the gateway
/// declared itself: a parameter's text escaped, a body property's not (<see cref="OpenApiXmlDocTests"/>). The inner
/// properties have no <c>[Description]</c>, which is described ahead of any documentation.
/// </remarks>
public sealed class GatewayPropertyDocumentationTests
{
    private const string CommandPath = "/gateway-property-docs/notes/{NoteId}";
    private const string QueryPath = "/gateway-property-docs/notes";

    [Fact]
    public void A_contributed_route_parameter_is_described_by_the_inner_propertys_summary()
    {
        using var document = Render();

        ParameterDescription(document, CommandPath, "put", "NoteId").Should().Be("The note to rename, from the route.");
    }

    [Fact]
    public void A_contributed_query_parameter_is_described_by_the_inner_propertys_summary()
    {
        using var document = Render();

        ParameterDescription(document, QueryPath, "get", "TitleContains").Should().Be("Only notes whose title contains this text.");
    }

    [Fact]
    public void A_contributed_body_property_is_described_by_the_inner_propertys_summary()
    {
        using var document = Render();

        BodyPropertyDescription(document, CommandPath, "put", "title").Should().Be("The note's new title, over\ntwo lines, Tom & Jerry.");
    }

    [Theory]
    [InlineData("colour", "The note's colour, documented on the base.")]
    [InlineData("pinned", "Whether the note is pinned, documented on the interface.")]
    public void A_contributed_property_whose_inner_property_inherits_its_documentation_is_described_by_what_it_inherits(string property, string expected)
    {
        // The inner property is documented only by a cref-less <inheritdoc/>, on an override or an interface
        // implementation: what it inherits is resolved from the inner property's own base type and interfaces, in the
        // upstream assembly, not from the gateway operation's.
        using var document = Render();

        BodyPropertyDescription(document, CommandPath, "put", property).Should().Be(expected);
    }

    [Fact]
    public void A_contributed_property_reads_the_inner_assemblys_documentation_file_through_its_inheritdoc_cref()
    {
        // No base type or interface of the gateway operation declares the property, so only the cref names where its
        // documentation is: the upstream assembly's file, not the gateway's.
        var property = typeof(RenameGatewayNoteCommand).GetProperty(nameof(RenameUpstreamNoteCommand.Title))!;

        OpenApiXmlDocs.GetSchemaSummary(property).Should().Be("The note's new title, over\ntwo lines, Tom & Jerry.");
        OpenApiXmlDocs.GetParameterSummary(property).Should().Be("The note's new title, over\ntwo lines, Tom &amp; Jerry.");
    }

    private static string? ParameterDescription(JsonDocument document, string path, string method, string name)
    {
        var parameter = Operation(document, path, method).GetProperty("parameters").EnumerateArray()
            .Single(p => string.Equals(p.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase));

        return Description(parameter);
    }

    private static string? BodyPropertyDescription(JsonDocument document, string path, string method, string name)
    {
        var schema = Resolve(document, Operation(document, path, method)
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));

        var property = schema.GetProperty("properties").EnumerateObject()
            .Single(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        return Description(property);
    }

    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/')[^1];

        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static string? Description(JsonElement element)
    {
        return element.TryGetProperty("description", out var description) ? description.GetString() : null;
    }

    private static JsonElement Operation(JsonDocument document, string path, string method)
    {
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
    }

    private static JsonDocument Render()
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(new IntegrationTestBlueprintApp(), new OpenApiInfoOptions());

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>The upstream note service the gateway operations below forward to.</summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(RenameUpstreamNoteCommand))]
[BlueprintClientInclude(typeof(FindUpstreamNotesQuery))]
internal partial class GatewayNoteApi;

/// <summary>Renames a note upstream.</summary>
[Expose<RenameUpstreamNoteCommand>]
[AllowAnonymous]
[RootLink("/gateway-property-docs/notes/{NoteId}")]
[HttpPut]
public partial class RenameGatewayNoteCommand : ICommand<UpstreamNoteApiResource>;

/// <summary>Finds notes upstream.</summary>
[Expose<FindUpstreamNotesQuery>]
[AllowAnonymous]
[RootLink("/gateway-property-docs/notes")]
public partial class FindGatewayNotesQuery : IQuery<UpstreamNoteApiResource>;
