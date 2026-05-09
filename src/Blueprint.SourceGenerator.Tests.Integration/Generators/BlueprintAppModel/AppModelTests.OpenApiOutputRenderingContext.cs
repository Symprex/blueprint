using System.Collections.Immutable;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Http;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.BlueprintAppModel;

public class AppModelOpenApiOutputRenderingContextTests
{
    [Fact]
    public void When_generated_http_operation_is_rendered_then_output_contains_valid_openapi_document()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());

        context.Json.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.TryGetProperty("info", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("paths", out _).Should().BeTrue();
    }

    [Fact]
    public void When_generated_http_operation_is_rendered_then_paths_section_contains_expected_route_and_method()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("paths").TryGetProperty("/openapi/http-response-metadata", out var pathItem).Should().BeTrue();

        pathItem.TryGetProperty("get", out var operation).Should().BeTrue();
        operation.GetProperty("operationId").GetString().Should().Be("HttpResponseMetadataQueryOp");
    }

    [Fact]
    public void When_generated_http_operation_is_rendered_then_operation_responses_contain_expected_status_codes()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var responses = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/http-response-metadata")
            .GetProperty("get")
            .GetProperty("responses");

        responses.TryGetProperty("200", out _).Should().BeTrue();
        responses.TryGetProperty("422", out _).Should().BeTrue();
        responses.TryGetProperty("500", out _).Should().BeTrue();
    }

    [Fact]
    public void When_generated_app_is_rendered_then_components_section_contains_covered_schema_types()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .EnumerateObject()
            .Select(static p => p.Name)
            .Should()
            .Contain(
                "Microsoft_AspNetCore_Mvc_ProblemDetails",
                "Microsoft_AspNetCore_Mvc_ValidationProblemDetails",
                "string");
    }

    [Fact]
    public void When_response_has_covered_body_type_then_response_content_uses_schema_ref()
    {
        var context = CreateOutputContext(new IntegrationTestBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var response200 = doc.RootElement
            .GetProperty("paths")
            .GetProperty("/openapi/http-response-metadata")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200");

        response200.TryGetProperty("content", out var content).Should().BeTrue();
        content.TryGetProperty("application/json", out var json).Should().BeTrue();

        var refValue = json.GetProperty("schema").GetProperty("$ref").GetString();
        refValue.Should().NotContain(".", "schema component $ref keys must use underscores instead of dots for tooling compatibility");
        refValue.Should().Be("#/components/schemas/string");
    }

    [Fact]
    public void When_document_has_no_paths_then_output_still_renders_valid_openapi_document()
    {
        var context = CreateSyntheticOutputContext([], []);

        context.Json.Should().NotBeNullOrEmpty();
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        doc.RootElement.GetProperty("paths").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void When_document_has_schema_components_then_components_are_emitted_with_correct_type()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    ["RequiredProp"],
                    [new OpenApiSchemaComponentPropertyModel("RequiredProp", "string", false, false)]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var schema = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto");

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Equal("RequiredProp");
        schema.GetProperty("properties").GetProperty("RequiredProp").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void When_schema_component_property_is_nullable_then_output_emits_nullable_true()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel("NullableProp", "string", false, true),
                        new OpenApiSchemaComponentPropertyModel("NonNullableProp", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        properties.GetProperty("NullableProp").TryGetProperty("nullable", out var nullableVal).Should().BeTrue(
            "nullable body properties should emit nullable: true in schema components");
        nullableVal.GetBoolean().Should().BeTrue();

        properties.GetProperty("NonNullableProp").TryGetProperty("nullable", out _).Should().BeFalse(
            "non-nullable body properties should not emit nullable in schema components");
    }

    [Fact]
    public void When_schema_component_property_has_format_then_output_emits_format()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel("Count", "integer", false, false, "int32"),
                        new OpenApiSchemaComponentPropertyModel("Created", "string", false, false, "date-time"),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        properties.GetProperty("Count").GetProperty("format").GetString().Should().Be("int32");
        properties.GetProperty("Created").GetProperty("format").GetString().Should().Be("date-time");
        properties.GetProperty("Name").TryGetProperty("format", out _).Should().BeFalse(
            "properties without a CLR type format mapping should not emit format");
    }

    [Fact]
    public void When_schema_component_property_has_enum_values_then_output_emits_enum_array()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel(
                            "Status",
                            "string",
                            false,
                            false,
                            EnumValues: ["Active", "Inactive", "Pending"]),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        var statusEnum = properties.GetProperty("Status").GetProperty("enum");
        statusEnum.GetArrayLength().Should().Be(3);
        statusEnum[0].GetString().Should().Be("Active");
        statusEnum[1].GetString().Should().Be("Inactive");
        statusEnum[2].GetString().Should().Be("Pending");

        properties.GetProperty("Name").TryGetProperty("enum", out _).Should().BeFalse(
            "non-enum properties should not emit enum array");
    }

    [Fact]
    public void When_schema_component_property_has_description_then_output_emits_description()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel(
                            "Code",
                            "string",
                            false,
                            false,
                            Description: "The unique code identifying the resource."),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        properties.GetProperty("Code").GetProperty("description").GetString()
            .Should().Be("The unique code identifying the resource.");

        properties.GetProperty("Name").TryGetProperty("description", out _).Should().BeFalse(
            "properties without a [Description] attribute should not emit description");
    }

    [Fact]
    public void When_schema_processors_are_configured_then_they_transform_components_in_registration_order()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    ["RequiredProp"],
                    [new OpenApiSchemaComponentPropertyModel("RequiredProp", "string", false, false)]),
            ],
            options =>
            {
                options.Processors.Add(static processorContext =>
                    processorContext.Component = processorContext.Component with
                    {
                        Type = "number",
                    });

                options.Processors.Add(static processorContext =>
                    processorContext.Component = processorContext.Component with
                    {
                        Type = processorContext.Component.Type?.ToUpperInvariant(),
                    });
            });

        var doc = JsonDocument.Parse(context.Json);

        var schema = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto");

        schema.GetProperty("type").GetString().Should().Be("NUMBER");
    }

    [Fact]
    public void When_schema_component_properties_include_validation_constraints_then_output_emits_openapi_constraint_keywords()
    {
        var context = CreateOutputContext(new ValidationConstraintBlueprintApp());
        var doc = JsonDocument.Parse(context.Json);

        var schema = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(typeof(SchemaValidationConstraintsResponse).FullName!.Replace('.', '_'));

        var code = schema.GetProperty("properties").GetProperty("Code");
        code.GetProperty("minLength").GetInt32().Should().Be(4);
        code.GetProperty("maxLength").GetInt32().Should().Be(12);
        code.GetProperty("pattern").GetString().Should().Be("^[A-Z0-9]+$");

        var positiveCount = schema.GetProperty("properties").GetProperty("PositiveCount");
        positiveCount.GetProperty("minimum").GetDecimal().Should().Be(0m);
        positiveCount.GetProperty("exclusiveMinimum").GetBoolean().Should().BeTrue();

        var quantity = schema.GetProperty("properties").GetProperty("Quantity");
        quantity.GetProperty("minimum").GetDecimal().Should().Be(1m);
        quantity.GetProperty("maximum").GetDecimal().Should().Be(10m);
    }

    private static OpenApiOutputRenderingContext CreateOutputContext(BlueprintApp app)
    {
        var documentBuilderContext = OpenApiDocumentBuilderContext.Create(app);
        var schemaProjectionContext = OpenApiSchemaProjectionContext.Create(documentBuilderContext);
        var responseSchemaProjectionContext = OpenApiResponseSchemaReferenceProjectionContext.Create(schemaProjectionContext);
        var responseObjectContext = OpenApiResponseObjectRenderingContext.Create(responseSchemaProjectionContext);
        var operationContext = OpenApiOperationRenderingContext.Create(responseObjectContext);
        var pathItemContext = OpenApiPathItemRenderingContext.Create(operationContext);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        return OpenApiOutputRenderingContext.Create(documentContext);
    }

    private static OpenApiOutputRenderingContext CreateSyntheticOutputContext(
        OpenApiPathItemModel[] pathItems,
        OpenApiSchemaComponentModel[] schemaComponents,
        Action<OpenApiSchemaProcessorOptions>? configureSchemaProcessors = null)
    {
        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, ImmutableArray<OpenApiSchemaModel>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, [.. schemaComponents]);
        var responseSchemaProjectionContext = new OpenApiResponseSchemaReferenceProjectionContext(
            schemaProjectionContext,
            ImmutableArray<OpenApiResponseSchemaReferenceOperationContext>.Empty);
        var responseObjectContext = new OpenApiResponseObjectRenderingContext(
            responseSchemaProjectionContext,
            schemaComponentContext,
            ImmutableArray<OpenApiResponseObjectOperationContext>.Empty);
        var operationContext = new OpenApiOperationRenderingContext(
            responseObjectContext,
            ImmutableArray<OpenApiOperationModel>.Empty);
        var pathItemContext = new OpenApiPathItemRenderingContext(operationContext, [.. pathItems]);
        var documentContext = OpenApiDocumentRenderingContext.Create(pathItemContext);

        OpenApiSchemaProcessorOptions? schemaProcessorOptions = null;
        if (configureSchemaProcessors is not null)
        {
            schemaProcessorOptions = new OpenApiSchemaProcessorOptions();
            configureSchemaProcessors(schemaProcessorOptions);
        }

        return OpenApiOutputRenderingContext.Create(documentContext, schemaProcessorOptions: schemaProcessorOptions);
    }
}
