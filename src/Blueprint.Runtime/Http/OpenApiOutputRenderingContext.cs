using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Blueprint.Model;
using Blueprint.Utilities;

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
    /// The result type name <c>HttpResponseMetadataModule</c> gives the 415 it adds for an operation that reads a body.
    /// </summary>
    private const string UnsupportedMediaTypeResultTypeName = "Blueprint.Runtime.Exec.UnsupportedMediaTypeResult";

    /// <summary>
    /// The result type name <c>HttpResponseMetadataModule</c> gives a response an operation's <c>ApiExceptionFactory</c>
    /// fields contribute.
    /// </summary>
    private const string ApiExceptionResultTypeName = "Blueprint.ApiException";

    /// <summary>
    /// The result type names <c>HttpResponseMetadataModule</c> gives the responses the pipeline itself adds (400 binding
    /// failure, 401 unauthenticated, 403 forbidden, 415 unsupported media type), which legacy never documented and so
    /// never counted when it compared a status's descriptions.
    /// </summary>
    private static readonly FrozenSet<string> SyntheticPipelineResultTypeNames = new[]
    {
        "Blueprint.Runtime.Exec.BindingFailureResult",
        "Blueprint.Runtime.Exec.UnauthenticatedResult",
        "Blueprint.Runtime.Exec.ForbiddenResult",
        UnsupportedMediaTypeResultTypeName,
    }.ToFrozenSet(StringComparer.Ordinal);

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
    /// <param name="httpOptions">
    /// Optional Blueprint HTTP options consulted when no explicit <see cref="OpenApiInfoOptions.Servers"/> are
    /// configured: <see cref="BlueprintHttpOptions.PublicApiHost"/>, as given, populates a single default
    /// <c>servers</c> entry. When <see langword="null"/>, or when it is not set, the <c>servers</c> array is omitted
    /// unless <see cref="OpenApiInfoOptions.Servers"/> already carries entries.
    /// </param>
    /// <returns>The rendered output context carrying the JSON string.</returns>
    public static OpenApiOutputRenderingContext Create(
        OpenApiDocumentRenderingContext documentRendering,
        OpenApiInfoOptions? infoOptions = null,
        OpenApiSchemaProcessorOptions? schemaProcessorOptions = null,
        BlueprintHttpOptions? httpOptions = null)
    {
        return Create(documentRendering, infoOptions, schemaProcessorOptions, httpOptions, basePath: null);
    }

    /// <summary>
    /// Creates a rendered OpenAPI 3.x JSON string from the supplied assembled document model, for an API mapped at
    /// <paramref name="basePath"/>.
    /// </summary>
    /// <param name="documentRendering">The assembled document model to render.</param>
    /// <param name="infoOptions">Optional customisation for the <c>info</c> block.</param>
    /// <param name="schemaProcessorOptions">Optional ordered schema processors for the <c>components/schemas</c> section.</param>
    /// <param name="httpOptions">
    /// Optional Blueprint HTTP options consulted when no explicit <see cref="OpenApiInfoOptions.Servers"/> are
    /// configured: the default <c>servers</c> entry is <see cref="BlueprintHttpOptions.PublicApiHost"/> followed by
    /// <paramref name="basePath"/>.
    /// </param>
    /// <param name="basePath">
    /// The base path the API's routes are mapped under, which the default <c>servers</c> entry ends with, as the
    /// document's <c>paths</c> are relative to it. <see langword="null"/> when unknown, leaving the entry as
    /// <see cref="BlueprintHttpOptions.PublicApiHost"/> alone.
    /// </param>
    /// <returns>The rendered output context carrying the JSON string.</returns>
    public static OpenApiOutputRenderingContext Create(
        OpenApiDocumentRenderingContext documentRendering,
        OpenApiInfoOptions? infoOptions,
        OpenApiSchemaProcessorOptions? schemaProcessorOptions,
        BlueprintHttpOptions? httpOptions,
        string? basePath)
    {
        ArgumentNullException.ThrowIfNull(documentRendering);

        var json = RenderJson(
            documentRendering,
            infoOptions ?? new OpenApiInfoOptions(),
            schemaProcessorOptions ?? new OpenApiSchemaProcessorOptions(),
            httpOptions,
            basePath);
        return new OpenApiOutputRenderingContext(documentRendering, json);
    }

    private static string RenderJson(
        OpenApiDocumentRenderingContext document,
        OpenApiInfoOptions infoOptions,
        OpenApiSchemaProcessorOptions schemaProcessorOptions,
        BlueprintHttpOptions? httpOptions,
        string? basePath)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });

        WriteDocument(writer, document, infoOptions, schemaProcessorOptions, httpOptions, basePath);
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteDocument(
        Utf8JsonWriter writer,
        OpenApiDocumentRenderingContext document,
        OpenApiInfoOptions infoOptions,
        OpenApiSchemaProcessorOptions schemaProcessorOptions,
        BlueprintHttpOptions? httpOptions,
        string? basePath)
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

        // Under legacy naming the formatter is handed the name that naming gives each component, whatever form the key
        // is carried in here (a type's name, a request body's, a ResourceEvent envelope's synthetic CLR name), so it only
        // ever sees names and can reproduce a consumer's legacy keys. The default hands it the key as it always has.
        var schemaNameFormatter = infoOptions.SchemaNaming == OpenApiSchemaNaming.Legacy && infoOptions.SchemaNameFormatter is { } formatter
            ? name => formatter(SimplifyClrTypeName(name))
            : infoOptions.SchemaNameFormatter;

        // Every component and every reference to one is written under the key it is assigned here, which no other
        // component shares.
        var componentKeys = AssignComponentKeys(document, schemaNameFormatter);
        string ComponentKeyOf(string name) => componentKeys.TryGetValue(name, out var key) ? key : SanitiseSchemaKey(name, schemaNameFormatter);

        writer.WriteString("openapi", string.IsNullOrWhiteSpace(infoOptions.OpenApiVersion) ? DefaultOpenApiVersion : infoOptions.OpenApiVersion);
        WriteInfo(writer, infoOptions);
        WriteServers(writer, ResolveServers(infoOptions.Servers, httpOptions, basePath));

        // A parameter's title is a schema name, passed through the consumer's formatter as legacy's schema name generator
        // passed it, but not a component key: no counter, no sanitising.
        var parameterTitleFormatter = infoOptions.SchemaNaming == OpenApiSchemaNaming.Legacy ? infoOptions.SchemaNameFormatter : null;

        // Legacy's NJsonSchema-based generator never wrote a schema component's "title" and Newtonsoft's contract
        // resolution never tracked "readOnly" (checked against every Signature365 8b40ecc961 app's committed
        // Spec/openapi.json: zero of either, across all 21 documents) — omit both only under Legacy naming; a Default
        // consumer keeps them, since they are real facts legacy simply had no means to describe. Nor did legacy write a
        // request body's "required" (every request body in the Portal document carries "content" alone), which is
        // omitted on the same terms.
        var omitLegacyExtras = infoOptions.SchemaNaming == OpenApiSchemaNaming.Legacy;

        WritePaths(writer, document.Paths, LegacyOperationIds.Assign(document), allRequireAuth, effectiveSchemes, ComponentKeyOf, parameterTitleFormatter, omitLegacyExtras);
        WriteComponents(writer, document.SchemaComponents, schemaProcessorOptions, effectiveSchemes, ComponentKeyOf, DescribeComponentSources(document), omitLegacyExtras);
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

    /// <summary>
    /// Resolves the effective <c>servers</c> list: the explicitly configured entries when there are any, and otherwise
    /// the single gateway-reachable address in <see cref="BlueprintHttpOptions.PublicApiHost"/>, followed by the base
    /// path the API is mapped under when that is known — as legacy Blueprint composed it, since the document's paths
    /// are relative to the server and do not carry the base path themselves. There is no tier below
    /// that — when <c>PublicApiHost</c> is unset the <c>servers</c> array stays empty (omitted by
    /// <see cref="WriteServers"/>), and the incoming request's origin is never consulted because the document is
    /// rendered once at registration (<see cref="OpenApiDocumentEndpoint"/>), before any request exists.
    /// </summary>
    /// <remarks>
    /// <see cref="BlueprintHttpOptions.PublicHost"/> is deliberately <em>not</em> a fallback. It is a bare host that
    /// link generation composes a scheme and the request's <c>PathBase</c> around separately, whereas a <c>servers</c>
    /// entry is a complete address including any mount prefix. Treating it as one would make an app that sets only
    /// <c>PublicHost</c> — an option that predates this and is set for link generation — silently advertise a
    /// prefix-less URL its API does not answer on.
    /// </remarks>
    private static List<OpenApiServerEntry> ResolveServers(List<OpenApiServerEntry> configuredServers, BlueprintHttpOptions? httpOptions, string? basePath)
    {
        if (configuredServers.Count > 0)
        {
            return configuredServers;
        }

        var publicApiHost = httpOptions?.PublicApiHost;
        if (string.IsNullOrWhiteSpace(publicApiHost))
        {
            return configuredServers;
        }

        // The configured host may already carry a scheme (e.g. copied from a full URL); avoid doubling it up.
        var url = publicApiHost.Contains("://", StringComparison.Ordinal) ? publicApiHost : $"https://{publicApiHost}";

        if (basePath is not null)
        {
            // Legacy's "{PublicApiHost}/{basePath}", which ends with a slash, the base path having one.
            var trimmedBasePath = basePath.Trim('/');
            url = url.TrimEnd('/') + "/" + (trimmedBasePath.Length > 0 ? trimmedBasePath + "/" : string.Empty);
        }

        return [new OpenApiServerEntry { Url = url }];
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

    private static void WritePaths(Utf8JsonWriter writer, ImmutableArray<OpenApiPathItemModel> paths, IReadOnlyDictionary<(string UrlFormat, string Method), string> operationIds, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter, Func<string, string>? parameterTitleFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName("paths");
        writer.WriteStartObject();

        foreach (var pathItem in paths)
        {
            WritePathItem(writer, pathItem, operationIds, useDocumentLevelSecurity, securitySchemes, schemaNameFormatter, parameterTitleFormatter, omitLegacyExtras);
        }

        writer.WriteEndObject();
    }

    private static void WritePathItem(Utf8JsonWriter writer, OpenApiPathItemModel pathItem, IReadOnlyDictionary<(string UrlFormat, string Method), string> operationIds, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter, Func<string, string>? parameterTitleFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName(pathItem.UrlFormat);
        writer.WriteStartObject();

        foreach (var methodSlot in pathItem.Methods)
        {
            WriteMethodSlot(writer, methodSlot, operationIds.GetValueOrDefault((pathItem.UrlFormat, methodSlot.HttpMethod.ToUpperInvariant())), useDocumentLevelSecurity, securitySchemes, schemaNameFormatter, parameterTitleFormatter, omitLegacyExtras);
        }

        writer.WriteEndObject();
    }

    private static void WriteMethodSlot(Utf8JsonWriter writer, OpenApiPathItemMethodModel methodSlot, string? operationId, bool useDocumentLevelSecurity, List<OpenApiSecurityScheme> securitySchemes, Func<string, string>? schemaNameFormatter, Func<string, string>? parameterTitleFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName(methodSlot.HttpMethod.ToLowerInvariant());
        writer.WriteStartObject();

        // When multiple operations share a method slot, use the first alphabetically. Its operationId is its name, made
        // unique across the document as legacy's was (LegacyOperationIds).
        var primaryOperation = methodSlot.Operations.IsEmpty ? null : methodSlot.Operations[0];
        if (primaryOperation is not null)
        {
            writer.WriteString("operationId", operationId ?? primaryOperation.OperationName);

            if (primaryOperation.Summary is not null)
            {
                writer.WriteString("summary", primaryOperation.Summary);
            }

            if (primaryOperation.Description is not null)
            {
                writer.WriteString("description", primaryOperation.Description);
            }

            WriteTags(writer, primaryOperation.Tags);
            WriteParameters(writer, primaryOperation.Parameters, parameterTitleFormatter, schemaNameFormatter, omitLegacyExtras);
            WriteRequestBody(writer, primaryOperation.OperationName, primaryOperation.RequestBody, schemaNameFormatter, omitLegacyExtras);
            WriteResponses(writer, primaryOperation.Responses, schemaNameFormatter, omitLegacyExtras);

            if (primaryOperation.Deprecated)
            {
                writer.WriteBoolean("deprecated", true);

                if (primaryOperation.DeprecationReason is not null)
                {
                    writer.WriteString(DeprecationMessageKey(omitLegacyExtras), primaryOperation.DeprecationReason);
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
            WriteResponses(writer, ImmutableArray<OpenApiResponseObjectModel>.Empty, schemaNameFormatter, omitLegacyExtras);
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

    private static void WriteParameters(Utf8JsonWriter writer, ImmutableArray<OpenApiParameterModel> parameters, Func<string, string>? titleFormatter, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
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

            // Legacy wrote a parameter's "required" only when true (550 of its 734 in Signature365's documents, the rest none;
            // never false), and nothing of style, explode or a schema's nullable (R159): omitted under Legacy naming.
            if (!omitLegacyExtras || parameter.Required)
            {
                writer.WriteBoolean("required", parameter.Required);
            }

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
                    writer.WriteString(DeprecationMessageKey(omitLegacyExtras), parameter.DeprecationReason);
                }
            }

            // Array query/header parameters need style+explode for repeated key serialization
            if (!omitLegacyExtras && parameter.SchemaType == "array" && parameter.In is "query" or "header")
            {
                writer.WriteString("style", "form");
                writer.WriteBoolean("explode", true);
            }

            writer.WritePropertyName("schema");
            writer.WriteStartObject();

            if (parameter.SchemaTitle is { } title)
            {
                writer.WriteString("title", titleFormatter is null ? title : titleFormatter(title));
            }

            writer.WriteString("type", parameter.SchemaType);

            if (parameter.SchemaFormat is not null)
            {
                writer.WriteString("format", parameter.SchemaFormat);
            }

            // nullable and required are orthogonal in OpenAPI 3.0: a required parameter
            // can still accept null values, so emit nullable regardless of required status.
            if (parameter.IsNullable && !omitLegacyExtras)
            {
                writer.WriteBoolean("nullable", true);
            }

            if (parameter.EnumDescription is not null)
            {
                writer.WriteString("description", parameter.EnumDescription);
            }

            // Array parameters: emit items sub-schema
            if (parameter.ItemsType is not null)
            {
                writer.WritePropertyName("items");
                writer.WriteStartObject();

                if (parameter.ItemsEnumSchemaRef is not null)
                {
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(parameter.ItemsEnumSchemaRef, schemaNameFormatter)}");
                }
                else
                {
                    writer.WriteString("type", parameter.ItemsType);

                    if (parameter.ItemsFormat is not null)
                    {
                        writer.WriteString("format", parameter.ItemsFormat);
                    }

                    if (!parameter.ItemsEnumValues.IsDefaultOrEmpty)
                    {
                        WriteEnumValues(writer, parameter.ItemsEnumValues);
                    }
                }

                writer.WriteEndObject();
            }

            WriteEnumValues(writer, parameter.EnumValues, includeVarNames: !omitLegacyExtras);
            WriteNumericConstraints(writer, parameter.SchemaType, parameter.Minimum, parameter.Maximum, parameter.ExclusiveMinimum, parameter.ExclusiveMaximum);

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

    private static void WriteRequestBody(Utf8JsonWriter writer, string operationName, OpenApiRequestBodyModel? requestBody, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
    {
        if (requestBody is null)
        {
            return;
        }

        writer.WritePropertyName("requestBody");
        writer.WriteStartObject();

        // Legacy never wrote a request body's "required" (see the omitLegacyExtras note in WriteDocument).
        if (!omitLegacyExtras)
        {
            writer.WriteBoolean("required", requestBody.Required);
        }

        writer.WritePropertyName("content");
        writer.WriteStartObject();
        writer.WritePropertyName("application/json");
        writer.WriteStartObject();

        writer.WritePropertyName("schema");
        writer.WriteStartObject();

        if (!requestBody.Properties.IsEmpty)
        {
            // Emit a $ref to the corresponding component schema, keyed by the body's schema name or else the operation name.
            // The request body schema component is assembled in OpenApiDocumentRenderingContext.
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(requestBody.SchemaName ?? operationName, schemaNameFormatter)}");
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

    private static void WriteEnumValues(Utf8JsonWriter writer, ImmutableArray<string> enumValues, bool includeVarNames = true)
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

        if (includeVarNames)
        {
            writer.WritePropertyName("x-enum-varnames");
            WriteStringArray(writer, enumValues);
        }
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

    /// <summary>
    /// Writes a numeric schema's <c>minimum</c> and <c>maximum</c>, and never an exclusive keyword (R147b): OpenAPI 3.0's
    /// <c>exclusiveMinimum</c> is a boolean beside <c>minimum</c>, draft-07's (which a generated client's AJV reads) is the
    /// bound itself, and no document is valid in both. An exclusive bound is written in the form both accept: an integer's
    /// as the next integer (greater than 0 is at least 1, exactly), a number's as the inclusive bound, which rejects no value
    /// the server accepts and merely admits the bound itself.
    /// </summary>
    private static void WriteNumericConstraints(Utf8JsonWriter writer, string? schemaType, decimal? minimum, decimal? maximum, bool exclusiveMinimum, bool exclusiveMaximum)
    {
        var isInteger = string.Equals(schemaType, "integer", StringComparison.Ordinal);

        if (minimum.HasValue)
        {
            writer.WriteNumber("minimum", exclusiveMinimum && isInteger ? minimum.Value + 1 : minimum.Value);
        }

        if (maximum.HasValue)
        {
            writer.WriteNumber("maximum", exclusiveMaximum && isInteger ? maximum.Value - 1 : maximum.Value);
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

    private static void WriteResponses(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseObjectModel> responses, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName("responses");
        writer.WriteStartObject();

        foreach (var response in responses)
        {
            // Legacy never documented a 415 (none of the 21 documents it wrote for Signature365 at 8b40ecc961 has one), nor
            // a 400, 401 or 403 of the pipeline's own (R156: only an ApiException factory's), so those the pipeline adds are
            // left out under Legacy naming. A response an operation declares for itself, through a result type of its own,
            // or one a factory contributes to, is still written.
            if (omitLegacyExtras && IsSyntheticPipelineResponse(response))
            {
                continue;
            }

            WriteResponse(writer, response, schemaNameFormatter, omitLegacyExtras);
        }

        writer.WriteEndObject();
    }

    private static bool IsSyntheticPipelineResponse(OpenApiResponseObjectModel response)
    {
        return !response.ResultTypeNames.IsDefaultOrEmpty
            && response.ResultTypeNames.All(static name => SyntheticPipelineResultTypeNames.Contains(name));
    }

    private static void WriteResponse(Utf8JsonWriter writer, OpenApiResponseObjectModel response, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName(MapStatusCodeString(response.StatusCode));
        writer.WriteStartObject();

        // Legacy documented an operation's ApiExceptionFactory fields (see WriteLegacyProblemExample); a Default consumer
        // keeps the status's own description and no example map, which is not the shape OpenAPI defines for "example".
        var legacyProblemExamples = omitLegacyExtras && !response.ProblemExamples.IsDefaultOrEmpty;

        var description = legacyProblemExamples
            ? DescribeLegacyProblemResponse(response)
            : omitLegacyExtras ? MapLegacyStatusDescription(response.StatusCode) : MapStatusDescription(response.StatusCode);

        if (description is null)
        {
            writer.WriteNull("description");
        }
        else
        {
            writer.WriteString("description", description);
        }

        if (!response.Headers.IsDefault && !response.Headers.IsEmpty)
        {
            WriteResponseHeaders(writer, response.Headers);
        }

        if (!response.Content.IsEmpty)
        {
            WriteResponseContent(writer, response.Content, schemaNameFormatter, legacyProblemExamples ? response : null);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Legacy's description of a response carrying <c>ApiExceptionFactory</c> fields: the factory's title when it is the
    /// only description the status had, else <see langword="null"/>. Legacy nulled a status's description once two of its
    /// responses disagreed (Signature365 8b40ecc961, <c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:196-201</c>), and
    /// its documents carry that as JSON <c>null</c> (93 responses, none an empty string). Every operation also had
    /// legacy's "Unexpected error" 500 and a validating one its "Validation failure" 422, so a factory at a status
    /// another result type shares is described as <see langword="null"/>. The responses the pipeline itself adds
    /// (<see cref="SyntheticPipelineResultTypeNames"/>) are not counted: legacy never documented them, and described 89
    /// single-factory 400s, 10 403s and 2 401s by their title.
    /// </summary>
    private static string? DescribeLegacyProblemResponse(OpenApiResponseObjectModel response)
    {
        var onlyFactories = response.ResultTypeNames
            .Where(static name => !SyntheticPipelineResultTypeNames.Contains(name))
            .All(static name => name == ApiExceptionResultTypeName);
        var titles = response.ProblemExamples.Select(static example => example.Title).Distinct(StringComparer.Ordinal).ToArray();

        return onlyFactories && titles.Length == 1 ? titles[0] : null;
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

    private static void WriteResponseContent(Utf8JsonWriter writer, ImmutableArray<OpenApiResponseContentModel> content, Func<string, string>? schemaNameFormatter, OpenApiResponseObjectModel? legacyProblemResponse)
    {
        writer.WritePropertyName("content");
        writer.WriteStartObject();

        foreach (var mediaEntry in content)
        {
            writer.WritePropertyName(mediaEntry.MediaType);
            writer.WriteStartObject();
            writer.WritePropertyName("schema");

            WriteResponseSchema(writer, mediaEntry, schemaNameFormatter);

            if (legacyProblemResponse is not null)
            {
                WriteLegacyProblemExample(writer, legacyProblemResponse);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes legacy's map of an operation's <c>ApiExceptionFactory</c> fields under the singular "example" key:
    /// <c>{"MissingPartnerCompany":{"value":{"status":404,"title":"...","type":"missing_partner_company"}}}</c>, keyed by the
    /// type in PascalCase (Signature365 8b40ecc961, <c>Blueprint.OpenApi/OpenApiDocumentBuilder.cs:246-262</c>; a later
    /// factory with the same type replaces an earlier one, as legacy's dictionary did). Signature365's client generator
    /// reads this map, not the standard "examples", to build each API's error catalogue.
    /// </summary>
    private static void WriteLegacyProblemExample(Utf8JsonWriter writer, OpenApiResponseObjectModel response)
    {
        var examples = new Dictionary<string, OperationProblemExample>(StringComparer.Ordinal);
        var keys = new List<string>();

        foreach (var example in response.ProblemExamples)
        {
            var key = example.Type.ToPascalCase();
            if (!examples.ContainsKey(key))
            {
                keys.Add(key);
            }

            examples[key] = example;
        }

        writer.WritePropertyName("example");
        writer.WriteStartObject();

        foreach (var key in keys)
        {
            var example = examples[key];

            writer.WritePropertyName(key);
            writer.WriteStartObject();
            writer.WritePropertyName("value");
            writer.WriteStartObject();
            writer.WriteNumber("status", response.StatusCode);
            writer.WriteString("title", example.Title);
            writer.WriteString("type", example.Type);
            writer.WriteEndObject();
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

        // ResourceEvent envelope: reference the named component schema (e.g. ResourceEventOfTemplateApiResource)
        // instead of inlining the envelope shape. The named component was created by
        // OpenApiDocumentRenderingContext.CollectResourceEventEnvelopeComponents().
        if (content.IsResourceEventEnvelope && content.SchemaComponents.Length >= 1)
        {
            var innerBodyTypeName = content.SchemaComponents[0].BodyTypeName;
            var envelopeBodyTypeName = OpenApiDocumentRenderingContext.BuildResourceEventEnvelopeBodyTypeName(innerBodyTypeName);

            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(envelopeBodyTypeName, schemaNameFormatter)}");
            writer.WriteEndObject();
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
            if (string.Equals(discriminator.PropertyName, OpenApiResponseObjectRenderingContext.ResourceObjectDiscriminatorPropertyName, StringComparison.Ordinal))
            {
                WriteLegacyResourceEventDiscriminatorCase(writer, discriminatorCase, schemaNameFormatter);
                continue;
            }

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

    /// <summary>
    /// Writes one case of a response made only of legacy resource events: the event the host writes for it, by the
    /// <c>resourceObject</c> that tells it apart and the <c>data</c> it carries, both of which the event always has
    /// (<c>ResourceEventOf{Resource}</c>'s required members).
    /// </summary>
    private static void WriteLegacyResourceEventDiscriminatorCase(
        Utf8JsonWriter writer,
        OpenApiResponseDiscriminatorCaseModel discriminatorCase,
        Func<string, string>? schemaNameFormatter)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue("resourceObject");
        writer.WriteStringValue("data");
        writer.WriteEndArray();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();

        writer.WritePropertyName("resourceObject");
        writer.WriteStartObject();
        writer.WriteString("type", "string");
        writer.WritePropertyName("enum");
        writer.WriteStartArray();
        writer.WriteStringValue(discriminatorCase.Value);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WritePropertyName("data");
        writer.WriteStartObject();
        writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(discriminatorCase.SchemaComponent.BodyTypeName, schemaNameFormatter)}");
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Gives each schema component the key it is written under, as legacy Blueprint's NJsonSchema gave it: the first
    /// component the document reaches keeps its name, and each later one whose name is taken is given it with the first
    /// free counter from 2 appended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Legacy reached schemas as it walked the document — an operation's request body, then its responses, and each type
    /// before the types its properties reach — so this walks the operations in the order they are written, and a
    /// component's references in the order its properties are. A component nothing reaches comes after, in the order it
    /// is written. The name counted on is the one the schema projection would have given the type had no other type
    /// shared it (<see cref="OpenApiSchemaProjectionContext.BaseNames"/>), through the formatter: two types can come to
    /// share a key either way, and legacy counted on from the one name whichever way it was.
    /// </para>
    /// <para>
    /// The well-known <c>Link</c> component is written under its own name, so no other takes it. A component itself
    /// named <c>Link</c> is not assigned a key: <see cref="EnsureUniqueComponentKey"/> rejects it rather than let it take
    /// the references every <c>$links</c> property makes to <c>Link</c>.
    /// </para>
    /// </remarks>
    private static Dictionary<string, string> AssignComponentKeys(OpenApiDocumentRenderingContext document, Func<string, string>? schemaNameFormatter)
    {
        var baseNames = document.PathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.SchemaProjection.BaseNames;
        var componentsByName = new Dictionary<string, OpenApiSchemaComponentModel>(StringComparer.Ordinal);

        foreach (var component in document.SchemaComponents)
        {
            componentsByName.TryAdd(component.BodyTypeName, component);
        }

        componentsByName.Remove("Link");

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var takenKeys = new HashSet<string>(StringComparer.Ordinal) { "Link" };

        foreach (var path in document.Paths)
        {
            foreach (var method in path.Methods)
            {
                if (method.Operations.IsEmpty)
                {
                    continue;
                }

                // The operation written for the method slot, as WriteMethodSlot writes it.
                var operation = method.Operations[0];

                if (operation.RequestBody is { Properties.IsEmpty: false } requestBody)
                {
                    Reach(requestBody.SchemaName ?? operation.OperationName);
                }
                else if (operation.BodylessRequestSchemaName is { } bodylessSchemaName)
                {
                    Reach(bodylessSchemaName);
                }

                foreach (var response in operation.Responses)
                {
                    foreach (var content in response.Content)
                    {
                        ReachResponseContent(content);
                    }
                }
            }
        }

        foreach (var component in document.SchemaComponents)
        {
            Reach(component.BodyTypeName);
        }

        return keys;

        // The components a response's content references, as WriteResponseSchema writes them.
        void ReachResponseContent(OpenApiResponseContentModel content)
        {
            if (content.Discriminator is not null)
            {
                foreach (var discriminatorCase in content.Discriminator.Cases)
                {
                    Reach(discriminatorCase.SchemaComponent.BodyTypeName);
                }
            }
            else if (content.IsResourceEventEnvelope && content.SchemaComponents.Length >= 1)
            {
                ReachResourceEventEnvelope(content.SchemaComponents[0].BodyTypeName);
            }
            else
            {
                foreach (var component in content.SchemaComponents)
                {
                    Reach(component.IsArrayWrapper ? component.Component.BodyTypeName : component.BodyTypeName);
                }
            }
        }

        // A ResourceEvent envelope's own key must be built from the inner type's *final* key — the one its "data"
        // property's $ref actually resolves to — never from the inner's projection-time name embedded verbatim in the
        // envelope's synthetic BodyTypeName. The two can differ: the projection's own walk can give one type of a
        // shared simple name the base name and the other "…2", while this reach-order walk (over responses, in
        // document order) can visit them in the other order and swap which one keeps the base name. Reaching the inner
        // type first — through the ordinary Reach(), which every other reference already goes through — and reading
        // back its resolved key is what keeps the envelope's name and its "data" $ref pointing at the same component
        // (R28: without this, "ResourceEventOfX2" could wrap the component actually written under key "X").
        void ReachResourceEventEnvelope(string innerRawName)
        {
            var envelopeRawName = OpenApiDocumentRenderingContext.BuildResourceEventEnvelopeBodyTypeName(innerRawName);

            if (keys.ContainsKey(envelopeRawName) || !componentsByName.TryGetValue(envelopeRawName, out var envelopeComponent))
            {
                return;
            }

            Reach(innerRawName);

            var innerFinalKey = keys.GetValueOrDefault(innerRawName)
                ?? SanitiseSchemaKey(baseNames.GetValueOrDefault(innerRawName, innerRawName), schemaNameFormatter);

            var key = "ResourceEventOf" + innerFinalKey;
            var uniqueKey = key;

            for (var counter = 2; !takenKeys.Add(uniqueKey); counter++)
            {
                uniqueKey = key + counter.ToString(CultureInfo.InvariantCulture);
            }

            keys[envelopeRawName] = uniqueKey;

            foreach (var property in envelopeComponent.Properties)
            {
                Reach(property.SchemaRef);
                Reach(property.EnumSchemaRef);
                Reach(property.ItemsSchemaRef);
                Reach(property.AdditionalPropertiesSchemaRef);
                Reach(property.AdditionalPropertiesItemsSchemaRef);
            }
        }

        void Reach(string? name)
        {
            if (name is null || keys.ContainsKey(name) || !componentsByName.TryGetValue(name, out var component))
            {
                return;
            }

            var key = SanitiseSchemaKey(baseNames.GetValueOrDefault(name, name), schemaNameFormatter);
            var uniqueKey = key;

            for (var counter = 2; !takenKeys.Add(uniqueKey); counter++)
            {
                uniqueKey = key + counter.ToString(CultureInfo.InvariantCulture);
            }

            keys[name] = uniqueKey;

            foreach (var property in component.Properties)
            {
                Reach(property.SchemaRef);
                Reach(property.EnumSchemaRef);
                Reach(property.ItemsSchemaRef);
                Reach(property.AdditionalPropertiesSchemaRef);
                Reach(property.AdditionalPropertiesItemsSchemaRef);
            }
        }
    }

    /// <summary>
    /// Maps each schema component's name to the CLR type it was projected from, where it was, so that a clash between
    /// two components can name the types involved rather than only the names they share.
    /// </summary>
    private static IReadOnlyDictionary<string, string> DescribeComponentSources(OpenApiDocumentRenderingContext document)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        // Both a type's backtick and angle-bracket full names map to its name; the angle-bracket one reads better.
        foreach (var (fullName, name) in document.PathItemRendering.OperationRendering.ResponseObjects.SchemaComponents.SchemaProjection.FullNameToShortName
                     .OrderByDescending(static pair => pair.Key.Contains('<', StringComparison.Ordinal)))
        {
            sources.TryAdd(name, fullName);
        }

        return sources;
    }

    private static void WriteComponents(
        Utf8JsonWriter writer,
        ImmutableArray<OpenApiSchemaComponentModel> schemaComponents,
        OpenApiSchemaProcessorOptions schemaProcessorOptions,
        List<OpenApiSecurityScheme> securitySchemes,
        Func<string, string>? schemaNameFormatter,
        IReadOnlyDictionary<string, string> componentSources,
        bool omitLegacyExtras)
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

            // Utf8JsonWriter writes a duplicate property name without complaint, and a parser then silently keeps one of
            // the two schemas. AssignComponentKeys gives two types that share a name keys of their own, so what can still
            // render to one key — two components under one name, such as a request body and a type both named for the one
            // CLR type name — must fail here, naming both.
            var sourcesByKey = new Dictionary<string, string>(StringComparer.Ordinal);

            var processedComponents = objectSchemaComponents
                .Select(component => ProcessSchemaComponent(component, schemaProcessorOptions))
                .ToList();

            foreach (var processed in processedComponents)
            {
                EnsureUniqueComponentKey(
                    sourcesByKey,
                    SanitiseSchemaKey(processed.BodyTypeName, schemaNameFormatter),
                    componentSources.GetValueOrDefault(processed.BodyTypeName, processed.BodyTypeName));
                WriteSchemaComponent(writer, processed, schemaNameFormatter, omitLegacyExtras);
            }

            // Emit the well-known "Link" schema for HATEOAS $links dictionary values.
            // This enables TypeScript/client generators to produce typed link objects. Legacy's NJsonSchema wrote a component
            // only when something reached it, so under Legacy naming it is written only when a component references it.
            if (!omitLegacyExtras || processedComponents.Any(static component => ReferencesLinkComponent(component)))
            {
                EnsureUniqueComponentKey(sourcesByKey, "Link", "Blueprint.Model.Link");
                WriteLinkSchemaComponent(writer, omitLegacyExtras);
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

    private static bool ReferencesLinkComponent(OpenApiSchemaComponentModel component)
    {
        return component.Properties.Any(static property => string.Equals(property.AdditionalPropertiesSchemaRef, "Link", StringComparison.Ordinal));
    }

    private static void EnsureUniqueComponentKey(Dictionary<string, string> sourcesByKey, string key, string source)
    {
        if (!sourcesByKey.TryAdd(key, source))
        {
            throw new InvalidOperationException(
                $"The OpenAPI schema components for '{sourcesByKey[key]}' and '{source}' are both named '{key}', and a document " +
                "cannot hold both under one key. Rename one of the types, give one a name of its own with [OpenApiSchemaName], " +
                "or change the SchemaNameFormatter that maps them together.");
        }
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
    private static void WriteLinkSchemaComponent(Utf8JsonWriter writer, bool omitLegacyExtras)
    {
        writer.WritePropertyName("Link");
        writer.WriteStartObject();

        // Legacy never wrote a schema component's title, this one's included (see WriteSchemaComponent).
        if (!omitLegacyExtras)
        {
            writer.WriteString("title", "Link");
        }

        // Described by Link's documentation, as legacy described every schema.
        if (OpenApiXmlDocs.GetSchemaSummary(typeof(Blueprint.Model.Link)) is { } description)
        {
            writer.WriteString("description", description);
        }

        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

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
        writer.WriteBoolean("nullable", true);
        writer.WriteString("description", "The type of resource this link will return.");
        writer.WriteEndObject();

        writer.WriteEndObject(); // properties
        writer.WriteEndObject(); // Link
    }

    private static void WriteSchemaComponent(Utf8JsonWriter writer, OpenApiSchemaComponentModel component, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName(SanitiseSchemaKey(component.BodyTypeName, schemaNameFormatter));
        writer.WriteStartObject();

        // Enum components are rendered as a flat type: string schema with enum/x-enumNames arrays
        // instead of the object schema shape used for regular body types.
        if (!component.ComponentEnumValues.IsDefaultOrEmpty)
        {
            writer.WriteString("type", "string");

            // Legacy's NJsonSchema wrote an enum component's description whether or not the enum had one, the empty string
            // for one without (17 of Signature365's 192 enum components).
            if (component.Description is not null || omitLegacyExtras)
            {
                writer.WriteString("description", component.Description ?? string.Empty);
            }

            WriteEnumValues(writer, component.ComponentEnumValues, includeVarNames: !omitLegacyExtras);
            writer.WriteEndObject();
            return;
        }

        // Prefer an explicit [DisplayName] title; fall back to the simplified CLR type name
        // (which correctly handles generics via the "Of" convention). Legacy's NJsonSchema never wrote a component's
        // title at all, so this is omitted under Legacy naming.
        if (!omitLegacyExtras)
        {
            var shortTitle = component.Title ?? SimplifyClrTypeName(component.BodyTypeName);

            writer.WriteString("title", shortTitle);
        }

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
                WriteSchemaProperty(writer, property, schemaNameFormatter, omitLegacyExtras);
            }

            writer.WriteEndObject();

            // Emit additionalProperties: false for object schemas with declared properties, matching
            // NJsonSchema's convention. This prevents consumers (including TypeScript generators)
            // from allowing arbitrary extra properties on typed schema objects.
            // Exception: types with [JsonExtensionData] properties (e.g. ProblemDetails) accept
            // arbitrary additional members per their contract, so additionalProperties is open to any value, or null
            // too, as legacy documented it.
            if (!component.HasExtensionData)
            {
                writer.WriteBoolean("additionalProperties", false);
            }
            else
            {
                writer.WritePropertyName("additionalProperties");
                writer.WriteStartObject();

                if (component.AdditionalPropertiesAreNullable)
                {
                    writer.WriteBoolean("nullable", true);
                }

                writer.WriteEndObject();
            }
        }
        else if (component.IsBodylessOperation)
        {
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

        if (!component.Conditionals.IsDefaultOrEmpty)
        {
            writer.WritePropertyName("allOf");
            writer.WriteStartArray();

            foreach (var conditional in component.Conditionals)
            {
                WriteConditionalRequirement(writer, conditional, omitLegacyExtras);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one cross-property conditional requirement as a JSON Schema <c>if</c>/<c>then</c> pair.
    /// </summary>
    /// <remarks>
    /// The <c>if</c> always requires the dependent property to be present, so a payload omitting it never activates the
    /// <c>then</c>. That under-enforces relative to the server, which sees the CLR default rather than an absence, and
    /// never over-enforces — a generated client validator that blocks a payload the server would accept is a worse
    /// failure than one the server still rejects.
    /// </remarks>
    private static void WriteConditionalRequirement(Utf8JsonWriter writer, OpenApiConditionalRequirement conditional, bool omitLegacyExtras)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("if");
        writer.WriteStartObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue(conditional.DependentPropertyName);
        writer.WriteEndArray();
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WritePropertyName(conditional.DependentPropertyName);
        WriteDependentValueCondition(writer, conditional);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("then");
        writer.WriteStartObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteStringValue(conditional.TargetPropertyName);
        writer.WriteEndArray();

        // "required" alone carries the whole of a NonNull requirement, so that kind contributes no property constraint
        // and the properties block is omitted rather than filled with an empty schema.
        // Legacy wrote "not": { "type": "null" } for it all the same (R162).
        if (conditional.TargetKind != OpenApiConditionalTargetKind.NonNull || omitLegacyExtras)
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WritePropertyName(conditional.TargetPropertyName);
            WriteConditionalTargetConstraint(writer, conditional.TargetKind, omitLegacyExtras);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the dependent-property test for a conditional: <c>const</c> for a single value, <c>enum</c> for several,
    /// each wrapped in <c>not</c> for the negated (<c>RequiredIfNot</c>/<c>RequiredItemsIfNot</c>) form.
    /// </summary>
    private static void WriteDependentValueCondition(Utf8JsonWriter writer, OpenApiConditionalRequirement conditional)
    {
        writer.WriteStartObject();

        if (conditional.DependentValueNegated)
        {
            writer.WritePropertyName("not");
            writer.WriteStartObject();
        }

        if (conditional.DependentValues.Length == 1)
        {
            writer.WritePropertyName("const");
            WriteConditionalValue(writer, conditional.DependentValues[0]);
        }
        else
        {
            writer.WritePropertyName("enum");
            writer.WriteStartArray();

            foreach (var value in conditional.DependentValues)
            {
                WriteConditionalValue(writer, value);
            }

            writer.WriteEndArray();
        }

        if (conditional.DependentValueNegated)
        {
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes the shape an active conditional requires of its target. String constraints carry <c>pattern</c> as well as
    /// <c>minLength</c> because the runtime attributes and the generated fast path both reject a whitespace-only value,
    /// which <c>"minLength": 1</c> alone permits.
    /// </summary>
    /// <remarks>
    /// "Not null" is expressed by the enclosing <c>then</c>'s <c>required</c> entry alone. The obvious stronger form,
    /// <c>{ "not": { "type": "null" } }</c>, is not legal in the OpenAPI 3.0 dialect this document declares — <c>"null"</c>
    /// is outside its <c>type</c> enum — and validators reject the document rather than skipping the keyword. What it adds
    /// over <c>required</c> is rejecting an explicit <c>"x": null</c>, so omitting it under-enforces slightly, which is the
    /// safe direction.
    /// </remarks>
    private static void WriteConditionalTargetConstraint(Utf8JsonWriter writer, OpenApiConditionalTargetKind targetKind, bool omitLegacyExtras)
    {
        writer.WriteStartObject();

        switch (targetKind)
        {
            // Legacy's string target carried no pattern, only its items did.
            case OpenApiConditionalTargetKind.NonBlankString:
                WriteNonBlankStringConstraint(writer, includePattern: !omitLegacyExtras);
                break;

            // NonNull contributes nothing beyond the enclosing "required" entry, so it yields an empty schema. The
            // caller skips it entirely rather than emitting one; handling it here keeps this method total, so that
            // skip stays an optimisation rather than something this method depends on. Legacy wrote "not": { "type":
            // "null" }, which the generated client's validators accept.
            case OpenApiConditionalTargetKind.NonNull:
                if (omitLegacyExtras)
                {
                    writer.WritePropertyName("not");
                    writer.WriteStartObject();
                    writer.WriteString("type", "null");
                    writer.WriteEndObject();
                }

                break;

            case OpenApiConditionalTargetKind.NonBlankStringItems:
            case OpenApiConditionalTargetKind.NonNullItems:
                writer.WriteString("type", "array");
                writer.WriteNumber("minItems", 1);

                if (targetKind == OpenApiConditionalTargetKind.NonBlankStringItems)
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    WriteNonBlankStringConstraint(writer);
                    writer.WriteEndObject();
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Unsupported conditional target kind.");
        }

        writer.WriteEndObject();
    }

    private static void WriteNonBlankStringConstraint(Utf8JsonWriter writer, bool includePattern = true)
    {
        writer.WriteString("type", "string");
        writer.WriteNumber("minLength", 1);

        // minLength alone still admits "   ", which string.IsNullOrWhiteSpace rejects. The pattern is unanchored, so it
        // matches a non-whitespace character anywhere in the value.
        if (includePattern)
        {
            writer.WriteString("pattern", @"\S");
        }
    }

    /// <summary>
    /// Writes a dependent value with its natural JSON type.
    /// </summary>
    /// <remarks>
    /// Every integral type must be listed. A value that falls through to the string branch can never match a property the
    /// document declares as <c>"type": "integer"</c>, and under the negated form that inverts: a <c>const</c> that never
    /// matches makes the enclosing <c>not</c> always true, so the requirement always applies and the generated client
    /// rejects payloads the server accepts. Enum members and chars arrive already normalised to strings, and
    /// <see cref="OpenApiValidationConstraints.ExtractConditionalRequirements"/> drops any requirement carrying a value it
    /// cannot represent, so the string branch is reached only by values that genuinely render as strings.
    /// </remarks>
    private static void WriteConditionalValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case short s:
                writer.WriteNumberValue(s);
                break;
            case byte by:
                writer.WriteNumberValue(by);
                break;
            case sbyte sb:
                writer.WriteNumberValue(sb);
                break;
            case ushort us:
                writer.WriteNumberValue(us);
                break;
            case uint ui:
                writer.WriteNumberValue(ui);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
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

    /// <summary>
    /// Returns the key a deprecation message of a property, a parameter or an operation is written under:
    /// <c>x-deprecatedMessage</c> under Legacy naming, which is what legacy's NJsonSchema wrote, and otherwise
    /// <c>x-deprecated-reason</c>.
    /// </summary>
    private static string DeprecationMessageKey(bool omitLegacyExtras)
    {
        return omitLegacyExtras ? "x-deprecatedMessage" : "x-deprecated-reason";
    }

    /// <summary>
    /// Writes <c>deprecated</c> for a deprecated property, whatever form the property takes, with its message beside it
    /// under <see cref="DeprecationMessageKey"/>.
    /// </summary>
    private static void WritePropertyDeprecation(Utf8JsonWriter writer, OpenApiSchemaComponentPropertyModel property, bool omitLegacyExtras)
    {
        if (!property.IsDeprecated)
        {
            return;
        }

        writer.WriteBoolean("deprecated", true);

        if (property.DeprecationReason is not null)
        {
            writer.WriteString(DeprecationMessageKey(omitLegacyExtras), property.DeprecationReason);
        }
    }

    private static void WriteSchemaProperty(Utf8JsonWriter writer, OpenApiSchemaComponentPropertyModel property, Func<string, string>? schemaNameFormatter, bool omitLegacyExtras)
    {
        writer.WritePropertyName(property.Name);
        writer.WriteStartObject();

        // Direct schema reference: property IS a $ref to another component (e.g. the "data" property
        // on a ResourceEvent envelope referencing the inner resource type). OpenAPI 3.0 ignores a $ref's siblings, so a
        // property that is nullable or described wraps the reference in oneOf beside them, as legacy Blueprint did.
        if (property.SchemaRef is not null)
        {
            if (property.IsNullable || property.Description is not null || property.IsDeprecated)
            {
                if (property.IsNullable)
                {
                    writer.WriteBoolean("nullable", true);
                }

                if (property.Description is not null)
                {
                    writer.WriteString("description", property.Description);
                }

                WritePropertyDeprecation(writer, property, omitLegacyExtras);

                writer.WritePropertyName("oneOf");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.SchemaRef, schemaNameFormatter)}");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.SchemaRef, schemaNameFormatter)}");
            }

            writer.WriteEndObject();
            return;
        }

        // Enum-typed properties are rendered as oneOf: [{ $ref: ... }] pointing at the enum's
        // top-level component schema, matching the pattern established by NJsonSchema / S365 and
        // expected by TypeScript generators that resolve type names from $ref paths.
        if (property.EnumSchemaRef is not null)
        {
            // Legacy left a property with nothing beside its reference as the bare reference (R163).
            if (omitLegacyExtras && !property.IsNullable && property.Description is null && !property.IsDeprecated)
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.EnumSchemaRef, schemaNameFormatter)}");
                writer.WriteEndObject();
                return;
            }

            if (property.IsNullable)
            {
                writer.WriteBoolean("nullable", true);
            }

            if (property.Description is not null)
            {
                writer.WriteString("description", property.Description);
            }

            WritePropertyDeprecation(writer, property, omitLegacyExtras);

            writer.WritePropertyName("oneOf");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.EnumSchemaRef, schemaNameFormatter)}");
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject();
            return;
        }

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

            // A nullable element wraps its reference in oneOf beside "nullable", as a nullable property does (R163).
            if (property.ItemsAreNullable)
            {
                writer.WriteBoolean("nullable", true);
                writer.WritePropertyName("oneOf");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.ItemsSchemaRef, schemaNameFormatter)}");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.ItemsSchemaRef, schemaNameFormatter)}");
            }

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

            // A nullable value type element (double?[]) is nullable beside its type, as a nullable property is.
            if (property.ItemsAreNullable)
            {
                writer.WriteBoolean("nullable", true);
            }

            writer.WriteEndObject();
        }

        if (property.AdditionalPropertiesSchemaRef is not null)
        {
            writer.WritePropertyName("additionalProperties");
            writer.WriteStartObject();

            // A nullable value wraps its reference in oneOf beside "nullable", as a nullable property does.
            if (property.AdditionalPropertiesAreNullable)
            {
                writer.WriteBoolean("nullable", true);
                writer.WritePropertyName("oneOf");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesSchemaRef, schemaNameFormatter)}");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesSchemaRef, schemaNameFormatter)}");
            }

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

            if (property.AdditionalPropertiesAreNullable)
            {
                writer.WriteBoolean("nullable", true);
            }

            // When dictionary values are arrays, emit the nested items schema.
            if (string.Equals(property.AdditionalPropertiesType, "array", StringComparison.Ordinal))
            {
                if (property.AdditionalPropertiesItemsSchemaRef is not null)
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    writer.WriteString("$ref", $"#/components/schemas/{SanitiseSchemaKey(property.AdditionalPropertiesItemsSchemaRef, schemaNameFormatter)}");
                    writer.WriteEndObject();
                }
                else if (property.AdditionalPropertiesItemsType is not null)
                {
                    writer.WritePropertyName("items");
                    writer.WriteStartObject();
                    writer.WriteString("type", property.AdditionalPropertiesItemsType);

                    if (property.AdditionalPropertiesItemsFormat is not null)
                    {
                        writer.WriteString("format", property.AdditionalPropertiesItemsFormat);
                    }

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }
        else if (property.AllowsAdditionalProperties)
        {
            // Nullable open-object properties (e.g. metadata, secureData, changedValues on ResourceEvent
            // envelopes) emit additionalProperties: {} to indicate any additional members are accepted.
            writer.WritePropertyName("additionalProperties");
            writer.WriteStartObject();
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

        WriteNumericConstraints(writer, property.Type, property.Minimum, property.Maximum, property.ExclusiveMinimum, property.ExclusiveMaximum);

        WritePropertyDeprecation(writer, property, omitLegacyExtras);

        // Legacy's Newtonsoft-based contract resolution never tracked a property's absent setter, so it never wrote
        // readOnly; omitted under Legacy naming to match.
        if (property.IsReadOnly && !omitLegacyExtras)
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

    /// <summary>
    /// The status description legacy wrote: its <c>HttpHost</c> gave every operation a 422 "Validation failure" and a 500
    /// "Unexpected error" (Signature365 8b40ecc961, <c>Blueprint.Http/HttpHost.cs:90-94</c>), and the other statuses an
    /// operation documents read as their standard ones.
    /// </summary>
    private static string MapLegacyStatusDescription(int statusCode)
    {
        return statusCode switch
        {
            422 => "Validation failure",
            500 => "Unexpected error",
            _ => MapStatusDescription(statusCode),
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
