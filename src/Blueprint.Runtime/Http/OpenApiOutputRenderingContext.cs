using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Renders the assembled OpenAPI document model into a standards-conformant OpenAPI 3.x JSON string.
/// This seam deliberately stays output-only: it writes the current path and schema inventory without widening
/// into HTTP endpoint exposure, titles, tags, or other post-configuration hooks.
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
    /// <returns>The rendered output context carrying the JSON string.</returns>
    public static OpenApiOutputRenderingContext Create(
        OpenApiDocumentRenderingContext documentRendering,
        OpenApiInfoOptions? infoOptions = null)
    {
        ArgumentNullException.ThrowIfNull(documentRendering);

        var json = RenderJson(documentRendering, infoOptions ?? new OpenApiInfoOptions());
        return new OpenApiOutputRenderingContext(documentRendering, json);
    }

    private static string RenderJson(OpenApiDocumentRenderingContext document, OpenApiInfoOptions infoOptions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });

        WriteDocument(writer, document, infoOptions);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteDocument(Utf8JsonWriter writer, OpenApiDocumentRenderingContext document, OpenApiInfoOptions infoOptions)
    {
        writer.WriteStartObject();

        writer.WriteString("openapi", OpenApiVersion);
        WriteInfo(writer, infoOptions);
        WritePaths(writer, document.Paths);
        WriteComponents(writer, document.SchemaComponents);

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

        writer.WriteEndObject();
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
            WriteResponses(writer, primaryOperation.Responses);
        }
        else
        {
            WriteResponses(writer, ImmutableArray<OpenApiResponseObjectModel>.Empty);
        }

        writer.WriteEndObject();
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

            WriteResponseSchema(writer, mediaEntry.SchemaComponents);

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteResponseSchema(
        Utf8JsonWriter writer,
        ImmutableArray<OpenApiResponseContentSchemaComponent> schemaComponents)
    {
        if (schemaComponents.Length == 1)
        {
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(schemaComponents[0].BodyTypeName)}");
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
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(component.BodyTypeName)}");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    private static void WriteComponents(Utf8JsonWriter writer, ImmutableArray<OpenApiSchemaComponentModel> schemaComponents)
    {
        if (schemaComponents.IsEmpty)
        {
            return;
        }

        writer.WritePropertyName("components");
        writer.WriteStartObject();

        writer.WritePropertyName("schemas");
        writer.WriteStartObject();

        foreach (var component in schemaComponents)
        {
            WriteSchemaComponent(writer, component);
        }

        writer.WriteEndObject();
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

    private static void WriteSchemaProperty(Utf8JsonWriter writer, OpenApiSchemaComponentPropertyModel property)
    {
        writer.WritePropertyName(property.Name);
        writer.WriteStartObject();

        if (property.Type is not null)
        {
            writer.WriteString("type", property.Type);
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
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            422 => "Unprocessable Entity",
            500 => "Internal Server Error",
            _ => statusCode.ToString(),
        };
    }
}
