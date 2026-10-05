using System.Text.Json;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Http.LegacyShape;

/// <summary>
/// Proves that under Legacy naming every non-GET operation with no request body still has a schema component, named by its
/// type and described by its documentation, as legacy Blueprint wrote one, while Default naming writes none.
/// </summary>
/// <remarks>
/// Legacy registered a schema for the operation type as it generated it (<c>OpenApiDocumentBuilder.GetOrAddJsonSchema</c>,
/// <c>generator.Generate</c> appending to the document's components, Signature365 <c>8b40ecc961</c>, :347 and :601-611) and only
/// then dropped the request body for an empty object (:357-360). Signature365's 18 legacy documents carry 128 non-GET operations
/// with no <c>requestBody</c> and exactly those 128 empty <c>{"type":"object","description":…,"additionalProperties":false}</c>
/// components (Portal 55 of 55); no GET operation has one. Generated clients build a form (and its validator) from each.
/// </remarks>
public sealed class OpenApiLegacyBodylessOperationTests
{
    [Fact]
    public void Under_legacy_naming_a_non_GET_operation_with_no_body_has_an_empty_component_named_by_its_type()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType);

        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DisableLegacyBodylessWidgetCommand");

        component.GetProperty("type").GetString().Should().Be("object");
        component.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        component.GetProperty("description").GetString().Should().Be("Disables a widget,\nover two lines.");
        component.TryGetProperty("properties", out _).Should().BeFalse();
        component.TryGetProperty("required", out _).Should().BeFalse();
    }

    [Fact]
    public void The_operation_still_has_no_request_body()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType);

        document.RootElement.GetProperty("paths").GetProperty("/legacy-bodyless/{Id}/disable").GetProperty("post")
            .TryGetProperty("requestBody", out _).Should().BeFalse();
    }

    [Fact]
    public void A_DELETE_with_no_body_has_one_too()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType);

        ComponentNames(document).Should().Contain("DeleteLegacyBodylessWidgetCommand");
    }

    [Fact]
    public void A_GET_has_none()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType);

        ComponentNames(document).Should().NotContain("GetLegacyBodylessWidgetsQuery");
    }

    // R184: legacy generated the schema once per method, so an operation published as GET and POST had it from the POST.
    [Fact]
    public void An_operation_published_as_GET_and_POST_has_one_too()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType);

        ComponentNames(document).Should().Contain("TouchLegacyBodylessWidgetCommand");
    }

    [Fact]
    public void Under_legacy_naming_with_bodies_keyed_by_operation_name_the_component_is_named_by_the_operation()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationName);

        ComponentNames(document).Should().Contain("DisableLegacyBodylessWidget").And.NotContain("DisableLegacyBodylessWidgetCommand");
    }

    [Fact]
    public void The_schema_name_formatter_applies_to_the_component_as_it_does_to_any_other()
    {
        using var document = Render(
            OpenApiSchemaNaming.Legacy,
            OpenApiRequestBodyNaming.OperationType,
            static name => name.Replace("Widget", "Gadget", StringComparison.Ordinal));

        ComponentNames(document).Should().Contain("DisableLegacyBodylessGadgetCommand").And.NotContain("DisableLegacyBodylessWidgetCommand");
    }

    [Fact]
    public void Under_default_naming_there_is_no_component()
    {
        using var document = Render(OpenApiSchemaNaming.Default, OpenApiRequestBodyNaming.OperationName);

        ComponentNames(document).Should().NotContain(static name => name.StartsWith("DisableLegacyBodylessWidget", StringComparison.Ordinal)
                                                                  || name.StartsWith("DeleteLegacyBodylessWidget", StringComparison.Ordinal));
    }

    [Fact]
    public void The_component_is_reached_as_legacy_reached_it_so_a_name_clash_is_counted_from_the_body_first()
    {
        using var document = Render(
            OpenApiSchemaNaming.Legacy,
            OpenApiRequestBodyNaming.OperationType,
            static name => name is "DisableLegacyBodylessWidgetCommand" or "LegacyShapeListApiResourceOfLegacyShapeCountryApiResource" ? "LegacyBodylessClash" : name);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        // The command's empty schema is reached first, as the operation's body, then the list it responds with.
        schemas.GetProperty("LegacyBodylessClash").GetProperty("description").GetString().Should().Be("Disables a widget,\nover two lines.");
        schemas.GetProperty("LegacyBodylessClash").TryGetProperty("properties", out _).Should().BeFalse();
        schemas.GetProperty("LegacyBodylessClash2").TryGetProperty("properties", out _).Should().BeTrue();
    }

    // R182: legacy's ConfigureOperation edited the body's schema in place and then dropped the emptied body, so the component
    // it had already registered stayed, empty. Signature365's Portal strips an auto-populated TenantId this way from the 22
    // commands whose only body property it is.
    [Fact]
    public void Under_legacy_naming_a_body_an_operation_processor_empties_and_drops_leaves_its_empty_component()
    {
        using var document = Render(OpenApiSchemaNaming.Legacy, OpenApiRequestBodyNaming.OperationType, processors: RemoveTenantIdBody());

        var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("EnableLegacyBodylessWidgetCommand");

        component.GetProperty("type").GetString().Should().Be("object");
        component.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        component.GetProperty("description").GetString().Should().Be("Enables a widget.");
        component.TryGetProperty("properties", out _).Should().BeFalse();
        document.RootElement.GetProperty("paths").GetProperty("/legacy-bodyless/{Id}/enable").GetProperty("post")
            .TryGetProperty("requestBody", out _).Should().BeFalse();
    }

    [Fact]
    public void Under_default_naming_a_body_an_operation_processor_drops_leaves_no_component()
    {
        using var document = Render(OpenApiSchemaNaming.Default, OpenApiRequestBodyNaming.OperationName, processors: RemoveTenantIdBody());

        ComponentNames(document).Should().NotContain(static name => name.StartsWith("EnableLegacyBodylessWidget", StringComparison.Ordinal));
    }

    private static OpenApiOperationProcessorOptions RemoveTenantIdBody()
    {
        var options = new OpenApiOperationProcessorOptions();

        options.Processors.Add(static (operation, descriptor) =>
        {
            if (descriptor.OperationType != typeof(EnableLegacyBodylessWidgetCommand) || operation.RequestBody is not { } body)
            {
                return operation;
            }

            var properties = body.Properties.RemoveAll(static p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));

            return operation with { RequestBody = properties.IsEmpty ? null : body with { Properties = properties } };
        });

        return options;
    }

    private static JsonDocument Render(
        OpenApiSchemaNaming naming,
        OpenApiRequestBodyNaming bodyNaming,
        Func<string, string>? formatter = null,
        OpenApiOperationProcessorOptions? processors = null)
    {
        var (_, rendered) = OpenApiDocumentEndpoint.Create(
            new IntegrationTestBlueprintApp(),
            new OpenApiInfoOptions { SchemaNaming = naming, RequestBodyNaming = bodyNaming, SchemaNameFormatter = formatter },
            operationProcessorOptions: processors);

        return JsonDocument.Parse(rendered.Json);
    }

    private static IReadOnlyList<string> ComponentNames(JsonDocument document)
    {
        return document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(static p => p.Name).ToList();
    }
}

/// <summary>
/// Disables a widget,
/// over two lines.
/// </summary>
[AllowAnonymous]
[RootLink("/legacy-bodyless/{Id}/disable")]
[HttpPost]
public partial class DisableLegacyBodylessWidgetCommand : ICommand<LegacyShapeListApiResource<LegacyShapeCountryApiResource>>
{
    public string Id { get; set; } = string.Empty;

    public static LegacyShapeListApiResource<LegacyShapeCountryApiResource> Handle(DisableLegacyBodylessWidgetCommand command)
    {
        return new LegacyShapeListApiResource<LegacyShapeCountryApiResource>([]);
    }
}

/// <summary>Deletes a widget.</summary>
[AllowAnonymous]
[RootLink("/legacy-bodyless/{Id}")]
[HttpDelete]
public partial class DeleteLegacyBodylessWidgetCommand : ICommand<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(DeleteLegacyBodylessWidgetCommand command)
    {
        return command.Id;
    }
}

/// <summary>Touches a widget, by GET or POST.</summary>
[AllowAnonymous]
[RootLink("/legacy-bodyless/{Id}/touch")]
[HttpGet]
[HttpPost]
public partial class TouchLegacyBodylessWidgetCommand : ICommand<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(TouchLegacyBodylessWidgetCommand command)
    {
        return command.Id;
    }
}

/// <summary>Enables a widget.</summary>
[AllowAnonymous]
[RootLink("/legacy-bodyless/{Id}/enable")]
[HttpPost]
public partial class EnableLegacyBodylessWidgetCommand : ICommand<string>
{
    public string Id { get; set; } = string.Empty;

    public string? TenantId { get; set; }

    public static string Handle(EnableLegacyBodylessWidgetCommand command)
    {
        return command.Id;
    }
}

/// <summary>Lists the widgets.</summary>
[AllowAnonymous]
[RootLink("/legacy-bodyless")]
public partial class GetLegacyBodylessWidgetsQuery : IQuery<string>
{
    public static string Handle(GetLegacyBodylessWidgetsQuery query)
    {
        return "widgets";
    }
}
