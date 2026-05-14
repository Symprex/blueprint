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
    /// <summary>
    /// Default OpenAPI specification version emitted when no override is provided via
    /// <see cref="OpenApiInfoOptions.OpenApiVersion"/>.
    /// </summary>
    private const string DefaultOpenApiVersion = "3.0.4";

    /// <summary>
    /// Converts a raw CLR type name into a safe OpenAPI schema component key. When a custom
    /// <paramref name="customFormatter"/> is provided it is used directly; otherwise the default
    /// strategy extracts the simple class name (everything after the last <c>.</c>) which produces
    /// clean, readable schema names like <c>TemplateApiResource</c> instead of
    /// <c>MyApp_Api_TemplateApiResource</c>. Consumers that need namespace disambiguation or
    /// suffix stripping can provide a custom formatter via
    /// <see cref="OpenApiInfoOptions.SchemaNameFormatter"/>.
    /// </summary>
    /// <remarks>
    /// Generic type names are flattened using the NJsonSchema "Of" convention for backward
    /// compatibility with legacy Blueprint and NSwag-based TypeScript generators. For example:
    /// <c>Blueprint.Http.ResourceEvent&lt;MyApp.TemplateApiResource&gt;</c> becomes
    /// <c>ResourceEventOfTemplateApiResource</c>. Nested generics are handled recursively.
    /// </remarks>
    private static string SanitiseSchemaKey(string clrTypeName, Func<string, string>? customFormatter)
    {
        if (customFormatter is not null)
        {
            return customFormatter(clrTypeName);
        }

        return SimplifyClrTypeName(clrTypeName);
    }

    /// <summary>
    /// Converts a fully-qualified CLR type name (including generic type arguments) into a
    /// clean OpenAPI schema key using the NJsonSchema "Of" convention. Namespace prefixes are
    /// stripped and generic angle brackets are replaced with <c>Of</c> separators so that
    /// <c>Blueprint.Http.ResourceEvent&lt;MyApp.TemplateApiResource&gt;</c> becomes
    /// <c>ResourceEventOfTemplateApiResource</c>.
    /// </summary>
    private static string SimplifyClrTypeName(string clrTypeName)
    {
        var angleBracketIndex = clrTypeName.IndexOf('<');

        if (angleBracketIndex < 0)
        {
            // Non-generic: strip namespace only (take text after last dot).
            var lastDot = clrTypeName.LastIndexOf('.');
            return lastDot >= 0 ? clrTypeName.Substring(lastDot + 1) : clrTypeName;
        }

        // Generic: extract the outer type name (before '<') and strip its namespace.
        var outerFullName = clrTypeName.Substring(0, angleBracketIndex);
        var outerLastDot = outerFullName.LastIndexOf('.');
        var outerSimpleName = outerLastDot >= 0 ? outerFullName.Substring(outerLastDot + 1) : outerFullName;

        // Extract the inner type arguments (between '<' and the matching '>').
        var argsContent = clrTypeName.Substring(angleBracketIndex + 1, clrTypeName.Length - angleBracketIndex - 2);
        var typeArgs = SplitGenericArguments(argsContent);

        // Recursively simplify each type argument and join with "Of" for the first, "And" for subsequent.
        var builder = new System.Text.StringBuilder(outerSimpleName);
        for (var i = 0; i < typeArgs.Count; i++)
        {
            builder.Append(i == 0 ? "Of" : "And");
            builder.Append(SimplifyClrTypeName(typeArgs[i].Trim()));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a comma-separated list of generic type arguments, respecting nested angle brackets
    /// so that <c>Foo&lt;Bar&gt;, Baz</c> correctly splits into two arguments rather than three.
    /// </summary>
    private static List<string> SplitGenericArguments(string argsContent)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < argsContent.Length; i++)
        {
            switch (argsContent[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(argsContent.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        result.Add(argsContent.Substring(start));
        return result;
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
        // Uses single-pass iteration to avoid intermediate List allocation from ToList().
        var hasAnyAuth = false;
        var allRequireAuth = true;

        foreach (var path in document.Paths)
        {
            foreach (var method in path.Methods)
            {
                foreach (var operation in method.Operations)
                {
                    if (operation.RequiresAuthentication)
                    {
                        hasAnyAuth = true;
                    }
                    else
                    {
                        allRequireAuth = false;
                    }
                }
            }
        }

        allRequireAuth = hasAnyAuth && allRequireAuth;

        // Resolve effective security schemes — use configured schemes or default to Bearer JWT
        var effectiveSchemes = infoOptions.SecuritySchemes.Count > 0
            ? infoOptions.SecuritySchemes
            : hasAnyAuth
                ? [OpenApiSecurityScheme.BearerJwt()]
                : [];

        writer.WriteString("openapi", string.IsNullOrWhiteSpace(infoOptions.OpenApiVersion) ? DefaultOpenApiVersion : infoOptions.OpenApiVersion);
        WriteInfo(writer, infoOptions);
        WriteServers(writer, infoOptions.Servers);
        WritePaths(writer, document.Paths, allRequireAuth, effectiveSchemes, infoOptions.SchemaNameFormatter);
        WriteComponents(writer, document.SchemaComponents, schemaProcessorOptions, effectiveSchemes, infoOptions.SchemaNameFormatter);
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

    private static void WritePaths(Utf8JsonWriter writer, ImmutableArray<OpenApiPathItemModel> paths, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter)
    {
        writer.WritePropertyName("paths");
        writer.WriteStartObject();

        foreach (var pathItem in paths)
        {
            WritePathItem(writer, pathItem, useDocumentLevelSecurity, securitySchemes, schemaNameFormatter);
        }

        writer.WriteEndObject();
    }

    private static void WritePathItem(Utf8JsonWriter writer, OpenApiPathItemModel pathItem, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter)
    {
        writer.WritePropertyName(pathItem.UrlFormat);
        writer.WriteStartObject();

        foreach (var methodSlot in pathItem.Methods)
        {
            WriteMethodSlot(writer, methodSlot, useDocumentLevelSecurity, securitySchemes, schemaNameFormatter);
        }

        writer.WriteEndObject();
    }

    private static void WriteMethodSlot(Utf8JsonWriter writer, OpenApiPathItemMethodModel methodSlot, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter)
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
            WriteRequestBody(writer, primaryOperation.OperationName, primaryOperation.RequestBody, schemaNameFormatter);
            WriteResponses(writer, primaryOperation.Responses, schemaNameFormatter);

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
            WriteResponses(writer, ImmutableArray<OpenApiResponseObjectModel>.Empty, schemaNameFormatter);
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

            // Array query/header parameters need style+explode for repeated key serialization
            if (parameter.SchemaType == "array" && parameter.In is "query" or "header")
            {
                writer.WriteString("style", "form");
                writer.WriteBoolean("explode", true);
            }

            writer.WritePropertyName("schema");
            writer.WriteStartObject();
            writer.WriteString("type", parameter.SchemaType);

            if (parameter.SchemaFormat is not null)
            {
                writer.WriteString("format", parameter.SchemaFormat);
            }

            // nullable and required are orthogonal in OpenAPI 3.0: a required parameter
            // can still accept null values, so emit nullable regardless of required status.
            if (parameter.IsNullable)
            {
                writer.WriteBoolean("nullable", true);
            }

            // Array parameters: emit items sub-schema
            if (parameter.ItemsType is not null)
            {
                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WriteString("type", parameter.ItemsType);

                if (parameter.ItemsFormat is not null)
                {
                    writer.WriteString("format", parameter.ItemsFormat);
                }

                writer.WriteEndObject();
            }

            WriteEnumValues(writer, parameter.EnumValues);
            WriteNumericConstraints(writer, parameter.Minimum, parameter.Maximum, parameter.ExclusiveMinimum, parameter.ExclusiveMaximum);

            // For array parameters, [MinLength]/[MaxLength]/[NotEmptyList]/[MaxItemsList] map to
            // minItems/maxItems rather than minLength/maxLength.
            if (string.Equals(parameter.SchemaType, "array", StringComparison.Ordinal))
            {
                WriteArrayItemConstraints(writer, parameter.MinLength, parameter.MaxLength);
            }
            else
            {
                WriteStringConstraints(writer, parameter.MinLength, parameter.MaxLength, parameter.Pattern);
            }

            writer.WriteEndObject();

            if (parameter.ExampleValue is not null)
            {
                WriteExampleValue(writer, parameter.ExampleValue);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRequestBody(Utf8JsonWriter writer, string operationName, OpenApiRequestBodyModel? requestBody, Func<string, string>? schemaNameFormatter)
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

        if (!requestBody.Properties.IsEmpty)
        {
            // Emit a $ref to the corresponding component schema keyed by the operation name.
            // The request body schema component is assembled in OpenApiDocumentRenderingContext.
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(operationName, schemaNameFormatter)}");
        }
        else
        {
            writer.WriteString("type", "object");
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

        // x-enumNames (NSwag convention) and x-enum-varnames (OpenAPI Generator convention)
        // both help TypeScript and other code generators produce named enum members instead of
        // raw string/numeric unions. Emit both for maximum tooling compatibility.
        writer.WritePropertyName("x-enumNames");
        WriteStringArray(writer, enumValues);

        writer.WritePropertyName("x-enum-varnames");
        WriteStringArray(writer, enumValues);
    }

    private static void WriteStringArray(Utf8JsonWriter writer, ImmutableArray<string> values)
    {
        writer.WriteStartArray();

        foreach (var value in values)
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

    private static void WriteResponses(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseObjectModel> responses, Func<string, string>? schemaNameFormatter)
    {
        writer.WritePropertyName("responses");
        writer.WriteStartObject();

        foreach (var response in responses)
        {
            WriteResponse(writer, response, schemaNameFormatter);
        }

        writer.WriteEndObject();
    }

    private static void WriteResponse(Utf8JsonWriter writer, OpenApiResponseObjectModel response, Func<string, string>? schemaNameFormatter)
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
            WriteResponseContent(writer, response.Content, schemaNameFormatter);
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

    private static void WriteResponseContent(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseContentModel> content, Func<string, string>? schemaNameFormatter)
    {
        writer.WritePropertyName("content");
        writer.WriteStartObject();

        foreach (var mediaEntry in content)
        {
            writer.WritePropertyName(mediaEntry.MediaType);
            writer.WriteStartObject();
            writer.WritePropertyName("schema");

            WriteResponseSchema(writer, mediaEntry, schemaNameFormatter);

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseSchema(
        Utf8JsonWriter writer,
        OpenApiResponseContentModel content,
        Func<string, string>? schemaNameFormatter)
    {
        if (content.Discriminator is not null)
        {
            WriteResourceEventDiscriminatedResponseSchema(writer, content.Discriminator, schemaNameFormatter);
            return;
        }

        // ResourceEvent envelope: wrap inner type's $ref in the canonical envelope schema shape
        // so TypeScript generators see the actual wire format (eventId, changeType, data, etc.).
        if (content.IsResourceEventEnvelope && content.SchemaComponents.Length >= 1)
        {
            WriteResourceEventEnvelopeSchema(writer, content.SchemaComponents, schemaNameFormatter);
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
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.Component.BodyTypeName, schemaNameFormatter)}");
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.BodyTypeName, schemaNameFormatter)}");
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
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.Component.BodyTypeName, schemaNameFormatter)}");
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.BodyTypeName, schemaNameFormatter)}");
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

    /// <summary>
    /// Writes a synthetic ResourceEvent envelope schema that wraps the inner resource type(s) in the
    /// canonical envelope shape produced by <c>HttpOperationResultExecutor.ResourceEventEnvelope</c>.
    /// This matches the HTTP wire format so TypeScript generators see the actual JSON structure.
    /// </summary>
    private static void WriteResourceEventEnvelopeSchema(
        Utf8JsonWriter writer,
        ImmutableArray<OpenApiResponseContentSchemaComponent> innerSchemaComponents,
        Func<string, string>? schemaNameFormatter)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");

        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        // $object — always "event"
        writer.WritePropertyName("$object");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        writer.WriteStringValue("event");
        writer.WriteEndArray();
        writer.WriteEndObject();

        // eventId
        writer.WritePropertyName("eventId");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteEndObject();

        // changeType
        writer.WritePropertyName("changeType");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        writer.WriteStringValue("Created");
        writer.WriteStringValue("Updated");
        writer.WriteStringValue("Deleted");
        writer.WriteEndArray();
        writer.WriteEndObject();

        // created
        writer.WritePropertyName("created");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteString("format", "date-time");
        writer.WriteEndObject();

        // resourceObject
        writer.WritePropertyName("resourceObject");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteEndObject();

        // href (nullable)
        writer.WritePropertyName("href");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteBoolean("nullable", true);
        writer.WriteEndObject();

        // data — references the inner type's schema
        writer.WritePropertyName("data");
        if (innerSchemaComponents.Length == 1)
        {
            var inner = innerSchemaComponents[0];
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(inner.BodyTypeName, schemaNameFormatter)}");
            writer.WriteEndObject();
        }
        else
        {
            // Multiple inner types: oneOf
            writer.WriteStartObject();
            writer.WritePropertyName("oneOf");
            writer.WriteStartArray();
            foreach (var inner in innerSchemaComponents)
            {
                writer.WriteStartObject();
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(inner.BodyTypeName, schemaNameFormatter)}");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        // metadata (nullable object)
        WriteNullableObjectProperty(writer, "metadata");

        // secureData (nullable object)
        WriteNullableObjectProperty(writer, "secureData");

        // changedValues (nullable object)
        WriteNullableObjectProperty(writer, "changedValues");

        // correlationId (nullable string)
        writer.WritePropertyName("correlationId");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteBoolean("nullable", true);
        writer.WriteEndObject();

        writer.WriteEndObject(); // end properties

        // required
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("$object");
        writer.WriteStringValue("eventId");
        writer.WriteStringValue("changeType");
        writer.WriteStringValue("created");
        writer.WriteStringValue("resourceObject");
        writer.WriteStringValue("data");
        writer.WriteEndArray();

        writer.WriteBoolean("additionalProperties", false);

        writer.WriteEndObject(); // end schema
    }

    private static void WriteNullableObjectProperty(Utf8JsonWriter writer, string propertyName)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteBoolean("nullable", true);
        writer.WriteBoolean("additionalProperties", true);
        writer.WriteEndObject();
    }

    private static void WriteResourceEventDiscriminatedResponseSchema(
        Utf8JsonWriter writer,
        OpenApiResponseDiscriminatorModel discriminator,
        Func<string, string>? schemaNameFormatter)
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
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(discriminatorCase.SchemaComponent.BodyTypeName, schemaNameFormatter)}");
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
        List<OpenApiSecurityScheme> securitySchemes,
        Func<string, string>? schemaNameFormatter)
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
                WriteSchemaComponent(writer, ProcessSchemaComponent(component, schemaProcessorOptions), schemaNameFormatter);
            }

            // Emit the well-known "Link" schema for HATEOAS $links dictionary values.
            // This enables TypeScript/client generators to produce typed link objects.
            WriteLinkSchemaComponent(writer);

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

    /// <summary>
    /// Writes the well-known <c>Link</c> schema component that describes the shape of HATEOAS link
    /// objects in <c>$links</c> dictionary values. This allows TypeScript and other client generators
    /// to produce typed link objects rather than treating links as opaque <c>object</c>.
    /// </summary>
    private static void WriteLinkSchemaComponent(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("Link");
        writer.WriteStartObject();
        writer.WriteString("title", "Link");
        writer.WriteString("type", "object");

        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("href");
        writer.WriteEndArray();

        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        writer.WritePropertyName("href");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteString("description", "The URL of this link.");
        writer.WriteEndObject();

        writer.WritePropertyName("type");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WriteString("description", "The type of resource this link will return.");
        writer.WriteEndObject();

        writer.WriteEndObject(); // properties
        writer.WriteEndObject(); // Link
    }

    private static void WriteSchemaComponent(Utf8JsonWriter writer, OpenApiSchemaComponentModel component, Func<string, string>? schemaNameFormatter)
    {
        writer.WritePropertyName(SanitiseSchemaKey(component.BodyTypeName, schemaNameFormatter));
        writer.WriteStartObject();

        // Prefer an explicit [DisplayName] title; fall back to the simplified CLR type name
        // (which correctly handles generics via the "Of" convention).
        var shortTitle = component.Title ?? SimplifyClrTypeName(component.BodyTypeName);

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
                WriteSchemaProperty(writer, property, schemaNameFormatter);
            }

            writer.WriteEndObject();

            // Emit additionalProperties: false for object schemas with declared properties, matching
            // NJsonSchema's convention. This prevents consumers (including TypeScript generators)
            // from allowing arbitrary extra properties on typed schema objects.
            writer.WriteBoolean("additionalProperties", false);
        }

        if (!component.Links.IsDefaultOrEmpty)
        {
            writer.WritePropertyName("x-links");
            writer.WriteStartArray();

            foreach (var link in component.Links)
            {
                writer.WriteStartObject();
                writer.WriteString("rel", link.Rel);
                writer.WriteString("operationId", link.OperationId);
                writer.WriteString("method", link.Method);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
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

    private static void WriteSchemaProperty(Utf8JsonWriter writer, OpenApiSchemaComponentPropertyModel property, Func<string, string>? schemaNameFormatter)
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
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.ItemsSchemaRef, schemaNameFormatter)}");
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
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesSchemaRef, schemaNameFormatter)}");
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
            429 => "429",
            500 => "500",
            502 => "502",
            503 => "503",
            504 => "504",
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
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
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
        // Collect all unique tags sorted by name, keeping the first description found for each.
        var tagDescriptions = new SortedDictionary<string, string?>(StringComparer.Ordinal);

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

        writer.WritePropertyName("tags");
        writer.WriteStartArray();

        foreach (var (tag, description) in tagDescriptions)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag);

            if (description is { Length: > 0 })
            {
                writer.WriteString("description", description);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
