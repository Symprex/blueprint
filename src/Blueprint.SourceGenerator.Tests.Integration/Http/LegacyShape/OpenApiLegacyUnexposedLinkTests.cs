using System.Text.Json;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming a resource's <c>x-links</c> keep a link to an operation the document leaves out
/// (<c>[UnexposedOperation]</c>), while Default naming still drops it.
/// </summary>
/// <remarks>
/// Legacy's <c>BlueprintLinkSchemaProcessor</c> wrote an <c>x-links</c> entry for every link of a resource with no
/// filter on whether the linked operation was exposed (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint.OpenApi/BlueprintLinkSchemaProcessor.cs:31-48</c>), and the generated client builds a stub for an
/// <c>x-links</c> operation id that has no path (<c>generator.mts:1045-1057</c>). Signature365's <c>User.DeleteUser</c>
/// and <c>Group.DeleteGroup</c> are two such links in the Portal's legacy document; without them the client has no
/// <c>canExecute</c> for the action.
/// </remarks>
public sealed class OpenApiLegacyUnexposedLinkTests
{
    [Fact]
    public void Under_legacy_naming_a_link_to_an_unexposed_operation_is_kept_in_x_links()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy);

        Links(document).Should().Equal(
            ("archive", "ArchiveUnexposedFixture"),
            ("internalRefresh", "RefreshUnexposedFixtureInternally"));
    }

    [Fact]
    public void Under_default_naming_a_link_to_an_unexposed_operation_is_left_out_of_x_links()
    {
        using var document = Render(OpenApiSchemaNaming.Default);

        Links(document).Should().Equal(("archive", "ArchiveUnexposedFixture"));
    }

    private static (string? Rel, string? OperationId)[] Links(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(UnexposedFixtureApiResource))
            .GetProperty("x-links").EnumerateArray()
            .Select(static link => (link.GetProperty("rel").GetString(), link.GetProperty("operationId").GetString()))
            .ToArray();
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}
