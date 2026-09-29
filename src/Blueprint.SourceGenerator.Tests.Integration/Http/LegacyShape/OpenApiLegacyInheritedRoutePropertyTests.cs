using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a route-owned property an operation inherits from a base class is documented in its
/// request body as well as its path, as legacy Blueprint's document had it, while Default naming keeps every owned
/// property out of the body.
/// </summary>
/// <remarks>
/// <para>
/// Legacy took an owned property out of a body schema in its contract resolver with
/// <c>allOwned.All(o => o.Property != p)</c> (Signature365 <c>8b40ecc961</c>, <c>OpenApiDocumentBuilder.cs:461-463</c>).
/// Newtonsoft's <c>ReflectionUtils.GetProperties</c> replaces a property whose declaring type is not the type being
/// serialized with the one it gets from the declaring type, so an inherited property was a different
/// <c>PropertyInfo</c> from the one in the operation's own list and was never excluded. An owned property declared on the
/// operation type itself was.
/// </para>
/// <para>
/// Signature365's Admin Portal has three commands that derive from a Portal command declaring <c>[Required] ApiId TenantId</c>
/// and <c>Id</c>: <c>AdminPortalStopLoggingSessionCommand</c>, <c>AdminPortalExtendLoggingSessionCommand</c> and
/// <c>AdminPortalUpdateInvoiceDetailsCommand</c>. Their legacy bodies carry <c>tenantId</c> and <c>id</c> (each with
/// <c>minLength: 1</c>), and the Admin Portal's client calls <c>stopLoggingSession(id, sessionId, {} as any)</c> with a
/// body argument as a result. A GET has no body, whatever it inherits.
/// </para>
/// </remarks>
public sealed class OpenApiLegacyInheritedRoutePropertyTests
{
    [Fact]
    public void Under_legacy_naming_an_inherited_route_property_is_in_the_request_body_and_the_path()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        BodyProperties(document).Should().BeEquivalentTo("reason", "tenantId", "id", "headerValue", "cookieValue", "queryValue");
        ParameterNames(document).Should().BeEquivalentTo("OwnId", "TenantId", "Id", "X-Legacy-Inherited", "legacyInherited", "queryValue");
    }

    [Fact]
    public void Under_legacy_naming_an_inherited_required_string_is_required_and_not_empty_in_the_body()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);
        var body = Component(document, "StopLegacyInheritedRoute");

        body.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Contain("tenantId");
        body.GetProperty("properties").GetProperty("tenantId").GetProperty("minLength").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Under_legacy_naming_a_route_property_the_operation_declares_itself_stays_out_of_the_body()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        BodyProperties(document).Should().NotContain("ownId");
    }

    [Fact]
    public void Under_legacy_naming_a_get_has_no_body_whatever_it_inherits()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        var get = document.RootElement.GetProperty("paths").GetProperty("/legacy-inherited/{TenantId}/items/{Id}").GetProperty("get");

        get.TryGetProperty("requestBody", out _).Should().BeFalse();
        get.GetProperty("parameters").EnumerateArray().Select(static p => p.GetProperty("name").GetString())
            .Should().Equal("TenantId", "Id", "X-Legacy-Inherited", "legacyInherited", "queryValue");
    }

    [Fact]
    public void Under_default_naming_an_inherited_route_property_stays_out_of_the_body()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        BodyProperties(document).Should().Equal("reason");
    }

    private static string?[] BodyProperties(JsonDocument document)
    {
        return Component(document, "StopLegacyInheritedRoute").GetProperty("properties").EnumerateObject().Select(static p => p.Name).ToArray();
    }

    private static string?[] ParameterNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("paths").GetProperty("/legacy-inherited/{TenantId}/items/{Id}/{OwnId}/stop").GetProperty("post")
            .GetProperty("parameters").EnumerateArray().Select(static p => p.GetProperty("name").GetString()).ToArray();
    }

    private static JsonElement Component(JsonDocument document, string name)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>The properties a base contract command declares, as the Portal's <c>StopLoggingSessionCommand</c> does.</summary>
public abstract class LegacyInheritedRouteBase
{
    [Required]
    public string TenantId { get; set; } = null!;

    public string Id { get; set; } = null!;

    [FromHeader("X-Legacy-Inherited")]
    public string? HeaderValue { get; set; }

    [FromCookie("legacyInherited")]
    public string? CookieValue { get; set; }

    [FromQuery]
    public string? QueryValue { get; set; }
}

/// <summary>Derives from a base declaring route properties, declares one of its own and has a body property.</summary>
[AllowAnonymous]
[RootLink("/legacy-inherited/{TenantId}/items/{Id}/{OwnId}/stop")]
[HttpPost]
public partial class StopLegacyInheritedRouteCommand : LegacyInheritedRouteBase, ICommand<string>
{
    public string OwnId { get; set; } = null!;

    public string? Reason { get; set; }

    public static string Handle(StopLegacyInheritedRouteCommand command)
    {
        return command.Id;
    }
}

/// <summary>A GET deriving from the same base.</summary>
[AllowAnonymous]
[RootLink("/legacy-inherited/{TenantId}/items/{Id}")]
public partial class GetLegacyInheritedRouteQuery : LegacyInheritedRouteBase, IQuery<string>
{
    public static string Handle(GetLegacyInheritedRouteQuery query)
    {
        return query.Id;
    }
}
