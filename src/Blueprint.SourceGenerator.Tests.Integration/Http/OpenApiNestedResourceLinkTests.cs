using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that a resource type reached only through another resource's list or object property still carries
/// <c>x-links</c> for the action links declared against it, as it did when it was returned directly.
/// </summary>
/// <remarks>
/// Legacy's <c>BlueprintLinkSchemaProcessor</c> ran over every schema of the document whatever reached it (Signature365
/// <c>8b40ecc961</c>, <c>Blueprint.OpenApi/BlueprintLinkSchemaProcessor.cs:31-48</c>). Signature365's <c>Product</c>
/// (<c>calculate-prices</c>), <c>MessageQueue</c> and <c>ImportSource</c> are reached only as list items or nested
/// properties, and the generated client builds a resource's <c>canExecute</c> actions from its <c>x-links</c>.
/// </remarks>
public sealed class OpenApiNestedResourceLinkTests
{
    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_resource_reached_only_as_list_items_has_its_x_links(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Links(document, "NestedLinkListedItemApiResource").Should().Equal(("archive", "ArchiveNestedLinkListedItem", "POST"));
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_resource_reached_only_as_a_nested_property_has_its_x_links(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Links(document, "NestedLinkChildApiResource").Should().Equal(("close", "CloseNestedLinkChild", "POST"));
    }

    [Theory]
    [InlineData(OpenApiSchemaNaming.Default)]
    [InlineData(OpenApiSchemaNaming.Legacy)]
    public void A_resource_that_is_a_nested_type_has_its_x_links(OpenApiSchemaNaming naming)
    {
        using var document = Render(naming);

        Links(document, "NestedLinkInnerApiResource").Should().Equal(("reopen", "ReopenNestedLinkInner", "POST"));
    }

    private static (string? Rel, string? OperationId, string? Method)[] Links(JsonDocument document, string schemaName)
    {
        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schemaName);

        return schema.TryGetProperty("x-links", out var links)
            ? links.EnumerateArray()
                .Select(static link => (link.GetProperty("rel").GetString(), link.GetProperty("operationId").GetString(), link.GetProperty("method").GetString()))
                .ToArray()
            : [];
    }

    private static JsonDocument Render(OpenApiSchemaNaming naming)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming });

        return JsonDocument.Parse(rendered.Json);
    }
}

/// <summary>A resource only ever a holder's list item.</summary>
public partial class NestedLinkListedItemApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}

/// <summary>A resource only ever a holder's nested property.</summary>
public partial class NestedLinkChildApiResource : ApiResource
{
    public string Id { get; init; } = null!;
}

/// <summary>Holds a resource that is itself a nested type, whose full name joins it to its container with a plus.</summary>
public static class NestedLinkContainer
{
    /// <summary>
    /// A resource only ever a holder's property, and a nested type. The resource generator writes only top-level types,
    /// so its <c>Object</c> is written by hand.
    /// </summary>
    public sealed class NestedLinkInnerApiResource : ApiResource
    {
        public override string Object => "nested-link-inner";

        public string Id { get; init; } = null!;
    }
}

/// <summary>The one resource an operation returns, which holds the others.</summary>
public partial class NestedLinkHolderApiResource : ApiResource
{
    public List<NestedLinkListedItemApiResource> Items { get; init; } = [];

    public NestedLinkChildApiResource? Child { get; init; }

    public NestedLinkContainer.NestedLinkInnerApiResource? Inner { get; init; }
}

/// <summary>Returns the holder.</summary>
[AllowAnonymous]
[RootLink("/nested-link-holders")]
public partial class GetNestedLinkHoldersQuery : IQuery<NestedLinkHolderApiResource>
{
    public static NestedLinkHolderApiResource Handle(GetNestedLinkHoldersQuery query)
    {
        return new NestedLinkHolderApiResource();
    }
}

/// <summary>An action on a resource only ever a list item.</summary>
[AllowAnonymous]
[Link(typeof(NestedLinkListedItemApiResource), "/nested-link-holders/items/{Id}/archive", "archive")]
[HttpPost]
public partial class ArchiveNestedLinkListedItemCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(ArchiveNestedLinkListedItemCommand command)
    {
        return command.Id;
    }
}

/// <summary>An action on a resource only ever a nested property.</summary>
[AllowAnonymous]
[Link(typeof(NestedLinkChildApiResource), "/nested-link-holders/child/{Id}/close", "close")]
[HttpPost]
public partial class CloseNestedLinkChildCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(CloseNestedLinkChildCommand command)
    {
        return command.Id;
    }
}

/// <summary>An action on a resource that is a nested type.</summary>
[AllowAnonymous]
[Link(typeof(NestedLinkContainer.NestedLinkInnerApiResource), "/nested-link-holders/inner/{Id}/reopen", "reopen")]
[HttpPost]
public partial class ReopenNestedLinkInnerCommand : ICommand<string>
{
    public string Id { get; set; } = null!;

    public static string Handle(ReopenNestedLinkInnerCommand command)
    {
        return command.Id;
    }
}