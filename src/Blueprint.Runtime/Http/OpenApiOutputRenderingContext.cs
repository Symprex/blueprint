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

        // Pre-compute whether security can be hoisted to document level. When every operation
        // requires authentication we write a single top-level security array and opt-out
        // anonymous endpoints with security: [], which is more idiomatic OpenAPI.
        var allOperations = document.Paths
            .SelectMany(static p => p.Methods)
            .SelectMany(static m => m.Operations)
            .ToList();

        var hasAnyAuth = allOperations.Any(static op => op.RequiresAuthentication);
        var allRequireAuth = hasAnyAuth && allOperations.All(static op => op.RequiresAuthentication);

        // Resolve effective security schemes — use configured schemes or default to Bearer JWT
        var effectiveSchemes = infoOptions.SecuritySchemes.Count > 0
            ? infoOptions.SecuritySchemes
            : hasAnyAuth
                ? [OpenApiSecurityScheme.BearerJwt()]
                : [];

        writer.WriteString("openapi", OpenApiVersion);
        WriteInfo(writer, infoOptions);
        WriteServers(writer, infoOptions.Servers);
        WritePaths(writer, document.Paths, allRequireAuth, effectiveSchemes);
        WriteComponents(writer, document.SchemaComponents, schemaProcessorOptions, effectiveSchemes);
        WriteDocumentTags(writer, document.Paths);

        if (allRequireAuth && effectiveSchemes.Count > 0)
        {
            WriteDocumentSecurity(writer, effectiveSchemes);
        }

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

    private static void WritePaths(Utf8JsonWriter writer, ImmutableArray<OpenApiPathItemModel> paths, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes)
    {
        writer.WritePropertyName("paths");
        writer.WriteStartObject();

        foreach (var pathItem in paths)
        {
            WritePathItem(writer, pathItem, useDocumentLevelSecurity, securitySchemes);
        }

        writer.WriteEndObject();
    }

    private static void WritePathItem(Utf8JsonWriter writer, OpenApiPathItemModel pathItem, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes)
    {
        writer.WritePropertyName(pathItem.UrlFormat);
        writer.WriteStartObject();

        foreach (var methodSlot in pathItem.Methods)
        {
            WriteMethodSlot(writer, methodSlot, useDocumentLevelSecurity, securitySchemes);
        }

        writer.WriteEndObject();
    }

    private static void WriteMethodSlot(Utf8JsonWriter writer, OpenApiPathItemMethodModel methodSlot, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes)
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

            // When document-level security is active (all ops require auth), per-operation security
            // blocks are omitted — the document-level array covers them. If an operation were anonymous
            // in this mode we'd write security: [] to opt out, but that can't happen since
            // useDocumentLevelSecurity is only true when ALL operations require auth.
            if (!useDocumentLevelSecurity && primaryOperation.RequiresAuthentication)
            {
                WriteOperationSecurity(writer, securitySchemes);
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

            if (parameter.AllowReserved)
            {
                writer.WriteBoolean("allowReserved", true);
            }

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

            if (parameter.ExampleValue is not null)
            {
                WriteExampleValue(writer, parameter.ExampleValue);
            }

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

                // For array types, [MinLength]/[MaxLength]/[Length] map to minItems/maxItems rather than minLength/maxLength.
                if (string.Equals(property.SchemaType, "array", StringComparison.Ordinal))
                {
                    WriteArrayItemConstraints(writer, property.MinLength, property.MaxLength);
                }
                else
                {
                    WriteStringConstraints(writer, property.MinLength, property.MaxLength, property.Pattern);
                }

                if (property.IsDeprecated)
                {
                    writer.WriteBoolean("deprecated", true);

                    if (property.DeprecationReason is not null)
                    {
                        writer.WriteString("x-deprecated-reason", property.DeprecationReason);
                    }
                }

                if (property.IsWriteOnly)
                {
                    writer.WriteBoolean("writeOnly", true);
                }

                if (property.DefaultValue is not null)
                {
                    WriteDefaultValue(writer, property.DefaultValue);
                }

                if (property.ExampleValue is not null)
                {
                    WriteExampleValue(writer, property.ExampleValue);
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
    /// Writes <c>minItems</c> / <c>maxItems</c> constraints for array-typed properties where
    /// <c>[MinLength]</c>/<c>[MaxLength]</c>/<c>[Length]</c> apply to element count rather than string length.
    /// </summary>
    private static void WriteArrayItemConstraints(Utf8JsonWriter writer, int? minItems, int? maxItems)
    {
        if (minItems.HasValue)
        {
            writer.WriteNumber("minItems", minItems.Value);
        }

        if (maxItems.HasValue)
        {
            writer.WriteNumber("maxItems", maxItems.Value);
        }
    }

    /// <summary>
    /// Writes a JSON <c>"default"</c> property with the correct JSON type based on the CLR value.
    /// </summary>
    private static void WriteDefaultValue(Utf8JsonWriter writer, object value)
    {
        WriteConstantValue(writer, "default", value);
    }

    /// <summary>
    /// Writes a JSON <c>"example"</c> property with the correct JSON type based on the CLR value.
    /// </summary>
    private static void WriteExampleValue(Utf8JsonWriter writer, object value)
    {
        WriteConstantValue(writer, "example", value);
    }

    /// <summary>
    /// Writes a named JSON property with the correct JSON type for a compile-time constant value.
    /// </summary>
    private static void WriteConstantValue(Utf8JsonWriter writer, string propertyName, object value)
    {
        switch (value)
        {
            case bool b:
                writer.WriteBoolean(propertyName, b);
                break;
            case int i:
                writer.WriteNumber(propertyName, i);
                break;
            case long l:
                writer.WriteNumber(propertyName, l);
                break;
            case double d:
                writer.WriteNumber(propertyName, d);
                break;
            case decimal m:
                writer.WriteNumber(propertyName, m);
                break;
            case float f:
                writer.WriteNumber(propertyName, f);
                break;
            default:
                writer.WriteString(propertyName, value.ToString());
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
        writer.WritePropertyName(MapStatusCodeString(response.StatusCode));
        writer.WriteStartObject();

        writer.WriteString("description", MapStatusDescription(response.StatusCode));

        if (!response.Headers.IsDefault && !response.Headers.IsEmpty)
        {
            WriteResponseHeaders(writer, response.Headers);
        }

        if (!response.Content.IsEmpty)
        {
            WriteResponseContent(writer, response.Content);
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseHeaders(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseHeaderModel> headers)
    {
        writer.WritePropertyName("headers");
        writer.WriteStartObject();

        foreach (var header in headers)
        {
            writer.WritePropertyName(header.Name);
            writer.WriteStartObject();

            writer.WriteString("description", header.Description);

            writer.WritePropertyName("schema");
            writer.WriteStartObject();
            writer.WriteString("type", header.SchemaType);
            writer.WriteEndObject();

            writer.WriteEndObject();
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
        List<OpenApiSecurityScheme> securitySchemes)
    {
        if (schemaComponents.IsEmpty && securitySchemes.Count == 0)
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

        if (securitySchemes.Count > 0)
        {
            writer.WritePropertyName("securitySchemes");
            writer.WriteStartObject();

            foreach (var scheme in securitySchemes)
            {
                WriteSecurityScheme(writer, scheme);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteSecurityScheme(Utf8JsonWriter writer, OpenApiSecurityScheme scheme)
    {
        writer.WritePropertyName(scheme.Name);
        writer.WriteStartObject();
        writer.WriteString("type", scheme.Type);

        if (scheme.Description is not null)
        {
            writer.WriteString("description", scheme.Description);
        }

        if (scheme.Scheme is not null)
        {
            writer.WriteString("scheme", scheme.Scheme);
        }

        if (scheme.BearerFormat is not null)
        {
            writer.WriteString("bearerFormat", scheme.BearerFormat);
        }

        if (scheme.ParameterName is not null)
        {
            writer.WriteString("name", scheme.ParameterName);
        }

        if (scheme.In is not null)
        {
            writer.WriteString("in", scheme.In);
        }

        if (scheme.OpenIdConnectUrl is not null)
        {
            writer.WriteString("openIdConnectUrl", scheme.OpenIdConnectUrl);
        }

        if (scheme.Flows is not null)
        {
            WriteOAuth2Flows(writer, scheme.Flows);
        }

        writer.WriteEndObject();
    }

    private static void WriteOAuth2Flows(Utf8JsonWriter writer, OpenApiOAuth2Flows flows)
    {
        writer.WritePropertyName("flows");
        writer.WriteStartObject();

        if (flows.AuthorizationCode is not null)
        {
            WriteOAuth2Flow(writer, "authorizationCode", flows.AuthorizationCode);
        }

        if (flows.ClientCredentials is not null)
        {
            WriteOAuth2Flow(writer, "clientCredentials", flows.ClientCredentials);
        }

        if (flows.Implicit is not null)
        {
            WriteOAuth2Flow(writer, "implicit", flows.Implicit);
        }

        writer.WriteEndObject();
    }

    private static void WriteOAuth2Flow(Utf8JsonWriter writer, string flowName, OpenApiOAuth2Flow flow)
    {
        writer.WritePropertyName(flowName);
        writer.WriteStartObject();

        if (flow.AuthorizationUrl is not null)
        {
            writer.WriteString("authorizationUrl", flow.AuthorizationUrl);
        }

        if (flow.TokenUrl is not null)
        {
            writer.WriteString("tokenUrl", flow.TokenUrl);
        }

        if (flow.RefreshUrl is not null)
        {
            writer.WriteString("refreshUrl", flow.RefreshUrl);
        }

        writer.WritePropertyName("scopes");
        writer.WriteStartObject();

        foreach (var (scope, description) in flow.Scopes)
        {
            writer.WriteString(scope, description);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the document-level <c>security</c> array, applied when every operation requires
    /// authentication so per-operation blocks can be omitted entirely.
    /// </summary>
    private static void WriteDocumentSecurity(Utf8JsonWriter writer, List<OpenApiSecurityScheme> securitySchemes)
    {
        writer.WritePropertyName("security");
        writer.WriteStartArray();
        writer.WriteStartObject();

        foreach (var scheme in securitySchemes)
        {
            writer.WritePropertyName(scheme.Name);
            writer.WriteStartArray();
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    /// <summary>
    /// Writes a per-operation <c>security</c> block referencing all configured security schemes.
    /// Used when the document contains a mix of authenticated and anonymous operations.
    /// </summary>
    private static void WriteOperationSecurity(Utf8JsonWriter writer, List<OpenApiSecurityScheme> securitySchemes)
    {
        writer.WritePropertyName("security");
        writer.WriteStartArray();
        writer.WriteStartObject();

        foreach (var scheme in securitySchemes)
        {
            writer.WritePropertyName(scheme.Name);
            writer.WriteStartArray();
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static void WriteSchemaComponent(Utf8JsonWriter writer, OpenApiSchemaComponentModel component)
    {
        writer.WritePropertyName(SanitiseSchemaKey(component.BodyTypeName));
        writer.WriteStartObject();

        // Prefer an explicit [DisplayName] title; fall back to the short CLR type name.
        var shortTitle = component.Title ?? component.BodyTypeName;
        var lastDot = shortTitle.LastIndexOf('.');

        if (lastDot >= 0 && component.Title is null)
        {
            shortTitle = shortTitle[(lastDot + 1)..];
        }

        writer.WriteString("title", shortTitle);

        if (component.Description is not null)
        {
            writer.WriteString("description", component.Description);
        }

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
            // For array types, [MinLength] / [Length] maps to minItems; for strings it maps to minLength.
            var keyword = string.Equals(property.Type, "array", StringComparison.Ordinal) ? "minItems" : "minLength";
            writer.WriteNumber(keyword, property.MinLength.Value);
        }

        if (property.MaxLength.HasValue)
        {
            var keyword = string.Equals(property.Type, "array", StringComparison.Ordinal) ? "maxItems" : "maxLength";
            writer.WriteNumber(keyword, property.MaxLength.Value);
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

        if (property.IsReadOnly)
        {
            writer.WriteBoolean("readOnly", true);
        }

        if (property.IsWriteOnly)
        {
            writer.WriteBoolean("writeOnly", true);
        }

        if (property.DefaultValue is not null)
        {
            WriteDefaultValue(writer, property.DefaultValue);
        }

        if (property.ExampleValue is not null)
        {
            WriteExampleValue(writer, property.ExampleValue);
        }

        writer.WriteEndObject();
    }

    private static string MapStatusCodeString(int statusCode)
    {
        return statusCode switch
        {
            200 => "200",
            201 => "201",
            202 => "202",
            204 => "204",
            301 => "301",
            302 => "302",
            304 => "304",
            400 => "400",
            401 => "401",
            403 => "403",
            404 => "404",
            409 => "409",
            422 => "422",
            500 => "500",
            _ => statusCode.ToString(),
        };
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
