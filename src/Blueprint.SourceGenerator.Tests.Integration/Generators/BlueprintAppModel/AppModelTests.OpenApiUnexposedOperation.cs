using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

/// <summary>
/// Proves that an operation marked <see cref="UnexposedOperationAttribute"/> is left out of the rendered OpenAPI document —
/// its path, its operation and any <c>x-links</c> entry naming it — as legacy Blueprint left out every descriptor whose
/// <see cref="ApiOperationDescriptor.IsExposed"/> was false, while an exposed operation on the same resource still renders.
/// </summary>
/// <remarks>
/// The attribute hides an operation from documentation and link generation, not from routing: Signature365's
/// service-to-service and management endpoints (survey responses, the Hangfire message operations) are called over HTTP
/// but must not appear in the published document or the clients generated from it.
/// </remarks>
public class AppModelOpenApiUnexposedOperationTests
{
    private const string ExposedPath = "/unexposed-fixtures/{Id}";
    private const string UnexposedPath = "/unexposed-fixtures/{Id}/internal-refresh";

    [Fact]
    public void When_openapi_document_is_rendered_then_unexposed_operation_has_no_path()
    {
        var paths = RenderDocument().RootElement.GetProperty("paths");

        paths.TryGetProperty(ExposedPath, out _).Should().BeTrue("the exposed query on the same resource is the control");
        paths.TryGetProperty(UnexposedPath, out _).Should().BeFalse("[UnexposedOperation] keeps an operation out of the document");
    }

    [Fact]
    public void When_openapi_document_is_rendered_then_no_operation_id_names_the_unexposed_operation()
    {
        var operationIds = RenderDocument().RootElement.GetProperty("paths")
            .EnumerateObject()
            .SelectMany(static path => path.Value.EnumerateObject())
            .Where(static method => method.Value.ValueKind == JsonValueKind.Object && method.Value.TryGetProperty("operationId", out _))
            .Select(static method => method.Value.GetProperty("operationId").GetString())
            .ToArray();

        operationIds.Should().Contain("GetUnexposedFixture").And.NotContain("RefreshUnexposedFixtureInternally");
    }

    [Fact]
    public void When_openapi_document_is_rendered_then_resource_x_links_omit_the_unexposed_operation()
    {
        var schema = RenderDocument().RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(nameof(UnexposedFixtureApiResource));

        var rels = schema.TryGetProperty("x-links", out var links)
            ? links.EnumerateArray().Select(static link => link.GetProperty("rel").GetString()).ToArray()
            : [];

        rels.Should().Equal(["archive"], "the exposed archive link stays, and a link to an operation the document omits must not dangle");
    }

    [Fact]
    public void When_app_is_built_then_the_unexposed_operation_is_still_registered_with_its_url()
    {
        // Hidden from documentation, not from dispatch: the route must still be mapped.
        var descriptor = new IntegrationTestBlueprintApp().FindOperation(typeof(RefreshUnexposedFixtureInternallyCommand));

        descriptor.IsExposed.Should().BeFalse();
        descriptor.Urls.Select(static url => url.UrlFormat).Should().Contain(UnexposedPath);
    }

    private static JsonDocument RenderDocument()
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(new IntegrationTestBlueprintApp());
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return JsonDocument.Parse(OpenApiOutputRenderingContext.Create(documentContext).Json);
    }
}

// --- Fixtures ---

/// <summary>A resource with one exposed and one unexposed action link.</summary>
public partial class UnexposedFixtureApiResource : ApiResource
{
    public string Id { get; set; } = null!;
}

/// <summary>The exposed control: a self-linked query on the same resource.</summary>
[AllowAnonymous]
[SelfLink(typeof(UnexposedFixtureApiResource), ExposedRoute)]
public partial class GetUnexposedFixtureQuery : IQuery<UnexposedFixtureApiResource>
{
    private const string ExposedRoute = "/unexposed-fixtures/{Id}";

    public string Id { get; set; } = "fixture-1";

    public static Task<UnexposedFixtureApiResource> HandleAsync(GetUnexposedFixtureQuery query)
    {
        return Task.FromResult(new UnexposedFixtureApiResource { Id = query.Id });
    }
}

/// <summary>An exposed action link, so the resource carries <c>x-links</c> either way.</summary>
[AllowAnonymous]
[Link(typeof(UnexposedFixtureApiResource), "/unexposed-fixtures/{Id}/archive", "archive")]
[HttpPost]
public partial class ArchiveUnexposedFixtureCommand : ICommand<UnexposedFixtureApiResource>
{
    public string Id { get; set; } = null!;

    public static Task<UnexposedFixtureApiResource> HandleAsync(ArchiveUnexposedFixtureCommand command)
    {
        return Task.FromResult(new UnexposedFixtureApiResource { Id = command.Id });
    }
}

/// <summary>Routed over HTTP for internal callers, but not part of the published API.</summary>
[AllowAnonymous]
[UnexposedOperation]
[Link(typeof(UnexposedFixtureApiResource), "/unexposed-fixtures/{Id}/internal-refresh", "internalRefresh")]
[HttpPost]
public partial class RefreshUnexposedFixtureInternallyCommand : ICommand<UnexposedFixtureApiResource>
{
    public string Id { get; set; } = null!;

    public static Task<UnexposedFixtureApiResource> HandleAsync(RefreshUnexposedFixtureInternallyCommand command)
    {
        return Task.FromResult(new UnexposedFixtureApiResource { Id = command.Id });
    }
}
