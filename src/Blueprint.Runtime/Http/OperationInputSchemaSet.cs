using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// The flattened input schema of every operation an OpenAPI document writes, or why one cannot be made (D21).
/// </summary>
/// <remarks>
/// <para>
/// Which properties an operation has, and where it reads each from, comes from its <see cref="OpenApiOperationModel"/>.
/// Each property's schema is the one the rendered document gives it, so the constraints, descriptions and any schema
/// processor's changes agree with the document, converted from OpenAPI 3.0's dialect to JSON Schema's: <c>nullable</c>
/// becomes a <c>null</c> type, <c>example</c> becomes <c>examples</c>, vendor extensions (<c>x-</c>) are dropped, and every
/// component referred to is copied under <c>$defs</c>.
/// </para>
/// <para>
/// Under <see cref="OpenApiSchemaNaming.Legacy"/> an inherited route, query or header property of a non-GET operation is
/// documented as a parameter and in the body as well (R153). It is one property, which the schema has once, where the
/// operation binds it from; two different properties of one name are a collision.
/// </para>
/// </remarks>
internal sealed class OperationInputSchemaSet
{
    private const string ComponentReferencePrefix = "#/components/schemas/";
    private const string DefinitionReferencePrefix = "#/$defs/";

    private readonly FrozenDictionary<string, Entry> _entriesByName;
    private readonly ImmutableArray<Entry> _entries;

    private OperationInputSchemaSet(ImmutableArray<Entry> entries)
    {
        this._entries = entries;
        this._entriesByName = entries.ToFrozenDictionary(static e => e.OperationName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Makes the schema of every operation <paramref name="document"/> writes, from the JSON it was rendered as.
    /// </summary>
    /// <param name="app">The app, whose descriptors give the order each operation declares its URLs in.</param>
    /// <param name="document">The assembled document model.</param>
    /// <param name="documentJson">The JSON the document was rendered as.</param>
    /// <returns>The schemas.</returns>
    public static OperationInputSchemaSet Create(BlueprintApp app, OpenApiDocumentRenderingContext document, string documentJson)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentJson);

        var root = JsonNode.Parse(documentJson)!.AsObject();
        var paths = root["paths"]?.AsObject();
        var components = root["components"]?["schemas"]?.AsObject();
        var operationIds = LegacyOperationIds.Assign(document);

        // The operation a slot is written for is the first of those that share it, as the document writes it.
        var primaryBySlot = new Dictionary<(string UrlFormat, string Method), OpenApiOperationModel>();

        foreach (var path in document.Paths)
        {
            foreach (var method in path.Methods)
            {
                if (!method.Operations.IsEmpty)
                {
                    primaryBySlot[(path.UrlFormat, method.HttpMethod.ToUpperInvariant())] = method.Operations[0];
                }
            }
        }

        var descriptorsByName = app.Operations.ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>();

        foreach (var operation in primaryBySlot.Values.DistinctBy(static o => o.OperationName, StringComparer.OrdinalIgnoreCase))
        {
            var slot = FindFirstDeclaredSlot(operation, descriptorsByName.GetValueOrDefault(operation.OperationName), primaryBySlot);
            var slotJson = paths?[slot.UrlFormat]?[slot.Method.ToLowerInvariant()]?.AsObject();

            entries.Add(CreateEntry(operation, operationIds.GetValueOrDefault(slot) ?? operation.OperationName, slotJson, components));
        }

        return new OperationInputSchemaSet([.. entries.OrderBy(static e => e.OperationName, StringComparer.Ordinal)]);
    }

    /// <summary>Gets every schema, failing when one could not be made.</summary>
    /// <returns>The schemas, ordered by operation name.</returns>
    public IReadOnlyList<OperationInputSchema> GetSchemas()
    {
        var errors = this._entries.Where(static e => e.Error is not null).Select(static e => e.Error!).ToList();

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        return [.. this._entries.Select(static e => e.Schema!)];
    }

    /// <summary>Gets one operation's schema, failing when it could not be made.</summary>
    /// <param name="operationName">The operation's name, ignoring case.</param>
    /// <returns>The schema, or <see langword="null"/> when the document writes no operation of that name.</returns>
    public OperationInputSchema? GetSchema(string operationName)
    {
        if (!this._entriesByName.TryGetValue(operationName, out var entry))
        {
            return null;
        }

        return entry.Error is null ? entry.Schema : throw new InvalidOperationException(entry.Error);
    }

    /// <summary>
    /// Finds the slot whose <c>operationId</c> names the operation: the first URL it declares, as legacy's document gave
    /// that URL its unnumbered id (<see cref="LegacyOperationIds"/>), at the first of its methods, where the document writes
    /// it rather than another operation sharing the slot.
    /// </summary>
    private static (string UrlFormat, string Method) FindFirstDeclaredSlot(
        OpenApiOperationModel operation,
        ApiOperationDescriptor? descriptor,
        Dictionary<(string UrlFormat, string Method), OpenApiOperationModel> primaryBySlot)
    {
        var urlFormats = descriptor is null
            ? operation.UrlFormats
            : [.. descriptor.Urls.Select(static url => OpenApiOperationRenderingContext.ToPathKey(url.UrlFormat))];

        foreach (var urlFormat in urlFormats)
        {
            foreach (var method in operation.HttpMethods.Order(StringComparer.Ordinal))
            {
                var slot = (urlFormat, method.ToUpperInvariant());

                if (primaryBySlot.TryGetValue(slot, out var primary) && ReferenceEquals(primary, operation))
                {
                    return slot;
                }
            }
        }

        // Every operation reached here is the primary of a slot, so one is always found above.
        return primaryBySlot.First(pair => ReferenceEquals(pair.Value, operation)).Key;
    }

    private static Entry CreateEntry(OpenApiOperationModel operation, string operationId, JsonObject? slotJson, JsonObject? components)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var inputs = ImmutableArray.CreateBuilder<OperationInputProperty>();
        var sources = new Dictionary<string, (OperationInputLocation Location, string? ClrPropertyName)>(StringComparer.OrdinalIgnoreCase);
        var collisions = new List<string>();

        void Add(string name, OperationInputLocation location, string? clrPropertyName, JsonObject schema, bool isRequired)
        {
            if (sources.TryGetValue(name, out var existing))
            {
                // One property documented twice (R153) is written once, where it is bound from; two are a collision.
                if (clrPropertyName is null || !string.Equals(existing.ClrPropertyName, clrPropertyName, StringComparison.Ordinal))
                {
                    collisions.Add(
                        $"'{name}': the {Describe(existing.Location)} property {existing.ClrPropertyName ?? name} and the "
                        + $"{Describe(location)} property {clrPropertyName ?? name}");
                }

                return;
            }

            sources[name] = (location, clrPropertyName);
            properties[name] = schema;
            inputs.Add(new OperationInputProperty(name, location));

            if (isRequired)
            {
                required.Add((JsonNode)name);
            }
        }

        var parametersJson = slotJson?["parameters"]?.AsArray();

        foreach (var parameter in operation.Parameters)
        {
            var location = parameter.In switch
            {
                "path" => OperationInputLocation.Path,
                "query" => OperationInputLocation.Query,
                "header" => OperationInputLocation.Header,

                // A cookie is not something a caller of a tool sends.
                _ => (OperationInputLocation?)null,
            };

            if (location is null)
            {
                continue;
            }

            var parameterJson = parametersJson?
                .OfType<JsonObject>()
                .FirstOrDefault(p => (string?)p["name"] == parameter.Name && (string?)p["in"] == parameter.In);

            Add(parameter.Name, location.Value, parameter.ClrPropertyName, CreateParameterSchema(parameterJson), parameter.Required);
        }

        JsonNode? additionalProperties = null;
        JsonArray? conditionals = null;

        if (operation.RequestBody is { } requestBody && FindRequestBodySchema(slotJson, components) is { } bodySchema)
        {
            var clrNamesByBodyName = requestBody.Properties
                .GroupBy(static p => p.Name, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().ClrPropertyName, StringComparer.Ordinal);
            var bodyRequired = bodySchema["required"]?.AsArray().Select(static n => (string?)n).ToHashSet(StringComparer.Ordinal) ?? [];

            foreach (var (name, propertySchema) in bodySchema["properties"]?.AsObject() ?? [])
            {
                Add(
                    name,
                    OperationInputLocation.Body,
                    clrNamesByBodyName.GetValueOrDefault(name),
                    propertySchema?.DeepClone() as JsonObject ?? [],
                    bodyRequired.Contains(name));
            }

            additionalProperties = bodySchema["additionalProperties"]?.DeepClone();
            conditionals = bodySchema["allOf"]?.DeepClone().AsArray();
        }

        if (collisions.Count > 0)
        {
            var error = $"The operation {operation.OperationName} cannot be given one input schema, as two of its properties take "
                + $"one name ({string.Join("; ", collisions)}). Bind one of them by another name.";

            return new Entry(operation.OperationName, null, error);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        if (additionalProperties is not null)
        {
            schema["additionalProperties"] = additionalProperties;
        }

        if (conditionals is { Count: > 0 })
        {
            schema["allOf"] = conditionals;
        }

        var definitions = ConvertWithDefinitions(schema, components);

        if (definitions.Count > 0)
        {
            schema["$defs"] = definitions;
        }

        using var parsed = JsonDocument.Parse(schema.ToJsonString());

        return new Entry(
            operation.OperationName,
            new OperationInputSchema(operation.OperationName, operationId, parsed.RootElement.Clone(), inputs.ToImmutable())
            {
                Summary = operation.Summary,
                Description = operation.Description,
            },
            null);
    }

    /// <summary>
    /// Makes a parameter's property schema from its document entry: its schema, with the parameter's own description,
    /// deprecation and example, which the document writes beside the schema rather than in it.
    /// </summary>
    private static JsonObject CreateParameterSchema(JsonObject? parameterJson)
    {
        var schema = parameterJson?["schema"]?.DeepClone() as JsonObject ?? [];

        if (parameterJson?["description"] is { } description)
        {
            schema["description"] = description.DeepClone();
        }

        if (parameterJson?["deprecated"] is { } deprecated)
        {
            schema["deprecated"] = deprecated.DeepClone();
        }

        if (parameterJson?["example"] is { } example)
        {
            schema["example"] = example.DeepClone();
        }

        return schema;
    }

    /// <summary>Finds the schema of the slot's JSON request body, following its reference to a component.</summary>
    private static JsonObject? FindRequestBodySchema(JsonObject? slotJson, JsonObject? components)
    {
        var schema = slotJson?["requestBody"]?["content"]?["application/json"]?["schema"]?.AsObject();

        if ((string?)schema?["$ref"] is { } reference && reference.StartsWith(ComponentReferencePrefix, StringComparison.Ordinal))
        {
            return components?[reference[ComponentReferencePrefix.Length..]]?.AsObject();
        }

        return schema;
    }

    /// <summary>
    /// Converts <paramref name="schema"/> to JSON Schema's dialect, and returns a copy, converted the same way, of every
    /// component it refers to, directly or through another, keyed as its <c>$defs</c> are.
    /// </summary>
    private static JsonObject ConvertWithDefinitions(JsonObject schema, JsonObject? components)
    {
        var definitions = new JsonObject();
        var pending = new Queue<string>();

        ConvertSchema(schema, pending, isRoot: true);

        while (pending.TryDequeue(out var name))
        {
            if (definitions.ContainsKey(name) || components?[name] is not JsonObject component)
            {
                continue;
            }

            var definition = component.DeepClone().AsObject();
            definitions[name] = definition;
            ConvertSchema(definition, pending, isRoot: false);
        }

        return definitions;
    }

    /// <summary>
    /// Converts one schema, and every schema within it, from OpenAPI 3.0's dialect to JSON Schema's in place, queueing the
    /// name of every component it refers to.
    /// </summary>
    private static void ConvertSchema(JsonObject schema, Queue<string> referencedComponents, bool isRoot)
    {
        foreach (var name in schema.Select(static p => p.Key).Where(static k => k.StartsWith("x-", StringComparison.Ordinal)).ToList())
        {
            schema.Remove(name);
        }

        if ((string?)schema["$ref"] is { } reference && reference.StartsWith(ComponentReferencePrefix, StringComparison.Ordinal))
        {
            var componentName = reference[ComponentReferencePrefix.Length..];
            schema["$ref"] = DefinitionReferencePrefix + componentName;
            referencedComponents.Enqueue(componentName);
        }

        if (schema.TryGetPropertyValue("example", out var example))
        {
            schema.Remove("example");
            schema["examples"] = new JsonArray(example?.DeepClone());
        }

        foreach (var keyword in (string[])["properties", "patternProperties"])
        {
            if (schema[keyword] is JsonObject map)
            {
                foreach (var (_, child) in map)
                {
                    ConvertChild(child, referencedComponents);
                }
            }
        }

        foreach (var keyword in (string[])["items", "additionalProperties", "not", "if", "then", "else"])
        {
            ConvertChild(schema[keyword], referencedComponents);
        }

        foreach (var keyword in (string[])["allOf", "anyOf", "oneOf"])
        {
            if (schema[keyword] is JsonArray branches)
            {
                foreach (var branch in branches)
                {
                    ConvertChild(branch, referencedComponents);
                }
            }
        }

        if (!isRoot && schema.TryGetPropertyValue("nullable", out var nullable))
        {
            schema.Remove("nullable");

            if (nullable?.GetValueKind() == JsonValueKind.True)
            {
                AdmitNull(schema);
            }
        }
    }

    private static void ConvertChild(JsonNode? child, Queue<string> referencedComponents)
    {
        if (child is JsonObject schema)
        {
            ConvertSchema(schema, referencedComponents, isRoot: false);
        }
    }

    /// <summary>
    /// Makes a schema OpenAPI 3.0 marked <c>nullable</c> admit <c>null</c> as JSON Schema says it: a <c>null</c> type beside
    /// its own, and in its <c>enum</c>; or, for one with no type of its own (a reference or a composition), as one branch
    /// of an <c>anyOf</c> whose other is <c>null</c>.
    /// </summary>
    private static void AdmitNull(JsonObject schema)
    {
        if (schema["type"] is JsonValue type && type.TryGetValue<string>(out var typeName))
        {
            schema["type"] = new JsonArray(typeName, "null");

            if (schema["enum"] is JsonArray values && !values.Any(static v => v is null))
            {
                values.Add(null);
            }

            return;
        }

        if (schema.ContainsKey("type") || !(schema.ContainsKey("$ref") || schema.ContainsKey("oneOf") || schema.ContainsKey("allOf") || schema.ContainsKey("anyOf")))
        {
            // A type list says for itself whether it admits null, and a schema of no type admits it already.
            return;
        }

        var branch = new JsonObject();

        foreach (var name in schema.Select(static p => p.Key).Where(static k => k is not ("description" or "deprecated" or "default" or "examples")).ToList())
        {
            var value = schema[name]?.DeepClone();
            schema.Remove(name);
            branch[name] = value;
        }

        // The document wraps a nullable reference in a one-branch oneOf (OpenAPI 3.0 ignores a $ref's siblings), which the
        // anyOf makes redundant.
        var nonNull = branch.Count == 1 && branch["oneOf"] is JsonArray { Count: 1 } only && only[0] is JsonObject single
            ? single.DeepClone()
            : branch;

        schema["anyOf"] = new JsonArray(nonNull, new JsonObject { ["type"] = "null" });
    }

    private static string Describe(OperationInputLocation location)
    {
        return location switch
        {
            OperationInputLocation.Path => "route",
            OperationInputLocation.Query => "query",
            OperationInputLocation.Header => "header",
            _ => "body",
        };
    }

    /// <summary>One operation's schema, or why it could not be made.</summary>
    /// <param name="OperationName">The operation's name.</param>
    /// <param name="Schema">Its schema, or <see langword="null"/> when <paramref name="Error"/> says why there is none.</param>
    /// <param name="Error">Why it has no schema, or <see langword="null"/>.</param>
    private sealed record Entry(string OperationName, OperationInputSchema? Schema, string? Error);
}
