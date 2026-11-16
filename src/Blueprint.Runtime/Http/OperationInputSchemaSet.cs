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
/// Under <see cref="OpenApiSchemaNaming.Legacy"/> an inherited route, query, header or cookie property of a non-GET
/// operation is documented as a parameter and in the body as well (R153), the body copy under its body name. It is one
/// property, which the schema has once, where the operation binds it from, and not at all for a cookie; the body's
/// conditionals are made to name it as the schema does. Two different properties of one name are a collision.
/// </para>
/// </remarks>
internal sealed class OperationInputSchemaSet
{
    private const string ComponentReferencePrefix = "#/components/schemas/";
    private const string DefinitionReferencePrefix = "#/$defs/";

    // Marks a read-only property a request body still sets, until conversion drops it with every other vendor extension.
    private const string DeserializedKeyword = "x-blueprint-deserialized";

    private readonly FrozenDictionary<string, Entry> _entriesByName;
    private readonly FrozenDictionary<string, ImmutableArray<Entry>> _entriesByNameIgnoringCase;
    private readonly ImmutableArray<Entry> _entries;

    private OperationInputSchemaSet(ImmutableArray<Entry> entries)
    {
        // Descriptor names are unique by ordinal comparison only, so two may differ by case alone.
        this._entries = entries;
        this._entriesByName = entries.ToFrozenDictionary(static e => e.OperationName, StringComparer.Ordinal);
        this._entriesByNameIgnoringCase = entries
            .GroupBy(static e => e.OperationName, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(static g => g.Key, static g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Makes the schema of every operation <paramref name="document"/> writes, from the JSON it was rendered as.
    /// </summary>
    /// <param name="app">The app, whose descriptors give the order each operation declares its URLs in.</param>
    /// <param name="document">The assembled document model.</param>
    /// <param name="documentJson">The JSON the document was rendered as.</param>
    /// <param name="readOnlyProperties">
    /// Every read-only property of a component, by the component's key and its name, which the document does not mark under
    /// <see cref="OpenApiSchemaNaming.Legacy"/>, and whether the serializer sets it from a request body all the same.
    /// </param>
    /// <returns>The schemas.</returns>
    public static OperationInputSchemaSet Create(
        BlueprintApp app,
        OpenApiDocumentRenderingContext document,
        string documentJson,
        IReadOnlyCollection<(string ComponentKey, string PropertyName, bool IsDeserialized)> readOnlyProperties)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documentJson);
        ArgumentNullException.ThrowIfNull(readOnlyProperties);

        var root = JsonNode.Parse(documentJson)!.AsObject();
        var paths = root["paths"]?.AsObject();
        var components = root["components"]?["schemas"]?.AsObject();

        // A request does not send a read-only property, so the schema must know one the document left unmarked. The serializer
        // still sets one with a setter, such as a [ReadOnly(true)] property, so what the serializer or validation requires of
        // that one is still required of a request.
        foreach (var (componentKey, propertyName, isDeserialized) in readOnlyProperties)
        {
            try
            {
                if (components?[componentKey]?["properties"]?[propertyName] is JsonObject property)
                {
                    property["readOnly"] = true;

                    if (isDeserialized)
                    {
                        property[DeserializedKeyword] = true;
                    }
                }
            }
            catch (ArgumentException)
            {
                // A component a processor made unreadable, such as one with a name twice, fails only the operations that
                // read it, when they do.
            }
        }
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

        // An operation processor may rename an operation, so it is known by its type, and by the name its descriptor gives it.
        var descriptorsByTypeName = app.Operations
            .GroupBy(static d => d.OperationType.FullName ?? d.OperationType.Name, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
        var entries = new List<Entry>();

        foreach (var operation in primaryBySlot.Values.DistinctBy(static o => o.OperationTypeName, StringComparer.Ordinal))
        {
            var descriptor = descriptorsByTypeName.GetValueOrDefault(operation.OperationTypeName);
            var operationName = descriptor?.Name ?? operation.OperationName;
            var slot = FindFirstDeclaredSlot(operation, descriptor, primaryBySlot);
            var operationId = operationIds.GetValueOrDefault(slot) ?? operation.OperationName;

            try
            {
                var slotJson = paths?[slot.UrlFormat]?[slot.Method.ToLowerInvariant()]?.AsObject();
                var declaredUrlFormat = descriptor?.Urls
                    .FirstOrDefault(url => string.Equals(OpenApiOperationRenderingContext.ToPathKey(url.UrlFormat), slot.UrlFormat, StringComparison.Ordinal))
                    ?.UrlFormat;

                entries.Add(CreateEntry(operation, operationName, operationId, (declaredUrlFormat, slot.Method), slotJson, components));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // An entry a processor made unreadable, such as an object with one name twice, is that operation's alone:
                // the others' schemas stay to be had.
                var error = $"The operation {operationName} cannot be given an input schema, as its entry in the "
                    + $"OpenAPI document could not be read: {exception.Message}";

                entries.Add(new Entry(operationName, null, error));
            }
        }

        return new OperationInputSchemaSet([.. entries.OrderBy(static e => e.OperationName, StringComparer.Ordinal)]);
    }

    /// <summary>Makes a set of schemas already made, which is how operations no one app can compile are tested.</summary>
    /// <param name="schemas">The schemas.</param>
    /// <returns>The set.</returns>
    internal static OperationInputSchemaSet FromSchemas(IEnumerable<OperationInputSchema> schemas)
    {
        return new OperationInputSchemaSet(
            [.. schemas.Select(static s => new Entry(s.OperationName, s, null)).OrderBy(static e => e.OperationName, StringComparer.Ordinal)]);
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
    /// <param name="operationName">
    /// The operation's name: exactly, or ignoring case when no operation has it exactly and only one has it ignoring case.
    /// </param>
    /// <returns>The schema, or <see langword="null"/> when the document writes no operation of that name.</returns>
    public OperationInputSchema? GetSchema(string operationName)
    {
        if (!this._entriesByName.TryGetValue(operationName, out var entry))
        {
            if (!this._entriesByNameIgnoringCase.TryGetValue(operationName, out var matches))
            {
                return null;
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"The name '{operationName}' matches the operations {string.Join(" and ", matches.Select(static e => e.OperationName))} "
                    + "ignoring case; ask for one by its exact name.");
            }

            entry = matches[0];
        }

        return entry.Error is null ? entry.Schema : throw new InvalidOperationException(entry.Error);
    }

    /// <summary>
    /// Finds the slot whose <c>operationId</c> names the operation: the first URL it declares, as legacy's document gave
    /// that URL its unnumbered id (<see cref="LegacyOperationIds"/>), at the first of its methods, where the document writes
    /// it rather than another operation sharing the slot. So that a request made from the schema at that slot carries every
    /// input where the schema says, the URL is the first with a placeholder for each of its path inputs, which one declared
    /// without them reads from the query string or body instead, and the method of an operation with a body is one whose
    /// body is read, which a GET's is not.
    /// </summary>
    private static (string UrlFormat, string Method) FindFirstDeclaredSlot(
        OpenApiOperationModel operation,
        ApiOperationDescriptor? descriptor,
        Dictionary<(string UrlFormat, string Method), OpenApiOperationModel> primaryBySlot)
    {
        var pathInputs = operation.Parameters.Where(static p => p.In == "path").Select(static p => p.Name).ToList();
        var declaredUrlFormats = descriptor is null
            ? operation.UrlFormats
            : [.. descriptor.Urls.Select(static url => OpenApiOperationRenderingContext.ToPathKey(url.UrlFormat))];

        // OrderBy is stable, so the URLs that have every placeholder stay in the order they are declared in.
        var urlFormats = declaredUrlFormats.OrderBy(url => !HasPlaceholderForEach(url, pathInputs)).ToList();
        var methods = operation.HttpMethods
            .OrderBy(method => operation.RequestBody is not null && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            .ThenBy(static method => method, StringComparer.Ordinal)
            .ToList();

        foreach (var urlFormat in urlFormats)
        {
            foreach (var method in methods)
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

    /// <summary>Returns whether a route template has a placeholder for each of <paramref name="names"/>.</summary>
    private static bool HasPlaceholderForEach(string urlFormat, List<string> names)
    {
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var start = urlFormat.IndexOf('{', StringComparison.Ordinal); start >= 0; start = urlFormat.IndexOf('{', start + 1))
        {
            var end = urlFormat.IndexOf('}', start);

            if (end < 0)
            {
                break;
            }

            // A placeholder's name is what precedes any constraint, default or optional marker, after any catch-all star.
            var placeholder = urlFormat[(start + 1)..end].TrimStart('*');
            var nameEnd = placeholder.IndexOfAny([':', '=', '?']);
            placeholders.Add(nameEnd < 0 ? placeholder : placeholder[..nameEnd]);
        }

        return names.TrueForAll(placeholders.Contains);
    }

    private static Entry CreateEntry(
        OpenApiOperationModel operation,
        string operationName,
        string operationId,
        (string? UrlFormat, string Method) slot,
        JsonObject? slotJson,
        JsonObject? components)
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

            Add(InputName(parameter), location.Value, parameter.ClrPropertyName, CreateParameterSchema(parameterJson), parameter.Required);
        }

        JsonNode? additionalProperties = null;
        JsonArray? conditionals = null;

        // Two body properties of one name are written as an object with that name twice, which cannot be read as one, so
        // they are found from the model, where both are still known by their own.
        var duplicateBodyProperties = operation.RequestBody?.Properties
            .GroupBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static g => g.DistinctBy(static p => p.ClrPropertyName ?? p.Name, StringComparer.Ordinal).ToList())
            .Where(static distinct => distinct.Count > 1) ?? [];

        foreach (var duplicates in duplicateBodyProperties)
        {
            collisions.Add(
                $"'{duplicates[0].Name}': the body property {duplicates[0].ClrPropertyName ?? duplicates[0].Name} and the "
                + $"body property {duplicates[1].ClrPropertyName ?? duplicates[1].Name}");
        }

        if (collisions.Count == 0 && operation.RequestBody is { } requestBody && FindRequestBodySchema(slotJson, components) is { } bodySchema)
        {
            var clrNamesByBodyName = requestBody.Properties
                .GroupBy(static p => p.Name, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().ClrPropertyName, StringComparer.Ordinal);
            var bodyRequired = bodySchema["required"]?.AsArray().Select(static n => (string?)n).ToHashSet(StringComparer.Ordinal) ?? [];

            // A property the operation binds from a parameter is written in the body as well under Legacy naming (R153), by
            // its body name rather than the parameter's, and a cookie one too. That copy is not an input: the property is
            // the parameter, or, for a cookie, none at all.
            var parameterNamesByClrName = operation.Parameters
                .Where(static p => p.ClrPropertyName is not null)
                .GroupBy(static p => p.ClrPropertyName!, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.First().In == "cookie" ? null : InputName(g.First()), StringComparer.Ordinal);
            var copiedBodyNames = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var (name, propertySchema) in bodySchema["properties"]?.AsObject() ?? [])
            {
                if (clrNamesByBodyName.GetValueOrDefault(name) is { } clrName
                    && parameterNamesByClrName.TryGetValue(clrName, out var parameterName))
                {
                    copiedBodyNames[name] = parameterName;

                    continue;
                }

                Add(
                    name,
                    OperationInputLocation.Body,
                    clrNamesByBodyName.GetValueOrDefault(name),
                    propertySchema?.DeepClone() as JsonObject ?? [],
                    bodyRequired.Contains(name));
            }

            additionalProperties = bodySchema["additionalProperties"]?.DeepClone();
            conditionals = bodySchema["allOf"] is JsonArray bodyConditionals
                ? new JsonArray([.. bodyConditionals.OfType<JsonObject>().Select(c => RetargetConditional(c, copiedBodyNames)).OfType<JsonNode>()])
                : null;
        }

        if (collisions.Count > 0)
        {
            var error = $"The operation {operationName} cannot be given one input schema, as two of its properties take "
                + $"one name ({string.Join("; ", collisions)}). Bind one of them by another name.";

            return new Entry(operationName, null, error);
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
            operationName,
            new OperationInputSchema(operationName, operationId, parsed.RootElement.Clone(), inputs.ToImmutable())
            {
                Summary = operation.Summary,
                Description = operation.Description,
                UrlFormat = slot.UrlFormat,
                HttpMethod = slot.Method,
            },
            null);
    }

    /// <summary>
    /// Returns the name a request sends a parameter by: a query parameter's binding name, which the document does not write
    /// under <see cref="OpenApiParameterNaming.PropertyName"/>, or else the name the document gives it.
    /// </summary>
    private static string InputName(OpenApiParameterModel parameter)
    {
        return parameter.BindingName ?? parameter.Name;
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

    /// <summary>
    /// Copies a body conditional (an <c>if</c>/<c>then</c> pair) so it names the inputs the schema has: a body copy of a
    /// parameter by the parameter's name. A conditional naming a cookie's copy, which the schema has no input for, is dropped.
    /// </summary>
    /// <param name="conditional">The body's conditional.</param>
    /// <param name="copiedBodyNames">Each body copy's name, to the parameter's, or <see langword="null"/> for a cookie's.</param>
    /// <returns>The copy, or <see langword="null"/> when it is dropped.</returns>
    private static JsonObject? RetargetConditional(JsonObject conditional, Dictionary<string, string?> copiedBodyNames)
    {
        var copy = conditional.DeepClone().AsObject();

        foreach (var clause in (string[])["if", "then"])
        {
            if (copy[clause] is not JsonObject clauseSchema)
            {
                continue;
            }

            if (clauseSchema["required"] is JsonArray required)
            {
                for (var i = 0; i < required.Count; i++)
                {
                    if ((string?)required[i] is { } name && copiedBodyNames.TryGetValue(name, out var input))
                    {
                        if (input is null)
                        {
                            return null;
                        }

                        required[i] = input;
                    }
                }
            }

            if (clauseSchema["properties"] is JsonObject properties)
            {
                var renamed = new JsonObject();

                foreach (var (name, schema) in properties.ToList())
                {
                    var input = copiedBodyNames.TryGetValue(name, out var parameterName) ? parameterName : name;

                    if (input is null)
                    {
                        return null;
                    }

                    properties.Remove(name);
                    renamed[input] = schema;
                }

                clauseSchema["properties"] = renamed;
            }
        }

        return copy;
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

        // OpenAPI 3.0 requires a read-only property of a response only; JSON Schema's required would ask a request for it,
        // whether always or when a conditional's if holds. One the serializer sets all the same keeps its requirements.
        // They are found before the properties are converted, which drops the marker.
        var readOnlyNames = (schema["properties"] as JsonObject ?? [])
            .Where(static p => p.Value?["readOnly"]?.GetValueKind() == JsonValueKind.True
                && p.Value?[DeserializedKeyword]?.GetValueKind() != JsonValueKind.True)
            .Select(static p => p.Key)
            .ToHashSet(StringComparer.Ordinal);

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

        if (readOnlyNames.Count > 0)
        {
            RemoveRequired(schema, readOnlyNames);

            foreach (var conditional in schema["allOf"] as JsonArray ?? [])
            {
                if (conditional?["then"] is JsonObject then)
                {
                    RemoveRequired(then, readOnlyNames);
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

    private static void RemoveRequired(JsonObject schema, HashSet<string> names)
    {
        if (schema["required"] is not JsonArray required)
        {
            return;
        }

        foreach (var name in required.Where(n => names.Contains((string?)n ?? string.Empty)).ToList())
        {
            required.Remove(name);
        }

        if (required.Count == 0)
        {
            schema.Remove("required");
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

        // Annotations say what the whole property is, null or not, so they stay beside the anyOf, where required is checked
        // against readOnly.
        foreach (var name in schema.Select(static p => p.Key)
            .Where(static k => k is not ("description" or "deprecated" or "default" or "examples" or "readOnly" or "writeOnly"))
            .ToList())
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
