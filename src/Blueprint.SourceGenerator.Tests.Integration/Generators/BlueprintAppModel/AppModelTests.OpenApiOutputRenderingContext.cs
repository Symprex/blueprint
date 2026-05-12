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
                "ProblemDetails",
                "ValidationProblemDetails",
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

    [Theory]
    [InlineData("3.0.0")]
    [InlineData("3.0.4")]
    [InlineData("3.1.0")]
    public void When_openapi_version_is_configured_then_output_emits_that_version(string version)
    {
        var context = CreateSyntheticOutputContextWithInfoOptions([], new OpenApiInfoOptions { OpenApiVersion = version });
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().Be(version);
    }

    [Fact]
    public void When_openapi_version_is_not_configured_then_output_emits_default_version()
    {
        var context = CreateSyntheticOutputContext([], []);
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement.GetProperty("openapi").GetString().Should().Be("3.0.4");
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

        schema.GetProperty("title").GetString().Should().Be("ResponseDto", "schema title should be the short CLR type name without namespace");
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("required").EnumerateArray().Select(static e => e.GetString()).Should().Equal("RequiredProp");
        schema.GetProperty("properties").GetProperty("RequiredProp").GetProperty("type").GetString().Should().Be("string");
    }

    [Fact]
    public void When_schema_component_type_has_no_namespace_then_title_equals_full_name()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "SimpleDto",
                    "object",
                    [],
                    []),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var schema = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("SimpleDto");

        schema.GetProperty("title").GetString().Should().Be("SimpleDto");
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

        // x-enum-varnames should mirror enum names for TypeScript client generation
        var varnames = properties.GetProperty("Status").GetProperty("x-enum-varnames");
        varnames.GetArrayLength().Should().Be(3);
        varnames[0].GetString().Should().Be("Active");
        varnames[1].GetString().Should().Be("Inactive");
        varnames[2].GetString().Should().Be("Pending");

        properties.GetProperty("Name").TryGetProperty("enum", out _).Should().BeFalse(
            "non-enum properties should not emit enum array");
        properties.GetProperty("Name").TryGetProperty("x-enum-varnames", out _).Should().BeFalse(
            "non-enum properties should not emit x-enum-varnames");
    }

    [Fact]
    public void When_schema_component_has_description_then_output_emits_description()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [],
                    Description: "Represents a sample response object."),
                new OpenApiSchemaComponentModel(
                    "Sample.OtherDto",
                    "object",
                    [],
                    []),
            ]);

        var doc = JsonDocument.Parse(context.Json);
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        schemas.GetProperty("Sample_ResponseDto").GetProperty("description").GetString()
            .Should().Be("Represents a sample response object.");

        schemas.GetProperty("Sample_OtherDto").TryGetProperty("description", out _).Should().BeFalse(
            "components without a [Description] attribute should not emit description");
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
    public void When_schema_component_property_is_array_then_output_emits_items_schema()
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
                            "Tags",
                            "array",
                            false,
                            false,
                            ItemsType: "string"),
                        new OpenApiSchemaComponentPropertyModel(
                            "Counts",
                            "array",
                            false,
                            false,
                            ItemsType: "integer",
                            ItemsFormat: "int32"),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        var tagsItems = properties.GetProperty("Tags").GetProperty("items");
        tagsItems.GetProperty("type").GetString().Should().Be("string");
        tagsItems.TryGetProperty("format", out _).Should().BeFalse();

        var countsItems = properties.GetProperty("Counts").GetProperty("items");
        countsItems.GetProperty("type").GetString().Should().Be("integer");
        countsItems.GetProperty("format").GetString().Should().Be("int32");

        properties.GetProperty("Name").TryGetProperty("items", out _).Should().BeFalse(
            "non-array properties should not emit items schema");
    }

    [Fact]
    public void When_schema_component_property_is_dictionary_then_output_emits_additional_properties_schema()
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
                            "Metadata",
                            "object",
                            false,
                            false,
                            AdditionalPropertiesType: "string"),
                        new OpenApiSchemaComponentPropertyModel(
                            "Scores",
                            "object",
                            false,
                            false,
                            AdditionalPropertiesType: "number",
                            AdditionalPropertiesFormat: "double"),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        var metaAdditional = properties.GetProperty("Metadata").GetProperty("additionalProperties");
        metaAdditional.GetProperty("type").GetString().Should().Be("string");
        metaAdditional.TryGetProperty("format", out _).Should().BeFalse();

        var scoresAdditional = properties.GetProperty("Scores").GetProperty("additionalProperties");
        scoresAdditional.GetProperty("type").GetString().Should().Be("number");
        scoresAdditional.GetProperty("format").GetString().Should().Be("double");

        properties.GetProperty("Name").TryGetProperty("additionalProperties", out _).Should().BeFalse(
            "non-dictionary properties should not emit additionalProperties schema");
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
            .GetProperty(nameof(SchemaValidationConstraintsResponse));

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

        var tags = schema.GetProperty("properties").GetProperty("Tags");
        tags.GetProperty("minItems").GetInt32().Should().Be(1, "array [MinLength] should map to minItems");
        tags.GetProperty("maxItems").GetInt32().Should().Be(20, "array [MaxLength] should map to maxItems");
        tags.TryGetProperty("minLength", out _).Should().BeFalse("array properties must not emit minLength");
    }

    [Fact]
    public void When_schema_component_property_is_readOnly_then_output_emits_readOnly_true()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel("ComputedValue", "string", false, false, IsReadOnly: true),
                        new OpenApiSchemaComponentPropertyModel("Name", "string", false, false),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        properties.GetProperty("ComputedValue").TryGetProperty("readOnly", out var readOnlyVal).Should().BeTrue(
            "read-only properties should emit readOnly: true in schema components");
        readOnlyVal.GetBoolean().Should().BeTrue();

        properties.GetProperty("Name").TryGetProperty("readOnly", out _).Should().BeFalse(
            "writable properties should not emit readOnly in schema components");
    }

    [Fact]
    public void When_schema_component_array_property_has_length_constraints_then_output_emits_minItems_and_maxItems()
    {
        var context = CreateSyntheticOutputContext(
            [],
            [
                new OpenApiSchemaComponentModel(
                    "Sample.ResponseDto",
                    "object",
                    [],
                    [
                        new OpenApiSchemaComponentPropertyModel("Tags", "array", false, false, MinLength: 1, MaxLength: 10),
                        new OpenApiSchemaComponentPropertyModel("Code", "string", false, false, MinLength: 4, MaxLength: 12),
                    ]),
            ]);

        var doc = JsonDocument.Parse(context.Json);

        var properties = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Sample_ResponseDto")
            .GetProperty("properties");

        var tags = properties.GetProperty("Tags");
        tags.GetProperty("minItems").GetInt32().Should().Be(1, "array properties should use minItems, not minLength");
        tags.GetProperty("maxItems").GetInt32().Should().Be(10, "array properties should use maxItems, not maxLength");
        tags.TryGetProperty("minLength", out _).Should().BeFalse("array properties should not emit minLength");
        tags.TryGetProperty("maxLength", out _).Should().BeFalse("array properties should not emit maxLength");

        var code = properties.GetProperty("Code");
        code.GetProperty("minLength").GetInt32().Should().Be(4, "string properties should still use minLength");
        code.GetProperty("maxLength").GetInt32().Should().Be(12, "string properties should still use maxLength");
        code.TryGetProperty("minItems", out _).Should().BeFalse("string properties should not emit minItems");
        code.TryGetProperty("maxItems", out _).Should().BeFalse("string properties should not emit maxItems");
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

    private static OpenApiOutputRenderingContext CreateSyntheticOutputContextWithInfoOptions(
        OpenApiPathItemModel[] pathItems,
        OpenApiInfoOptions infoOptions,
        OpenApiSchemaComponentModel[]? schemaComponents = null)
    {
        var documentBuilderContext = new OpenApiDocumentBuilderContext(
            new BlueprintApp(),
            null,
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiDocumentOperationContext>.Empty);
        var schemaProjectionContext = new OpenApiSchemaProjectionContext(documentBuilderContext, ImmutableArray<OpenApiSchemaModel>.Empty);
        var schemaComponentContext = new OpenApiSchemaComponentRenderingContext(schemaProjectionContext, [.. schemaComponents ?? []]);
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

        return OpenApiOutputRenderingContext.Create(documentContext, infoOptions: infoOptions);
    }

    [Fact]
    public void When_custom_security_schemes_configured_then_output_uses_configured_schemes_instead_of_default()
    {
        var infoOptions = new OpenApiInfoOptions { Title = "TestApi" };
        infoOptions.SecuritySchemes.Add(new OpenApiSecurityScheme
        {
            Name = "apiKeyAuth",
            Type = "apiKey",
            ParameterName = "X-API-Key",
            In = "header",
            Description = "API key authentication",
        });

        var authOp = new OpenApiOperationModel(
            "TestOp",
            "TestOp",
            ["GET"],
            ["/test"],
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiResponseObjectModel>.Empty,
            ImmutableArray<OpenApiParameterModel>.Empty,
            null)
        {
            RequiresAuthentication = true,
        };

        var pathItems = new[]
        {
            new OpenApiPathItemModel("/test", [
                new OpenApiPathItemMethodModel("GET", [authOp]),
            ]),
        };

        var context = CreateSyntheticOutputContextWithInfoOptions(pathItems, infoOptions);
        var doc = JsonDocument.Parse(context.Json);

        // Should have the custom scheme, not bearerAuth
        var securitySchemes = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        securitySchemes.TryGetProperty("apiKeyAuth", out var apiKeyScheme).Should().BeTrue();
        apiKeyScheme.GetProperty("type").GetString().Should().Be("apiKey");
        apiKeyScheme.GetProperty("name").GetString().Should().Be("X-API-Key");
        apiKeyScheme.GetProperty("in").GetString().Should().Be("header");
        apiKeyScheme.GetProperty("description").GetString().Should().Be("API key authentication");

        securitySchemes.TryGetProperty("bearerAuth", out _).Should().BeFalse(
            "custom schemes should replace, not supplement, the default bearerAuth");
    }

    [Fact]
    public void When_no_custom_security_schemes_and_auth_required_then_default_bearer_jwt_is_used()
    {
        var authOp = new OpenApiOperationModel(
            "TestOp",
            "TestOp",
            ["GET"],
            ["/test"],
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiResponseObjectModel>.Empty,
            ImmutableArray<OpenApiParameterModel>.Empty,
            null)
        {
            RequiresAuthentication = true,
        };

        var pathItems = new[]
        {
            new OpenApiPathItemModel("/test", [
                new OpenApiPathItemMethodModel("GET", [authOp]),
            ]),
        };

        var context = CreateSyntheticOutputContextWithInfoOptions(pathItems, new OpenApiInfoOptions { Title = "TestApi" });
        var doc = JsonDocument.Parse(context.Json);

        var securitySchemes = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        securitySchemes.TryGetProperty("bearerAuth", out var bearerScheme).Should().BeTrue();
        bearerScheme.GetProperty("type").GetString().Should().Be("http");
        bearerScheme.GetProperty("scheme").GetString().Should().Be("bearer");
        bearerScheme.GetProperty("bearerFormat").GetString().Should().Be("JWT");
    }

    [Fact]
    public void When_oauth2_security_scheme_configured_then_flows_are_rendered()
    {
        var infoOptions = new OpenApiInfoOptions { Title = "TestApi" };
        infoOptions.SecuritySchemes.Add(new OpenApiSecurityScheme
        {
            Name = "oauth2",
            Type = "oauth2",
            Flows = new OpenApiOAuth2Flows
            {
                AuthorizationCode = new OpenApiOAuth2Flow
                {
                    AuthorizationUrl = "https://auth.example.com/authorize",
                    TokenUrl = "https://auth.example.com/token",
                    Scopes = { ["read"] = "Read access", ["write"] = "Write access" },
                },
            },
        });

        var authOp = new OpenApiOperationModel(
            "TestOp",
            "TestOp",
            ["GET"],
            ["/test"],
            ImmutableArray<string>.Empty,
            ImmutableArray<OpenApiResponseObjectModel>.Empty,
            ImmutableArray<OpenApiParameterModel>.Empty,
            null)
        {
            RequiresAuthentication = true,
        };

        var pathItems = new[]
        {
            new OpenApiPathItemModel("/test", [
                new OpenApiPathItemMethodModel("GET", [authOp]),
            ]),
        };

        var context = CreateSyntheticOutputContextWithInfoOptions(pathItems, infoOptions);
        var doc = JsonDocument.Parse(context.Json);

        var scheme = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("oauth2");

        scheme.GetProperty("type").GetString().Should().Be("oauth2");

        var flow = scheme.GetProperty("flows").GetProperty("authorizationCode");
        flow.GetProperty("authorizationUrl").GetString().Should().Be("https://auth.example.com/authorize");
        flow.GetProperty("tokenUrl").GetString().Should().Be("https://auth.example.com/token");
        flow.GetProperty("scopes").GetProperty("read").GetString().Should().Be("Read access");
        flow.GetProperty("scopes").GetProperty("write").GetString().Should().Be("Write access");
    }

    [Fact]
    public void When_custom_schema_name_formatter_configured_then_component_keys_use_formatter()
    {
        var infoOptions = new OpenApiInfoOptions
        {
            Title = "TestApi",
            SchemaNameFormatter = clrName =>
            {
                // Strip namespace, keep only the short type name
                var lastDot = clrName.LastIndexOf('.');
                return lastDot >= 0 ? clrName[(lastDot + 1)..] : clrName;
            },
        };

        var schemaComponents = new[]
        {
            new OpenApiSchemaComponentModel(
                "Symprex.Signature365.Portal.Contract.Api.TemplateApiResource",
                "object",
                [],
                [new OpenApiSchemaComponentPropertyModel("Name", "string", false, false)]),
        };

        var context = CreateSyntheticOutputContextWithInfoOptions([], infoOptions, schemaComponents);
        var doc = JsonDocument.Parse(context.Json);

        var schemas = doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        schemas.TryGetProperty("TemplateApiResource", out _).Should().BeTrue(
            "custom formatter should strip namespace from schema key");
        schemas.TryGetProperty("Symprex_Signature365_Portal_Contract_Api_TemplateApiResource", out _).Should().BeFalse(
            "default underscore-separated key should not appear when custom formatter is configured");
    }

    [Fact]
    public void When_schema_name_formatter_is_null_then_default_underscore_formatting_is_used()
    {
        var infoOptions = new OpenApiInfoOptions
        {
            Title = "TestApi",
            SchemaNameFormatter = null,
        };

        var schemaComponents = new[]
        {
            new OpenApiSchemaComponentModel(
                "Sample.ResponseDto",
                "object",
                [],
                [new OpenApiSchemaComponentPropertyModel("Name", "string", false, false)]),
        };

        var context = CreateSyntheticOutputContextWithInfoOptions([], infoOptions, schemaComponents);
        var doc = JsonDocument.Parse(context.Json);

        doc.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .TryGetProperty("Sample_ResponseDto", out _)
            .Should()
            .BeTrue("null formatter should fall back to default dot-to-underscore replacement");
    }
}
