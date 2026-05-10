using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders the assembled OpenAPI document model into a standards-conformant OpenAPI 3.x JSON string.
/// This seam deliberately stays output-only: it writes the current path and schema inventory while allowing narrow
/// post-configuration hooks such as info customisation, tags, and schema processors without widening into descriptor
/// or host-registration changes.
/// </summary>
/// <param name="DocumentRendering">The assembled document model that was rendered to produce this output.</param>
/// <param name="Json">The rendered OpenAPI 3.x JSON string for the assembled document.</param>
public sealed record OpenApiOutputRenderingContext(
    OpenApiDocumentRenderingContext DocumentRendering,
    string Json)
{
    private const string OpenApiVersion = "3.0.4";

    /// <summary>
    /// Converts a raw CLR type name into a safe OpenAPI schema component key by replacing <c>.</c> with <c>_</c>.
    /// CLR names such as <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> are allowed by the OpenAPI 3.x component-name
    /// regex (<c>^[a-zA-Z0-9\.\-_]+$</c>), but many popular tools (Swagger UI, openapi-generator, etc.) treat dots as
    /// namespace separators and fail to resolve <c>$ref</c> pointers that contain them. Using underscores preserves
    /// the full type identity while remaining universally safe.
    /// </summary>
    private static string SanitiseSchemaKey(string clrTypeName)
    {
        return clrTypeName.Replace('.', '_');
    }

    /// <summary>
    /// Creates a rendered OpenAPI 3.x JSON string from the supplied assembled document model.
    /// The rendered output follows the standard OpenAPI 3.x object structure: a top-level <c>openapi</c> version
    /// string, a stub <c>info</c> object, a <c>paths</c> object keyed by route template, and a
    /// <c>components/schemas</c> section for covered reusable body types. Schema references in responses use
    /// the <c>$ref</c> pointer format to the matching component entry.
    /// </summary>
    /// <param name="documentRendering">The assembled document model to render.</param>
    /// <param name="infoOptions">
    /// Optional customisation for the <c>info</c> block. When <see langword="null"/> the defaults in
    /// <see cref="OpenApiInfoOptions"/> are used.
    /// </param>
    /// <param name="schemaProcessorOptions">
    /// Optional ordered schema processors that can transform rendered schema components before they are written into the
    /// <c>components/schemas</c> section. When <see langword="null"/> no schema processors are applied.
    /// </param>
    /// <returns>The rendered output context carrying the JSON string.</returns>
    public static OpenApiOutputRenderingContext Create(
        OpenApiDocumentRenderingContext documentRendering,
        OpenApiInfoOptions? infoOptions = null,
        OpenApiSchemaProcessorOptions? schemaProcessorOptions = null)
    {
        ArgumentNullException.ThrowIfNull(documentRendering);

        var json = RenderJson(
            documentRendering,
            infoOptions ?? new OpenApiInfoOptions(),
            schemaProcessorOptions ?? new OpenApiSchemaProcessorOptions());
        return new OpenApiOutputRenderingContext(documentRendering, json);
    }

    private static string RenderJson(
        OpenApiDocumentRenderingContext document,
        OpenApiInfoOptions infoOptions,
        OpenApiSchemaProcessorOptions schemaProcessorOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });

        WriteDocument(writer, document, infoOptions, schemaProcessorOptions);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        OpenApiDocumentRenderingContext document,
        OpenApiInfoOptions infoOptions,
        OpenApiSchemaProcessorOptions schemaProcessorOptions)
    {
        writer.WriteStartObject();

        writer.WriteString("openapi", OpenApiVersion);
        WriteInfo(writer, infoOptions);
        WriteServers(writer, infoOptions.Servers);
        WritePaths(writer, document.Paths);
        WriteComponents(writer, document.SchemaComponents, schemaProcessorOptions, document.Paths);
        WriteDocumentTags(writer, document.Paths);

        writer.WriteEndObject();
    }

    private static void WriteInfo(Utf8JsonWriter writer, OpenApiInfoOptions infoOptions)
    {
        writer.WritePropertyName("info");
        writer.WriteStartObject();
        writer.WriteString("title", infoOptions.Title);
        writer.WriteString("version", infoOptions.Version);

        if (!string.IsNullOrEmpty(infoOptions.Description))
        {
            writer.WriteString("description", infoOptions.Description);
        }

        if (!string.IsNullOrEmpty(infoOptions.TermsOfService))
        {
            writer.WriteString("termsOfService", infoOptions.TermsOfService);
        }

        if (infoOptions.Contact is { HasValue: true } contact)
        {
            writer.WritePropertyName("contact");
            writer.WriteStartObject();

            if (!string.IsNullOrEmpty(contact.Name))
            {
                writer.WriteString("name", contact.Name);
            }

            if (!string.IsNullOrEmpty(contact.Url))
            {
                writer.WriteString("url", contact.Url);
            }

            if (!string.IsNullOrEmpty(contact.Email))
            {
                writer.WriteString("email", contact.Email);
            }

            writer.WriteEndObject();
        }

        if (infoOptions.License is { } license)
        {
            writer.WritePropertyName("license");
            writer.WriteStartObject();
            writer.WriteString("name", license.Name);

            if (!string.IsNullOrEmpty(license.Url))
            {
                writer.WriteString("url", license.Url);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteServers(Utf8JsonWriter writer, List<OpenApiServerEntry> servers)
    {
        if (servers.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("servers");
        writer.WriteStartArray();

        foreach (var server in servers)
        {
            writer.WriteStartObject();
            writer.WriteString("url", server.Url);

            if (!string.IsNullOrEmpty(server.Description))
            {
                writer.WriteString("description", server.Description);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePaths(Utf8JsonWriter writer, ImmutableArray<OpenApiPathItemModel> paths)
    {
        writer.WritePropertyName("paths");
        writer.WriteStartObject();

        foreach (var pathItem in paths)
        {
            WritePathItem(writer, pathItem);
        }

        writer.WriteEndObject();
    }

    private static void WritePathItem(Utf8JsonWriter writer, OpenApiPathItemModel pathItem)
    {
        writer.WritePropertyName(pathItem.UrlFormat);
        writer.WriteStartObject();

        foreach (var methodSlot in pathItem.Methods)
        {
            WriteMethodSlot(writer, methodSlot);
        }

        writer.WriteEndObject();
    }

    private static void WriteMethodSlot(Utf8JsonWriter writer, OpenApiPathItemMethodModel methodSlot)
    {
        writer.WritePropertyName(methodSlot.HttpMethod.ToLowerInvariant());
        writer.WriteStartObject();

        // When multiple operations share a method slot, use the first alphabetically for operationId.
        var primaryOperation = methodSlot.Operations.IsEmpty ? null : methodSlot.Operations[0];
        if (primaryOperation is not null)
        {
            writer.WriteString("operationId", primaryOperation.OperationName);

            if (primaryOperation.Summary is not null)
            {
                writer.WriteString("summary", primaryOperation.Summary);
            }

            if (primaryOperation.Description is not null)
            {
                writer.WriteString("description", primaryOperation.Description);
            }

            WriteTags(writer, primaryOperation.Tags);
            WriteParameters(writer, primaryOperation.Parameters);
            WriteRequestBody(writer, primaryOperation.RequestBody);
            WriteResponses(writer, primaryOperation.Responses);

            if (primaryOperation.Deprecated)
            {
                writer.WriteBoolean("deprecated", true);

                if (primaryOperation.DeprecationReason is not null)
                {
                    writer.WriteString("x-deprecated-reason", primaryOperation.DeprecationReason);
                }
            }

            if (primaryOperation.RequiresAuthentication)
            {
                writer.WritePropertyName("security");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName("bearerAuth");
                writer.WriteStartArray();
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
        }
        else
        {
            WriteResponses(writer, ImmutableArray<OpenApiResponseObjectModel>.Empty);
        }

        writer.WriteEndObject();
    }

    private static void WriteTags(Utf8JsonWriter writer, ImmutableArray<string> tags)
    {
        if (tags.IsEmpty)
        {
            return;
        }

        writer.WritePropertyName("tags");
        writer.WriteStartArray();

        foreach (var tag in tags)
        {
            writer.WriteStringValue(tag);
        }

        writer.WriteEndArray();
    }

    private static void WriteParameters(Utf8JsonWriter writer, ImmutableArray<OpenApiParameterModel> parameters)
    {
        if (parameters.IsEmpty)
        {
            return;
        }

        writer.WritePropertyName("parameters");
        writer.WriteStartArray();

        foreach (var parameter in parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("in", parameter.In);
            writer.WriteBoolean("required", parameter.Required);

            if (parameter.Description is not null)
            {
                writer.WriteString("description", parameter.Description);
            }

            if (parameter.IsDeprecated)
            {
                writer.WriteBoolean("deprecated", true);

                if (parameter.DeprecationReason is not null)
                {
                    writer.WriteString("x-deprecated-reason", parameter.DeprecationReason);
                }
            }

            writer.WritePropertyName("schema");
            writer.WriteStartObject();
            writer.WriteString("type", parameter.SchemaType);

            if (parameter.SchemaFormat is not null)
            {
                writer.WriteString("format", parameter.SchemaFormat);
            }

            if (parameter.IsNullable && !parameter.Required)
            {
                writer.WriteBoolean("nullable", true);
            }

            WriteEnumValues(writer, parameter.EnumValues);
            WriteNumericConstraints(writer, parameter.Minimum, parameter.Maximum, parameter.ExclusiveMinimum, parameter.ExclusiveMaximum);
            WriteStringConstraints(writer, parameter.MinLength, parameter.MaxLength, parameter.Pattern);

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRequestBody(Utf8JsonWriter writer, OpenApiRequestBodyModel? requestBody)
    {
        if (requestBody is null)
        {
            return;
        }

        writer.WritePropertyName("requestBody");
        writer.WriteStartObject();
        writer.WriteBoolean("required", requestBody.Required);

        writer.WritePropertyName("content");
        writer.WriteStartObject();
        writer.WritePropertyName("application/json");
        writer.WriteStartObject();

        writer.WritePropertyName("schema");
        writer.WriteStartObject();
        writer.WriteString("type", "object");

        if (!requestBody.Properties.IsEmpty)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach (var property in requestBody.Properties)
            {
                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                writer.WriteString("type", property.SchemaType);

                if (property.SchemaFormat is not null)
                {
                    writer.WriteString("format", property.SchemaFormat);
                }

                if (property.IsNullable && !property.IsRequired)
                {
                    writer.WriteBoolean("nullable", true);
                }

                if (property.Description is not null)
                {
                    writer.WriteString("description", property.Description);
                }

                WriteEnumValues(writer, property.EnumValues);

                if (property.ItemsSchemaRef is not null)
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.ItemsSchemaRef)}");
                    writer.WriteEndObject();
                }
                else if (property.ItemsType is not null)
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    writer.WriteString("type", property.ItemsType);

                    if (property.ItemsFormat is not null)
                    {
                        writer.WriteString("format", property.ItemsFormat);
                    }

                    writer.WriteEndObject();
                }

                if (property.AdditionalPropertiesSchemaRef is not null)
                {
                    writer.WritePropertyName("additionalProperties");
                    writer.WriteStartObject();
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesSchemaRef)}");
                    writer.WriteEndObject();
                }
                else if (property.AdditionalPropertiesType is not null)
                {
                    writer.WritePropertyName("additionalProperties");
                    writer.WriteStartObject();
                    writer.WriteString("type", property.AdditionalPropertiesType);

                    if (property.AdditionalPropertiesFormat is not null)
                    {
                        writer.WriteString("format", property.AdditionalPropertiesFormat);
                    }

                    writer.WriteEndObject();
                }

                WriteNumericConstraints(writer, property.Minimum, property.Maximum, property.ExclusiveMinimum, property.ExclusiveMaximum);
                WriteStringConstraints(writer, property.MinLength, property.MaxLength, property.Pattern);

                if (property.IsDeprecated)
                {
                    writer.WriteBoolean("deprecated", true);

                    if (property.DeprecationReason is not null)
                    {
                        writer.WriteString("x-deprecated-reason", property.DeprecationReason);
                    }
                }

                if (property.DefaultValue is not null)
                {
                    WriteDefaultValue(writer, property.DefaultValue);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            var requiredProperties = requestBody.Properties
                .Where(static p => p.IsRequired)
                .Select(static p => p.Name)
                .ToArray();

            if (requiredProperties.Length > 0)
            {
                writer.WritePropertyName("required");
                writer.WriteStartArray();

                foreach (var name in requiredProperties)
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteEnumValues(Utf8JsonWriter writer, ImmutableArray<string> enumValues)
    {
        if (enumValues.IsDefaultOrEmpty)
        {
            return;
        }

        writer.WritePropertyName("enum");
        writer.WriteStartArray();

        foreach (var value in enumValues)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNumericConstraints(Utf8JsonWriter writer, decimal? minimum, decimal? maximum, bool exclusiveMinimum, bool exclusiveMaximum)
    {
        if (minimum.HasValue)
        {
            writer.WriteNumber("minimum", minimum.Value);
        }

        if (exclusiveMinimum)
        {
            writer.WriteBoolean("exclusiveMinimum", true);
        }

        if (maximum.HasValue)
        {
            writer.WriteNumber("maximum", maximum.Value);
        }

        if (exclusiveMaximum)
        {
            writer.WriteBoolean("exclusiveMaximum", true);
        }
    }

    private static void WriteStringConstraints(Utf8JsonWriter writer, int? minLength, int? maxLength, string? pattern)
    {
        if (minLength.HasValue)
        {
            writer.WriteNumber("minLength", minLength.Value);
        }

        if (maxLength.HasValue)
        {
            writer.WriteNumber("maxLength", maxLength.Value);
        }

        if (!string.IsNullOrWhiteSpace(pattern))
        {
            writer.WriteString("pattern", pattern);
        }
    }

    /// <summary>
    /// Writes a JSON <c>"default"</c> property with the correct JSON type based on the CLR value.
    /// </summary>
    private static void WriteDefaultValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case bool b:
                writer.WriteBoolean("default", b);
                break;
            case int i:
                writer.WriteNumber("default", i);
                break;
            case long l:
                writer.WriteNumber("default", l);
                break;
            case double d:
                writer.WriteNumber("default", d);
                break;
            case decimal m:
                writer.WriteNumber("default", m);
                break;
            case float f:
                writer.WriteNumber("default", f);
                break;
            default:
                writer.WriteString("default", value.ToString());
                break;
        }
    }

    private static void WriteResponses(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseObjectModel> responses)
    {
        writer.WritePropertyName("responses");
        writer.WriteStartObject();

        foreach (var response in responses)
        {
            WriteResponse(writer, response);
        }

        writer.WriteEndObject();
    }

    private static void WriteResponse(Utf8JsonWriter writer, OpenApiResponseObjectModel response)
    {
        writer.WritePropertyName(response.StatusCode.ToString());
        writer.WriteStartObject();

        writer.WriteString("description", MapStatusDescription(response.StatusCode));

        if (!response.Content.IsEmpty)
        {
            WriteResponseContent(writer, response.Content);
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseContent(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseContentModel> content)
    {
        writer.WritePropertyName("content");
        writer.WriteStartObject();

        foreach (var mediaEntry in content)
        {
            writer.WritePropertyName(mediaEntry.MediaType);
            writer.WriteStartObject();
            writer.WritePropertyName("schema");

            WriteResponseSchema(writer, mediaEntry);

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseSchema(
        Utf8JsonWriter writer,
        OpenApiResponseContentModel content)
    {
        if (content.Discriminator is not null)
        {
            WriteResourceEventDiscriminatedResponseSchema(writer, content.Discriminator);
            return;
        }

        var schemaComponents = content.SchemaComponents;
        if (schemaComponents.Length == 1)
        {
            var component = schemaComponents[0];
            writer.WriteStartObject();

            if (component.IsArrayWrapper)
            {
                writer.WriteString("type", "array");
                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.Component.BodyTypeName)}");
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.BodyTypeName)}");
            }

            writer.WriteEndObject();
        }
        else if (schemaComponents.Length > 1)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("oneOf");
            writer.WriteStartArray();

            foreach (var component in schemaComponents)
            {
                writer.WriteStartObject();

                if (component.IsArrayWrapper)
                {
                    writer.WriteString("type", "array");
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.Component.BodyTypeName)}");
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.BodyTypeName)}");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else if (content.InlineSchema is not null)
        {
            writer.WriteStartObject();
            writer.WriteString("type", content.InlineSchema.Type);

            if (content.InlineSchema.Format is not null)
            {
                writer.WriteString("format", content.InlineSchema.Format);
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    private static void WriteResourceEventDiscriminatedResponseSchema(
        Utf8JsonWriter writer,
        OpenApiResponseDiscriminatorModel discriminator)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("oneOf");
        writer.WriteStartArray();

        foreach (var discriminatorCase in discriminator.Cases)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            writer.WriteStringValue("eventId");
            writer.WriteStringValue("resourceId");
            writer.WriteStringValue("content");
            writer.WriteEndArray();
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            writer.WritePropertyName("eventId");
            writer.WriteStartObject();
            writer.WriteString("type", "string");
            writer.WritePropertyName("enum");
            writer.WriteStartArray();
            writer.WriteStringValue(discriminatorCase.Value);
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName("resourceId");
            writer.WriteStartObject();
            writer.WriteString("type", "string");
            writer.WriteEndObject();

            writer.WritePropertyName("content");
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(discriminatorCase.SchemaComponent.BodyTypeName)}");
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("discriminator");
        writer.WriteStartObject();
        writer.WriteString("propertyName", discriminator.PropertyName);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteComponents(
        Utf8JsonWriter writer,
        ImmutableArray<OpenApiSchemaComponentModel> schemaComponents,
        OpenApiSchemaProcessorOptions schemaProcessorOptions,
        ImmutableArray<OpenApiPathItemModel> paths)
    {
        var hasAuthenticatedOperations = paths
            .SelectMany(static p => p.Methods)
            .SelectMany(static m => m.Operations)
            .Any(static op => op.RequiresAuthentication);

        if (schemaComponents.IsEmpty && !hasAuthenticatedOperations)
        {
            return;
        }

        writer.WritePropertyName("components");
        writer.WriteStartObject();

        if (!schemaComponents.IsEmpty)
        {
            // Array-typed schemas are rendered inline at the response level as "type: array, items: { $ref: ... }"
            // rather than as standalone schema components.
            var objectSchemaComponents = schemaComponents
                .Where(static c => !string.Equals(c.Type, "array", StringComparison.Ordinal) || !c.Properties.IsEmpty);

            writer.WritePropertyName("schemas");
            writer.WriteStartObject();

            foreach (var component in objectSchemaComponents)
            {
                WriteSchemaComponent(writer, ProcessSchemaComponent(component, schemaProcessorOptions));
            }

            writer.WriteEndObject();
        }

        if (hasAuthenticatedOperations)
        {
            writer.WritePropertyName("securitySchemes");
            writer.WriteStartObject();
            writer.WritePropertyName("bearerAuth");
            writer.WriteStartObject();
            writer.WriteString("type", "http");
            writer.WriteString("scheme", "bearer");
            writer.WriteString("bearerFormat", "JWT");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteSchemaComponent(Utf8JsonWriter writer, OpenApiSchemaComponentModel component)
    {
        writer.WritePropertyName(SanitiseSchemaKey(component.BodyTypeName));
        writer.WriteStartObject();

        if (component.Type is not null)
        {
            writer.WriteString("type", component.Type);
        }

        if (!component.RequiredPropertyNames.IsEmpty)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();

            foreach (var name in component.RequiredPropertyNames)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
        }

        if (!component.Properties.IsEmpty)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();

            foreach (var property in component.Properties)
            {
                WriteSchemaProperty(writer, property);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static OpenApiSchemaComponentModel ProcessSchemaComponent(
        OpenApiSchemaComponentModel component,
        OpenApiSchemaProcessorOptions schemaProcessorOptions)
    {
        var processedComponent = component;

        foreach (var processor in schemaProcessorOptions.Processors)
        {
            var processorContext = new OpenApiSchemaProcessorContext(processedComponent);
            processor(processorContext);
            processedComponent = processorContext.Component;
        }

        return processedComponent;
    }

    private static void WriteSchemaProperty(Utf8JsonWriter writer, OpenApiSchemaComponentPropertyModel property)
    {
        writer.WritePropertyName(property.Name);
        writer.WriteStartObject();

        if (property.Type is not null)
        {
            writer.WriteString("type", property.Type);
        }

        if (property.IsNullable)
        {
            writer.WriteBoolean("nullable", true);
        }

        if (property.Format is not null)
        {
            writer.WriteString("format", property.Format);
        }

        if (property.Description is not null)
        {
            writer.WriteString("description", property.Description);
        }

        WriteEnumValues(writer, property.EnumValues);

        if (property.ItemsSchemaRef is not null)
        {
            writer.WritePropertyName("items");
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.ItemsSchemaRef)}");
            writer.WriteEndObject();
        }
        else if (property.ItemsType is not null)
        {
            writer.WritePropertyName("items");
            writer.WriteStartObject();
            writer.WriteString("type", property.ItemsType);

            if (property.ItemsFormat is not null)
            {
                writer.WriteString("format", property.ItemsFormat);
            }

            writer.WriteEndObject();
        }

        if (property.AdditionalPropertiesSchemaRef is not null)
        {
            writer.WritePropertyName("additionalProperties");
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesSchemaRef)}");
            writer.WriteEndObject();
        }
        else if (property.AdditionalPropertiesType is not null)
        {
            writer.WritePropertyName("additionalProperties");
            writer.WriteStartObject();
            writer.WriteString("type", property.AdditionalPropertiesType);

            if (property.AdditionalPropertiesFormat is not null)
            {
                writer.WriteString("format", property.AdditionalPropertiesFormat);
            }

            writer.WriteEndObject();
        }

        if (property.MinLength.HasValue)
        {
            writer.WriteNumber("minLength", property.MinLength.Value);
        }

        if (property.MaxLength.HasValue)
        {
            writer.WriteNumber("maxLength", property.MaxLength.Value);
        }

        if (!string.IsNullOrWhiteSpace(property.Pattern))
        {
            writer.WriteString("pattern", property.Pattern);
        }

        if (property.Minimum.HasValue)
        {
            writer.WriteNumber("minimum", property.Minimum.Value);
        }

        if (property.ExclusiveMinimum)
        {
            writer.WriteBoolean("exclusiveMinimum", true);
        }

        if (property.Maximum.HasValue)
        {
            writer.WriteNumber("maximum", property.Maximum.Value);
        }

        if (property.ExclusiveMaximum)
        {
            writer.WriteBoolean("exclusiveMaximum", true);
        }

        if (property.IsDeprecated)
        {
            writer.WriteBoolean("deprecated", true);

            if (property.DeprecationReason is not null)
            {
                writer.WriteString("x-deprecated-reason", property.DeprecationReason);
            }
        }

        if (property.DefaultValue is not null)
        {
            WriteDefaultValue(writer, property.DefaultValue);
        }

        writer.WriteEndObject();
    }

    private static string MapStatusDescription(int statusCode)
    {
        return statusCode switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            304 => "Not Modified",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            415 => "Unsupported Media Type",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => statusCode.ToString(),
        };
    }

    /// <summary>
    /// Renders the top-level <c>tags</c> array by collecting all unique tag names used across operations.
    /// Per the OpenAPI 3.0 specification, declaring tags at the document level provides a canonical ordering
    /// and a place to attach descriptions for documentation tooling.
    /// </summary>
    private static void WriteDocumentTags(Utf8JsonWriter writer, ImmutableArray<OpenApiPathItemModel> paths)
    {
        // Collect all unique tags and the first description found for each tag across all operations.
        var tagDescriptions = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            foreach (var method in path.Methods)
            {
                foreach (var operation in method.Operations)
                {
                    foreach (var tag in operation.Tags)
                    {
                        if (tagDescriptions.TryGetValue(tag, out var existingDesc))
                        {
                            // Keep the first non-null description encountered for this tag.
                            if (existingDesc is null
                                && operation.TagDescriptions.TryGetValue(tag, out var desc)
                                && desc is not null)
                            {
                                tagDescriptions[tag] = desc;
                            }

                            continue;
                        }

                        operation.TagDescriptions.TryGetValue(tag, out var description);
                        tagDescriptions[tag] = description;
                    }
                }
            }
        }

        if (tagDescriptions.Count == 0)
        {
            return;
        }

        var orderedTags = tagDescriptions.Keys
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        writer.WritePropertyName("tags");
        writer.WriteStartArray();

        foreach (var tag in orderedTags)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag);

            if (tagDescriptions[tag] is { Length: > 0 } desc)
            {
                writer.WriteString("description", desc);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
