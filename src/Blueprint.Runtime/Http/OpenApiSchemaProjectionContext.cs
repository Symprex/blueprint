using System.Collections.Frozen;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Validation;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Projects covered app-owned serializer metadata into a stable schema-ready runtime model so later OpenAPI document work
/// can consume JSON shape facts without rediscovering body coverage or widening into document rendering yet.
/// </summary>
/// <param name="Document">The document-builder snapshot that owns the projected schema set.</param>
/// <param name="Schemas">The distinct covered body-type schemas available for later OpenAPI work.</param>
/// <param name="FullNameToShortName">Maps full CLR body-type names (both backtick and angle-bracket formats for generics)
/// to the short <see cref="OpenApiSchemaNames"/> keys used by schema components.</param>
public sealed record OpenApiSchemaProjectionContext(
    OpenApiDocumentBuilderContext Document,
    ImmutableArray<OpenApiSchemaModel> Schemas,
    FrozenDictionary<string, string> FullNameToShortName)
{
    /// <summary>
    /// Creates a schema projection snapshot for the covered body types in the supplied OpenAPI document-builder context.
    /// Missing/uncovered body types are intentionally excluded so this seam only projects serializer-backed shapes.
    /// </summary>
    /// <param name="document">The previously prepared OpenAPI document-builder context.</param>
    /// <returns>The schema projection snapshot.</returns>
    public static OpenApiSchemaProjectionContext Create(OpenApiDocumentBuilderContext document)
    {
        return Create(document, OpenApiSchemaNaming.Default);
    }

    /// <summary>
    /// Creates a schema projection snapshot for the covered body types in the supplied OpenAPI document-builder context,
    /// naming each schema under <paramref name="naming"/>.
    /// </summary>
    /// <param name="document">The previously prepared OpenAPI document-builder context.</param>
    /// <param name="naming">How schemas are named from their CLR types.</param>
    /// <returns>The schema projection snapshot.</returns>
    public static OpenApiSchemaProjectionContext Create(OpenApiDocumentBuilderContext document, OpenApiSchemaNaming naming)
    {
        return Create(document, naming, OpenApiRequiredProperties.Declared);
    }

    /// <summary>
    /// Creates a schema projection snapshot for the covered body types in the supplied OpenAPI document-builder context,
    /// naming each schema under <paramref name="naming"/> and marking its properties required as
    /// <paramref name="requiredProperties"/> says.
    /// </summary>
    /// <param name="document">The previously prepared OpenAPI document-builder context.</param>
    /// <param name="naming">How schemas are named from their CLR types.</param>
    /// <param name="requiredProperties">Which properties are marked required.</param>
    /// <returns>The schema projection snapshot.</returns>
    internal static OpenApiSchemaProjectionContext Create(
        OpenApiDocumentBuilderContext document,
        OpenApiSchemaNaming naming,
        OpenApiRequiredProperties requiredProperties)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.App.AppOwnedSerializerContext is not JsonSerializerContext serializerContext)
        {
            return new OpenApiSchemaProjectionContext(document, ImmutableArray<OpenApiSchemaModel>.Empty, FrozenDictionary<string, string>.Empty)
            {
                Naming = naming,
                RequiredProperties = requiredProperties,
            };
        }

        var namer = new SchemaNamer(naming);
        var coveredTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var bodyType in document.App.AppOwnedSerializerContextBodyTypes)
        {
            coveredTypesByName.TryAdd(namer.NameOf(bodyType), bodyType);
        }

        // Build a reverse lookup: full CLR name → FormatTypeName short name.
        // Descriptor feature data stores full CLR names at compile time (using Roslyn's
        // ToDisplayString format with angle brackets for generics), while coveredTypesByName
        // uses FormatTypeName-produced short names. This bridge resolves the mismatch.
        // For generic types, Type.FullName uses backtick notation (e.g., ResourceEvent`1[[...]])
        // while the source generator produces angle-bracket format (e.g., ResourceEvent<Foo>),
        // so we include both formats as keys to handle either naming convention.
        var fullNameToShortNameBuilder = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var bodyType in document.App.AppOwnedSerializerContextBodyTypes)
        {
            var shortName = namer.NameOf(bodyType);
            var fullName = bodyType.FullName ?? bodyType.Name;
            fullNameToShortNameBuilder.TryAdd(fullName, shortName);

            // Source generators produce angle-bracket format names via ToDisplayString, which also joins a nested type
            // to its container with a dot rather than a plus. Include this as an alternate key so the lookup succeeds
            // for either format — a nested type missing it would be found by its simple name alone, which another type
            // can share.
            var displayStyleName = FormatFullNameDisplayStyle(bodyType);

            if (!string.Equals(displayStyleName, fullName, StringComparison.Ordinal))
            {
                fullNameToShortNameBuilder.TryAdd(displayStyleName, shortName);
            }
        }

        var fullNameToShortName = fullNameToShortNameBuilder.ToFrozenDictionary(StringComparer.Ordinal);

        var rawBodyTypeNames = document.Operations
            .Where(static operation => operation.IsSerializerContextCoverageSatisfied)
            .SelectMany(static operation => operation.RequiredSerializerContextBodyTypeNames)
            .Distinct(StringComparer.Ordinal)
            .Select(name => fullNameToShortName.GetValueOrDefault(name, name))
            .ToList();

        // For array body types (e.g. "Namespace.Type[]"), also include the element type so it can be rendered
        // as a reusable schema component referenced by "type: array, items: { $ref: ... }".
        var elementTypeNames = rawBodyTypeNames
            .Where(static name => name.EndsWith("[]", StringComparison.Ordinal))
            .Select(static name => name[..^2])
            .Where(elementName => !rawBodyTypeNames.Contains(elementName, StringComparer.Ordinal));

        var coveredBodyTypeNames = rawBodyTypeNames
            .Concat(elementTypeNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static bodyTypeName => bodyTypeName, StringComparer.Ordinal);

        // The covered body types some operation needs are projected, and so is every object type they or a request body
        // reach — through a property, a collection's items, a dictionary's values or another such type — so that a nested
        // object is a component its property references, as legacy Blueprint made it, rather than a bare object.
        var projectedTypesByName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var bodyTypeName in coveredBodyTypeNames)
        {
            if (coveredTypesByName.TryGetValue(bodyTypeName, out var bodyType))
            {
                projectedTypesByName[bodyTypeName] = bodyType;
            }
        }

        var (nestedObjectTypes, reachedEnumTypes) = CollectNestedObjectTypes(projectedTypesByName.Values.ToList(), GetRequestBodyPropertyTypes(document, naming), serializerContext);

        foreach (var nestedType in nestedObjectTypes)
        {
            projectedTypesByName.TryAdd(namer.NameOf(nestedType), nestedType);
        }

        var schemaNamesByTypeBuilder = new Dictionary<Type, string>();

        foreach (var (name, type) in projectedTypesByName)
        {
            schemaNamesByTypeBuilder[type] = name;
        }

        // An enum's schema name is drawn from the same namer, and so the same collision-counter pool, as every object
        // type's: two distinct enum CLR types of one simple name (e.g. two apps' own AuthEnforcementType) must be told
        // apart the way legacy's NJsonSchema told any two same-named types apart, not silently collapsed onto the first
        // one's values — which a raw, unrouted OpenApiSchemaNames.Format call for every enum property did.
        foreach (var enumType in reachedEnumTypes)
        {
            schemaNamesByTypeBuilder.TryAdd(enumType, namer.NameOf(enumType));
        }

        var schemaNamesByType = schemaNamesByTypeBuilder.ToFrozenDictionary();

        // Every projected type, a nested one included, is known by its full name too, not only the covered body types
        // fullNameToShortName holds, so that whatever the document goes on to key by a runtime type's full name (a
        // resource's x-links) finds a component reached only through another type's property, list or dictionary.
        var projectedFullNameToNameBuilder = new Dictionary<string, string>(fullNameToShortName, StringComparer.Ordinal);

        foreach (var (name, type) in projectedTypesByName)
        {
            projectedFullNameToNameBuilder.TryAdd(type.FullName ?? type.Name, name);
        }

        var schemaBuilder = ImmutableArray.CreateBuilder<OpenApiSchemaModel>();

        foreach (var (schemaName, schemaType) in projectedTypesByName.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            try
            {
                schemaBuilder.Add(CreateSchema(schemaName, schemaType, serializerContext, naming, requiredProperties, schemaNamesByType));
            }
            catch (NotSupportedException)
            {
                // Types with custom JsonConverters that STJ source generation cannot handle
                // (SYSLIB1220) will throw when GetTypeInfo tries to configure their property
                // graph. Skip these types from OpenAPI schema projection — they still serialize
                // at runtime via the reflection fallback in HttpOperationResultExecutor.
            }
        }

        return new OpenApiSchemaProjectionContext(document, schemaBuilder.ToImmutable(), fullNameToShortName)
        {
            Naming = naming,
            RequiredProperties = requiredProperties,
            SchemaNamesByType = schemaNamesByType,
            ProjectedFullNameToName = projectedFullNameToNameBuilder.ToFrozenDictionary(StringComparer.Ordinal),
            BaseNames = namer.BaseNames.ToFrozenDictionary(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Gets which properties the schemas mark required, which the operations' request bodies and parameters follow too.
    /// </summary>
    internal OpenApiRequiredProperties RequiredProperties { get; init; }

    /// <summary>
    /// Gets the name of the schema component projected for each CLR type that has one, which a request body property
    /// of that type references in turn.
    /// </summary>
    internal FrozenDictionary<Type, string> SchemaNamesByType { get; init; } = FrozenDictionary<Type, string>.Empty;

    /// <summary>
    /// Gets the name of the schema component projected for each type, by the type's <see cref="Type.FullName"/>, which
    /// <see cref="FullNameToShortName"/> holds only for the covered body types: this one also names the types those reach.
    /// </summary>
    internal FrozenDictionary<string, string> ProjectedFullNameToName { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Gets, for each schema this projection named apart from another type of the same name, the name it would have had —
    /// from which the document settles, as legacy did, which of the types sharing a name keeps it.
    /// </summary>
    internal FrozenDictionary<string, string> BaseNames { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>
    /// Walks the object graph the serializer context describes, from the properties of <paramref name="rootTypes"/> and
    /// from <paramref name="requestBodyPropertyTypes"/>, and returns every object type reached that is not already a
    /// root (those need components of their own) alongside every enum type reached (those need a schema name of their
    /// own from the same collision-counter pool as an object's).
    /// </summary>
    /// <remarks>
    /// Only what the serializer context has metadata for is followed, and only an object-shaped type (not a primitive,
    /// an enum, a collection, a dictionary or a type written by a converter, such as a strongly typed id) becomes a
    /// component. <see cref="Blueprint.Model.Link"/> is excluded because the document always carries it as a well-known component.
    /// </remarks>
    private static (List<Type> NestedObjectTypes, List<Type> EnumTypes) CollectNestedObjectTypes(
        IReadOnlyCollection<Type> rootTypes,
        IEnumerable<Type> requestBodyPropertyTypes,
        JsonSerializerContext serializerContext)
    {
        var visited = new HashSet<Type>(rootTypes);
        var nested = new List<Type>();
        var enums = new List<Type>();

        foreach (var rootType in rootTypes)
        {
            foreach (var childType in GetChildTypes(TryGetTypeInfo(serializerContext, rootType)))
            {
                Visit(childType);
            }
        }

        foreach (var requestBodyPropertyType in requestBodyPropertyTypes)
        {
            Visit(requestBodyPropertyType);
        }

        return (nested, enums);

        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (!visited.Add(type))
            {
                return;
            }

            if (type.IsEnum)
            {
                enums.Add(type);
                return;
            }

            var typeInfo = TryGetTypeInfo(serializerContext, type);

            if (typeInfo is null)
            {
                return;
            }

            if (typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                // A type the document renders as a scalar (a NodaTime Instant is a date-time string, however the
                // serializer context describes it) is neither a component nor a way into more of them.
                if (MapKind(type, typeInfo.Kind) != OpenApiSchemaKind.Object)
                {
                    return;
                }

                if (type != typeof(Blueprint.Model.Link) && type != typeof(object))
                {
                    nested.Add(type);
                }
            }

            foreach (var childType in GetChildTypes(typeInfo))
            {
                Visit(childType);
            }
        }
    }

    private static IEnumerable<Type> GetChildTypes(JsonTypeInfo? typeInfo)
    {
        return typeInfo?.Kind switch
        {
            JsonTypeInfoKind.Object => typeInfo.Properties.Where(static property => !property.IsExtensionData && IsSerialised(property)).Select(static property => property.PropertyType),
            JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary when typeInfo.ElementType is { } elementType => [elementType],
            _ => [],
        };
    }

    private static JsonTypeInfo? TryGetTypeInfo(JsonSerializerContext serializerContext, Type type)
    {
        try
        {
            return serializerContext.GetTypeInfo(type);
        }
        catch (NotSupportedException)
        {
            // As for a covered type (see Create): a converter the source generator cannot configure is not described.
            return null;
        }
    }

    /// <summary>
    /// The CLR types of every body-bound property of every operation in <paramref name="document"/>, which may reach
    /// object types no response does.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "OpenAPI projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    private static IEnumerable<Type> GetRequestBodyPropertyTypes(OpenApiDocumentBuilderContext document, OpenApiSchemaNaming naming)
    {
        foreach (var operation in document.Operations)
        {
            if (!operation.Operation.TryGetFeatureData<Blueprint.Model.OperationPropertyOwnershipFeatureData>(out var ownership))
            {
                continue;
            }

            foreach (var entry in ownership.Entries)
            {
                if (operation.Operation.OperationType.GetProperty(entry.PropertyName, BindingFlags.Public | BindingFlags.Instance) is { } property
                    && OpenApiOperationRenderingContext.IsRequestBodyEntry(operation.Operation, entry, property, naming))
                {
                    yield return property.PropertyType;
                }
            }
        }
    }

    /// <summary>
    /// Gets how the schemas in this projection are named from their CLR types, which every other name the document
    /// derives from a type (a request body's, an enum's) follows too.
    /// </summary>
    public OpenApiSchemaNaming Naming { get; init; }

    private static OpenApiSchemaModel CreateSchema(
        string bodyTypeName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        JsonSerializerContext serializerContext,
        OpenApiSchemaNaming naming,
        OpenApiRequiredProperties requiredProperties,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        var typeInfo = serializerContext.GetTypeInfo(bodyType)
            ?? throw new InvalidOperationException($"The serializer context did not expose metadata for covered body type '{bodyTypeName}'.");
        var validationAttributesByJsonPropertyName = CreateValidationAttributesByJsonPropertyName(bodyType, serializerContext);

        // Create once per schema rather than per-property to avoid repeated allocations.
        var nullabilityContext = new NullabilityInfoContext();

        // A property the serializer neither writes nor reads, such as a [JsonIgnore]d one the metadata keeps without a
        // getter or setter (ApiResource.ResourceKey), is not part of the schema.
        var properties = typeInfo.Kind == JsonTypeInfoKind.Object
            ? typeInfo.Properties
                .Where(IsSerialised)
                .Select(property => CreateProperty(property, bodyType, bodyTypeName, serializerContext, validationAttributesByJsonPropertyName, nullabilityContext, naming, requiredProperties, schemaNamesByType))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OpenApiSchemaPropertyModel>.Empty;

        // The host sets the status of every problem it writes, so a problem's status is required and never null, whatever
        // the CLR type's int? says; a validation problem's errors may be null, and are not required whatever the rule for
        // required properties. Legacy Blueprint documented both so.
        var isProblem = typeof(Microsoft.AspNetCore.Mvc.ProblemDetails).IsAssignableFrom(bodyType);

        if (isProblem)
        {
            properties =
            [
                .. properties.Select(static property => property.Name switch
                {
                    "status" => property with { IsRequired = true, IsNullable = false },
                    "errors" => property with { IsRequired = false, IsNullable = true },
                    _ => property,
                }),
            ];

            // Legacy described a problem through Newtonsoft.Json, which knows nothing of System.Text.Json's
            // [JsonExtensionData], so it documented the bucket of extension members as a property of its own: a nullable
            // open object, which the host never writes, writing the members themselves beside the others. It documented
            // the problem as open to further members of any value, or null, too (AdditionalPropertiesAreNullable).
            if (!properties.Any(static property => !property.IsExtensionData && property.Name == "extensions"))
            {
                properties =
                [
                    .. properties.Append(new OpenApiSchemaPropertyModel(
                            "extensions",
                            OpenApiSchemaKind.Object,
                            IsRequired: false,
                            IsExtensionData: false,
                            IsNullable: true,
                            Format: null,
                            EnumValues: [],
                            EnumSchemaName: null,
                            Description: null)
                        {
                            AllowsAdditionalProperties = true,
                        })
                        .OrderBy(static property => property.Name, StringComparer.Ordinal),
                ];
            }
        }

        // R158: legacy's document described a problem's members, which the ASP.NET Core shared framework ships no XML
        // documentation to read here, and so does a document rendered as legacy's, from what legacy wrote.
        if (isProblem && naming == OpenApiSchemaNaming.Legacy)
        {
            properties =
            [
                .. properties.Select(property => property.Description is null && OpenApiProblemDetailsDocs.GetMemberDescription(bodyType, property.Name) is { } description
                    ? property with { Description = description }
                    : property),
            ];
        }

        // Any other type with a [JsonExtensionData] bucket legacy described the same way, by rule rather than by what
        // Signature365 recorded for a problem: its Newtonsoft.Json contract kept the bucket as a dictionary property like
        // any other, and NJsonSchema, which recognises the attribute by name, documented the type as open to further
        // members of the bucket's value, nullable when the value's annotation was (AdditionalPropertiesAreNullable).
        var additionalPropertiesAreNullable = isProblem;
        var hasExtensionData = false;

        if (!isProblem && typeInfo.Kind == JsonTypeInfoKind.Object
            && typeInfo.Properties.FirstOrDefault(static property => property.IsExtensionData) is { } extensionData)
        {
            properties = [.. properties.Select(static property => property.IsExtensionData ? property with { IsExtensionData = false } : property)];
            hasExtensionData = true;

            var bucketValueType = OpenApiDictionaryValues.GetValueType(extensionData.PropertyType);
            additionalPropertiesAreNullable = bucketValueType is not null
                && OpenApiDictionaryValues.AreValuesNullable(extensionData.AttributeProvider as PropertyInfo, bucketValueType, nullabilityContext);
        }

        // R165: legacy listed a type's properties in the order Newtonsoft.Json's contract did, where ordinal order is the
        // rule for every other consumer.
        if (naming == OpenApiSchemaNaming.Legacy)
        {
            properties = OrderAsLegacy(properties, bodyType, typeInfo);
        }

        var propertyNames = properties
            .Select(static property => property.Name)
            .ToImmutableArray();

        // As legacy's NJsonSchema described a schema: the type's [Description], else its XML documentation's summary.
        var schemaDescription = GetTypeDescription(bodyType)
            ?? (naming == OpenApiSchemaNaming.Legacy ? OpenApiProblemDetailsDocs.GetTypeDescription(bodyType) : null);
        var schemaTitle = bodyType.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName;

        // Conditional validators are cross-property, so they belong to this schema rather than to any one of its
        // properties, and are gathered from the CLR type rather than from serializer metadata — which carries no
        // attributes of its own.
        var conditionals = ExtractSchemaConditionals(
            bodyType,
            CreateDeclaredJsonNamesByClrName(typeInfo),
            useLegacyRules: naming == OpenApiSchemaNaming.Legacy);

        return new OpenApiSchemaModel(
            bodyTypeName,
            MapKind(bodyType, typeInfo.Kind),
            true,
            propertyNames,
            properties,
            schemaDescription,
            schemaTitle,
            conditionals)
        {
            AdditionalPropertiesAreNullable = additionalPropertiesAreNullable,
            HasExtensionData = hasExtensionData,
        };
    }

    /// <summary>
    /// Extracts the cross-property conditional requirements declared by a covered body type's properties, resolved from
    /// the CLR names the validators name onto the JSON-visible names the schema component declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A requirement is projected only when both its target and its dependent are declared properties of
    /// <em>this</em> schema. Anything else is dropped rather than guessed at: the rendered component carries
    /// <c>"additionalProperties": false</c>, so a condition naming a member the schema does not declare is
    /// unsatisfiable rather than merely unhelpful, and under the negated form it would apply the requirement to every
    /// payload — blocking client-side what the server accepts.
    /// </para>
    /// <para>
    /// The result is ordered explicitly. <see cref="Type.GetProperties(BindingFlags)"/> guarantees no order, and the
    /// rendered document carries an ETag over its bytes, so the conditionals must not shuffle between builds. Target
    /// then dependent name is not a total order on its own: a scalar and an items conditional (<c>RequiredIf</c> and
    /// <c>RequiredItemsIf</c>) can share both a target and a dependent while differing only in
    /// <see cref="OpenApiConditionalTargetKind"/> — reachable because both attributes may decorate the one property —
    /// so that, then the negation flag and the dependent values themselves as a final fallback, closes the tie. The
    /// same tiebreak keys as <see cref="OpenApiValidationConstraints.ExtractConditionalRequirements"/> applies within
    /// one property, so this only has further work to do across properties that resolve to the same target and
    /// dependent JSON names.
    /// </para>
    /// </remarks>
    /// <param name="bodyType">The covered body type whose properties declare the conditional validators.</param>
    /// <param name="declaredJsonNamesByClrName">The CLR-to-JSON name map for the properties this schema declares.</param>
    /// <param name="useLegacyRules">
    /// Whether to export what legacy Blueprint's <c>RequiredIfSchemaProcessor</c> exported, in the order it did: see
    /// <see cref="OpenApiValidationConstraints.ExtractConditionalRequirements"/>. The requirements then follow the type's
    /// property order rather than being sorted.
    /// </param>
    /// <returns>The resolved conditional requirements in deterministic order; empty when none apply.</returns>
    internal static ImmutableArray<OpenApiConditionalRequirement> ExtractSchemaConditionals(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        IReadOnlyDictionary<string, string> declaredJsonNamesByClrName,
        bool useLegacyRules = false)
    {
        ArgumentNullException.ThrowIfNull(bodyType);
        ArgumentNullException.ThrowIfNull(declaredJsonNamesByClrName);

        if (declaredJsonNamesByClrName.Count == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<OpenApiConditionalRequirement>();

        foreach (var clrProperty in bodyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var requirement in OpenApiValidationConstraints.ExtractConditionalRequirements(clrProperty, useLegacyRules))
            {
                if (declaredJsonNamesByClrName.TryGetValue(requirement.TargetPropertyName, out var targetName)
                    && declaredJsonNamesByClrName.TryGetValue(requirement.DependentPropertyName, out var dependentName))
                {
                    builder.Add(requirement with
                    {
                        TargetPropertyName = targetName,
                        DependentPropertyName = dependentName,
                    });
                }
            }
        }

        // Legacy wrote them in the order it found them, the type's property order.
        if (useLegacyRules)
        {
            return builder.ToImmutable();
        }

        return
        [
            .. builder
                .OrderBy(static requirement => requirement.TargetPropertyName, StringComparer.Ordinal)
                .ThenBy(static requirement => requirement.DependentPropertyName, StringComparer.Ordinal)
                .ThenBy(static requirement => requirement.TargetKind)
                .ThenBy(static requirement => requirement.DependentValueNegated)
                .ThenBy(
                    static requirement => OpenApiValidationConstraints.FormatDependentValuesKey(requirement.DependentValues),
                    StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Puts the properties of a schema in the order legacy's document listed them (<see cref="OpenApiPropertyOrder"/>).
    /// A property that no CLR property is found for, by the serializer's own association or by name, goes last in the
    /// order it had.
    /// </summary>
    /// <param name="properties">The schema's properties.</param>
    /// <param name="bodyType">The type the schema is for.</param>
    /// <param name="typeInfo">The serializer's metadata for <paramref name="bodyType"/>.</param>
    /// <returns>The properties in legacy's order.</returns>
    private static ImmutableArray<OpenApiSchemaPropertyModel> OrderAsLegacy(
        ImmutableArray<OpenApiSchemaPropertyModel> properties,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        JsonTypeInfo typeInfo)
    {
        var order = OpenApiPropertyOrder.GetLegacyOrder(bodyType).Select(static (name, index) => (name, index)).ToDictionary(static pair => pair.name, static pair => pair.index, StringComparer.Ordinal);
        var clrNamesByJsonName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var jsonProperty in typeInfo.Properties)
        {
            if (jsonProperty.AttributeProvider is MemberInfo { Name: var clrName })
            {
                clrNamesByJsonName.TryAdd(jsonProperty.Name, clrName);
            }
        }

        int PositionOf(OpenApiSchemaPropertyModel property)
        {
            if (clrNamesByJsonName.TryGetValue(property.Name, out var clrName) && order.TryGetValue(clrName, out var position))
            {
                return position;
            }

            // A property the schema adds (a problem's extensions) is named for the CLR property it stands for.
            var byName = order.Keys.FirstOrDefault(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase));

            return byName is null ? int.MaxValue : order[byName];
        }

        return [.. properties.OrderBy(PositionOf)];
    }

    /// <summary>
    /// Maps each CLR property name on a covered body type to the JSON-visible name the rendered schema declares for it,
    /// omitting every property the schema does not declare — <c>[JsonIgnore]</c>d members, extension-data buckets, and
    /// anything else the serializer does not surface as a named property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Conditional validators name CLR properties while a schema's <c>if</c>/<c>then</c> branches must name the
    /// properties the component declares, so this map is the only bridge between the two. Its omissions are load
    /// bearing: they are what stops a requirement being projected onto a name no instance of the schema can carry.
    /// </para>
    /// <para>
    /// The map is keyed by walking <paramref name="typeInfo"/>'s own declared properties and reading each one's
    /// <see cref="JsonPropertyInfo.AttributeProvider"/> back as the <see cref="PropertyInfo"/> the serializer bound
    /// it to, rather than by recomputing a JSON name for every CLR property and testing it for membership in the
    /// declared set. Name-equality matching admits a non-serialised member whose computed JSON name happens to
    /// collide with a different declared property's name — for example a <c>[JsonIgnore]</c>d <c>Owner</c> beside a
    /// <c>[JsonPropertyName("Owner")] OwnerId</c> — and silently re-points a conditional written against the
    /// ignored member onto the unrelated one. Keying on the serializer's own property identity cannot make that
    /// substitution: the CLR property is the one STJ actually declared the member for.
    /// </para>
    /// </remarks>
    /// <param name="typeInfo">The serializer's type metadata for the covered body type to map.</param>
    /// <returns>The CLR-to-JSON name map for the properties the schema declares.</returns>
    internal static Dictionary<string, string> CreateDeclaredJsonNamesByClrName(JsonTypeInfo typeInfo)
    {
        var jsonNamesByClrName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var jsonProperty in typeInfo.Properties.Where(IsSerialised))
        {
            // Extension data is the catch-all bucket rather than a named member, so CreateComponent drops it from
            // the schema's properties. A conditional naming it would name something no instance declares.
            if (jsonProperty.IsExtensionData)
            {
                continue;
            }

            if (jsonProperty.AttributeProvider is not PropertyInfo clrProperty)
            {
                continue;
            }

            // A property shadowed with `new` appears twice under one CLR name; the first wins. Either candidate
            // reached here already resolves to a name the schema declares, so the choice between them cannot
            // introduce one it does not.
            jsonNamesByClrName.TryAdd(clrProperty.Name, jsonProperty.Name);
        }

        return jsonNamesByClrName;
    }

    /// <summary>
    /// Returns whether the serializer writes or reads <paramref name="property"/>. System.Text.Json's generated metadata
    /// keeps an always-ignored property, which it can do neither with, without a getter or a setter.
    /// </summary>
    private static bool IsSerialised(JsonPropertyInfo property)
    {
        return property.Get is not null || property.Set is not null;
    }

    private static OpenApiSchemaPropertyModel CreateProperty(
        JsonPropertyInfo property,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        string bodyTypeName,
        JsonSerializerContext serializerContext,
        IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> validationAttributesByJsonPropertyName,
        NullabilityInfoContext nullabilityContext,
        OpenApiSchemaNaming naming,
        OpenApiRequiredProperties requiredProperties,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        var propertyTypeInfo = serializerContext.GetTypeInfo(property.PropertyType);
        var propertyKind = propertyTypeInfo is null
            ? MapKind(property.PropertyType, JsonTypeInfoKind.None)
            : MapKind(property.PropertyType, propertyTypeInfo.Kind);

        // An object with a component of its own is referenced rather than described inline. The underlying type's metadata
        // decides, as a nullable struct's own metadata is its converter's, not an object's.
        var underlyingPropertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var referencedSchemaName = schemaNamesByType.TryGetValue(underlyingPropertyType, out var referenced)
            && TryGetTypeInfo(serializerContext, underlyingPropertyType)?.Kind == JsonTypeInfoKind.Object
                ? referenced
                : null;
        var validationAttributes = GetValidationAttributes(property, validationAttributesByJsonPropertyName);
        ThrowIfUnsupportedCrossPropertyValidationAttribute(bodyTypeName, property.Name, validationAttributes);
        var minimum = GetMinimum(validationAttributes, includeComparisonBounds: naming != OpenApiSchemaNaming.Legacy);
        var maximum = GetMaximum(validationAttributes, includeComparisonBounds: naming != OpenApiSchemaNaming.Legacy);
        var isNullable = IsNullableProperty(property, nullabilityContext);

        // Legacy's NJsonSchema reflected a closed generic's property from its *open* generic definition (e.g.
        // Expandable<T>.Value), whose CLR type is the type parameter T itself rather than the closed argument — and its
        // nullable-reference-type annotation on an unconstrained type parameter did not carry a resolvable nullability, so
        // legacy documented the property as non-nullable however the closed instantiation reads (Signature365 8b40ecc961
        // Portal document, ExpandableOfAzureAdGroup.value: oneOf without "nullable"). A Default consumer still wants the
        // instantiation's real nullability, so this only overrides under the legacy naming option.
        if (naming == OpenApiSchemaNaming.Legacy && IsGenericTypeParameterProperty(bodyType, (property.AttributeProvider as PropertyInfo)?.Name))
        {
            isNullable = false;
        }

        // Legacy's Newtonsoft.Json contract knew nothing of the C# required keyword, which System.Text.Json counts as
        // required, so under the legacy rule the serializer's own requirement carries over only from [JsonRequired].
        var isRequired = requiredProperties == OpenApiRequiredProperties.NonNullable
            ? property.AttributeProvider?.IsDefined(typeof(System.Text.Json.Serialization.JsonRequiredAttribute), inherit: true) == true
            : property.IsRequired;

        // Legacy required every property whose type is not nullable, and every [Required] one, which it then did not
        // document as nullable.
        if (requiredProperties == OpenApiRequiredProperties.NonNullable)
        {
            var hasRequiredAttribute = validationAttributes.Any(static attribute => attribute is RequiredAttribute);
            isRequired |= hasRequiredAttribute
                || (property.AttributeProvider is PropertyInfo clrProperty
                    ? OpenApiNullability.IsNotNullable(clrProperty, nullabilityContext)
                    : property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null);
            isNullable &= !hasRequiredAttribute;
        }

        var format = MapFormatFromValidationAttributes(validationAttributes) ?? MapFormat(property.PropertyType);
        var (enumValues, enumSchemaName) = MapEnumValues(property.PropertyType, naming, schemaNamesByType);
        var enumDescription = enumSchemaName is null ? null : GetTypeDescription(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        var description = GetPropertyDescription(property);
        var obsoleteAttr = property.AttributeProvider is MemberInfo deprecatedMember
            ? deprecatedMember.GetCustomAttribute<ObsoleteAttribute>(inherit: true)
            : null;
        var isDeprecated = obsoleteAttr is not null;
        var deprecationReason = obsoleteAttr?.Message;
        var defaultValue = property.AttributeProvider is MemberInfo defaultMember
            ? defaultMember.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>()?.Value
            : null;
        var exampleValue = property.AttributeProvider is MemberInfo exampleMember
            ? exampleMember.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiExampleAttribute>()?.Value
            : null;
        var isReadOnly = IsReadOnlyProperty(property, bodyType);
        var isWriteOnly = property.AttributeProvider is MemberInfo writeOnlyMember
            && writeOnlyMember.GetCustomAttribute<Blueprint.Model.Attributes.OpenApiWriteOnlyAttribute>() is not null;

        // Extract element type metadata for collection/dictionary properties so schema rendering
        // can emit "items" (arrays) or "additionalProperties" (dictionaries).
        // When JsonTypeInfo is available, use its Kind; otherwise fall back to CLR interface inspection.
        var (itemsKind, itemsFormat, itemsSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Enumerable, schemaNamesByType);
        var (additionalPropertiesKind, additionalPropertiesFormat, additionalPropertiesSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Dictionary, schemaNamesByType);

        // When dictionary values are arrays (e.g. Dictionary<string, string[]>), resolve the nested
        // array element type so the schema can emit "items" inside "additionalProperties".
        OpenApiSchemaKind? additionalPropertiesItemsKind = null;
        string? additionalPropertiesItemsFormat = null;
        string? additionalPropertiesItemsSchemaName = null;

        var dictValueType = additionalPropertiesKind is null ? null : ExtractElementType(property.PropertyType, JsonTypeInfoKind.Dictionary);

        if (additionalPropertiesKind == OpenApiSchemaKind.Array && dictValueType is not null)
        {
            var dictValueTypeInfo = serializerContext.GetTypeInfo(dictValueType);
            (additionalPropertiesItemsKind, additionalPropertiesItemsFormat, additionalPropertiesItemsSchemaName) =
                ResolveElementSchema(dictValueTypeInfo, dictValueType, JsonTypeInfoKind.Enumerable, schemaNamesByType);
        }

        // Legacy documented an open value as any value, and a value as nullable when its annotation said so.
        var valuesAreAnyValue = dictValueType is not null && OpenApiDictionaryValues.IsAnyValue(dictValueType);
        var valuesAreNullable = dictValueType is not null
            && !valuesAreAnyValue
            && OpenApiDictionaryValues.AreValuesNullable(property.AttributeProvider as PropertyInfo, dictValueType, nullabilityContext);

        return new OpenApiSchemaPropertyModel(
            property.Name,
            propertyKind,
            isRequired,
            property.IsExtensionData,
            isNullable,
            format,
            enumValues,
            enumSchemaName,
            description,
            OpenApiValidationConstraints.GetDocumentedMinLength(
                GetMinLength(validationAttributes),
                OpenApiValidationConstraints.IsRequiredNonEmptyString(validationAttributes),
                isNonEnumString: propertyKind == OpenApiSchemaKind.String && enumValues.IsDefaultOrEmpty && enumSchemaName is null,
                hasStringLengthAttribute: validationAttributes.Any(static attribute => attribute is StringLengthAttribute),
                useLegacyStringLengthZero: naming == OpenApiSchemaNaming.Legacy),
            GetMaxLength(validationAttributes),
            GetPattern(validationAttributes),
            minimum.Value,
            maximum.Value,
            minimum.IsExclusive,
            maximum.IsExclusive,
            itemsKind,
            itemsFormat,
            additionalPropertiesKind,
            additionalPropertiesFormat,
            itemsSchemaName,
            additionalPropertiesSchemaName,
            additionalPropertiesItemsKind,
            additionalPropertiesItemsFormat,
            additionalPropertiesItemsSchemaName,
            isDeprecated,
            deprecationReason,
            defaultValue,
            exampleValue,
            isReadOnly,
            isWriteOnly,
            SchemaName: referencedSchemaName)
        {
            AllowsAdditionalProperties = valuesAreAnyValue,
            AdditionalPropertiesAreNullable = valuesAreNullable,

            // R163: legacy wrote the items of a collection of nullable elements as a nullable reference.
            ItemsAreNullable = naming == OpenApiSchemaNaming.Legacy
                && itemsSchemaName is not null
                && OpenApiNullability.AreItemsNullable(property.AttributeProvider as PropertyInfo, nullabilityContext),
            EnumDescription = enumDescription,
        };
    }

    /// <summary>
    /// Resolves the schema kind, format, and optional element type name for element types of
    /// collections and dictionaries. The element type name is the <see cref="OpenApiSchemaNames"/>
    /// representation and is present only when the element type is a complex object that also
    /// appears as a covered schema component (i.e. has a reusable schema in the output).
    /// Prefers <see cref="JsonTypeInfo.Kind"/> when available; falls back to CLR interface
    /// inspection so that properties whose type the serializer context does not cover
    /// (e.g. <c>IDictionary&lt;string, Link&gt;</c> exposed as an interface) still emit
    /// accurate <c>items</c> or <c>additionalProperties</c> schemas.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved by the serializer context for types it covers.")]
    private static (OpenApiSchemaKind? Kind, string? Format, string? SchemaName) ResolveElementSchema(
        JsonTypeInfo? typeInfo,
        Type propertyType,
        JsonTypeInfoKind expectedKind,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        // When STJ has type info and the kind matches, use it directly.
        if (typeInfo is not null && typeInfo.Kind == expectedKind)
        {
            var elementType = ExtractElementType(typeInfo.Type, expectedKind);
            if (elementType is null)
            {
                return (null, null, null);
            }

            var kind = MapKind(elementType, JsonTypeInfoKind.None);
            var format = MapFormat(elementType);
            var schemaName = ResolveSchemaRef(elementType, kind, schemaNamesByType);
            return (kind, format, schemaName);
        }

        // Fallback: inspect the CLR type for collection/dictionary interfaces.
        // string implements IEnumerable<char> — exclude it to avoid spurious items schemas.
        if (typeInfo is null && propertyType != typeof(string))
        {
            var elementType = ExtractElementType(propertyType, expectedKind);
            if (elementType is null)
            {
                return (null, null, null);
            }

            var kind = MapKind(elementType, JsonTypeInfoKind.None);
            var format = MapFormat(elementType);
            var schemaName = ResolveSchemaRef(elementType, kind, schemaNamesByType);
            return (kind, format, schemaName);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Returns the schema component name when an element type is complex (not a primitive) AND
    /// has a component of its own (so the <c>$ref</c> target actually exists).
    /// Primitive elements (string, integer, number, boolean) are rendered inline.
    /// </summary>
    /// <remarks>
    /// The element's type, not its name, finds the component: another type of the same name has a component under a name
    /// of its own, and a name would find that one.
    /// </remarks>
    private static string? ResolveSchemaRef(Type elementType, OpenApiSchemaKind? kind, FrozenDictionary<Type, string> schemaNamesByType)
    {
        if (kind is not (null or OpenApiSchemaKind.None or OpenApiSchemaKind.Object))
        {
            return null;
        }

        return schemaNamesByType.GetValueOrDefault(elementType);
    }

    /// <summary>
    /// Extracts the element type from a collection or dictionary CLR type by inspecting generic interfaces.
    /// For enumerables, returns the <c>T</c> in <c>IEnumerable&lt;T&gt;</c>.
    /// For dictionaries, returns the <c>TValue</c> in <c>IDictionary&lt;TKey, TValue&gt;</c>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved by the serializer context for types it covers.")]
    private static Type? ExtractElementType(Type type, JsonTypeInfoKind kind)
    {
        if (kind == JsonTypeInfoKind.Dictionary)
        {
            // The type itself (e.g. IDictionary<string, Link>) or an implemented interface, read-only ones included.
            return OpenApiDictionaryValues.GetValueType(type);
        }

        if (kind == JsonTypeInfoKind.Enumerable)
        {
            // Check the type itself first (e.g. IEnumerable<string>), then its implemented interfaces.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }

            var enumInterface = type.GetInterfaces()
                .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumInterface?.GetGenericArguments()[0];
        }

        return null;
    }

    /// <summary>
    /// Maps a CLR property type to its OpenAPI <c>format</c> hint (e.g. <c>int32</c>, <c>date-time</c>, <c>uuid</c>).
    /// Returns <c>null</c> when no standard format applies. For <c>IParsable&lt;T&gt;</c> types, infers
    /// format from the single backing field when present (e.g. a wrapper around <see cref="Guid"/> → <c>"uuid"</c>).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "IParsable types used in operation properties are preserved by the serializer context registration.")]
    private static string? MapFormat(Type propertyType)
    {
        // byte[] maps to base64-encoded binary before Nullable unwrapping.
        if (propertyType == typeof(byte[]))
        {
            return "byte";
        }

        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying == typeof(int))
        {
            return "int32";
        }

        if (underlying == typeof(long))
        {
            return "int64";
        }

        if (underlying == typeof(float))
        {
            return "float";
        }

        if (underlying == typeof(double))
        {
            return "double";
        }

        if (underlying == typeof(decimal))
        {
            return "double";
        }

        if (underlying == typeof(DateTime))
        {
            return "date-time";
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return "date-time";
        }

        if (underlying == typeof(DateOnly))
        {
            return "date";
        }

        if (underlying == typeof(TimeOnly))
        {
            return "time";
        }

        if (underlying == typeof(Guid))
        {
            return "uuid";
        }

        if (underlying == typeof(Uri))
        {
            return "uri";
        }

        if (underlying == typeof(byte))
        {
            return "int32";
        }

        if (underlying == typeof(short))
        {
            return "int32";
        }

        if (underlying == typeof(sbyte))
        {
            return "int32";
        }

        if (underlying == typeof(ushort))
        {
            return "int32";
        }

        if (underlying == typeof(uint))
        {
            return "int64";
        }

        if (underlying == typeof(ulong))
        {
            return "int64";
        }

        if (underlying == typeof(TimeSpan))
        {
            return "duration";
        }

        if (underlying == typeof(Half))
        {
            return "float";
        }

        // NodaTime types serialise as ISO-8601 strings via NodaTime.Serialization.SystemTextJson.
        // Matched by full name so Blueprint.Runtime does not need a NodaTime package reference.
        var fullName = underlying.FullName;
        if (fullName is not null && fullName.StartsWith("NodaTime.", StringComparison.Ordinal))
        {
            return fullName switch
            {
                "NodaTime.Instant" => "date-time",
                "NodaTime.OffsetDateTime" => "date-time",
                "NodaTime.ZonedDateTime" => "date-time",
                "NodaTime.LocalDateTime" => "date-time",
                "NodaTime.LocalDate" => "date",
                "NodaTime.LocalTime" => "time",
                "NodaTime.Duration" => "duration",
                "NodaTime.Period" => "duration",
                "NodaTime.Offset" => "duration",
                _ => null,
            };
        }

        // IParsable<T> types serialise as strings; infer format from a single wrapped field.
        if (underlying.GetInterfaces().Any(
                static i => i.IsGenericType && i.Name.StartsWith("IParsable`", StringComparison.Ordinal)))
        {
            var instanceFields = underlying.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (instanceFields.Length == 1)
            {
                var wrappedType = instanceFields[0].FieldType;
                if (wrappedType == typeof(Guid))
                {
                    return "uuid";
                }

                if (wrappedType == typeof(int))
                {
                    return "int32";
                }

                if (wrappedType == typeof(long))
                {
                    return "int64";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts an OpenAPI format hint from well-known validation attributes. These override the
    /// CLR type-based format when present, enabling <c>string</c> properties annotated with
    /// <c>[EmailAddress]</c>, <c>[Phone]</c>, <c>[Url]</c>, or <c>[CreditCard]</c> to carry the
    /// correct semantic format in the generated schema.
    /// </summary>
    private static string? MapFormatFromValidationAttributes(IReadOnlyList<ValidationAttribute> validationAttributes)
    {
        foreach (var attribute in validationAttributes)
        {
            switch (attribute)
            {
                case System.ComponentModel.DataAnnotations.EmailAddressAttribute:
                case Blueprint.Validation.EmailAddressAttribute:
                    return "email";
                case PhoneAttribute:
                    return "phone";
                case System.ComponentModel.DataAnnotations.UrlAttribute:
                case Blueprint.Validation.UrlAttribute:
                    return "uri";
                case CreditCardAttribute:
                    return "credit-card";
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the enum member names when a property's CLR type is an enum (unwrapping <c>Nullable&lt;T&gt;</c>),
    /// or an empty array when the type is not an enum.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067",
        Justification = "Enum types used in STJ-serializable operations are preserved by the generated serializer context.")]
    private static (ImmutableArray<string> Values, string? SchemaName) MapEnumValues(
        Type propertyType,
        OpenApiSchemaNaming naming,
        FrozenDictionary<Type, string> schemaNamesByType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (!underlying.IsEnum)
        {
            return (ImmutableArray<string>.Empty, null);
        }

        // The projection's own walk already named every enum it reached through the same SchemaNamer as every object
        // type, so two distinct enums of one simple name are told apart (X, X2) rather than collapsed onto the first
        // one's schema — an enum this walk did not reach (a query/route parameter's, with no body/response use) falls
        // back to the bare formatted name, as before.
        var schemaName = schemaNamesByType.TryGetValue(underlying, out var assignedName)
            ? assignedName
            : OpenApiSchemaNames.Format(underlying, naming);

        return (Enum.GetNames(underlying).ToImmutableArray(), schemaName);
    }

    /// <summary>
    /// Returns a body property's description: its underlying CLR member's <see cref="DescriptionAttribute"/>, else the
    /// summary of its XML documentation, as legacy's NJsonSchema took it; <c>null</c> when it has neither.
    /// </summary>
    private static string? GetPropertyDescription(JsonPropertyInfo property)
    {
        if (property.AttributeProvider is not MemberInfo member)
        {
            return null;
        }

        return member.GetCustomAttribute<DescriptionAttribute>()?.Description ?? OpenApiXmlDocs.GetSchemaSummary(member);
    }

    /// <summary>
    /// Returns a type's description as legacy's NJsonSchema took it: its <see cref="DescriptionAttribute"/>, else the summary
    /// of its XML documentation; <c>null</c> when it has neither.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The description, or <c>null</c>.</returns>
    internal static string? GetTypeDescription(Type type)
    {
        return type.GetCustomAttribute<DescriptionAttribute>()?.Description ?? OpenApiXmlDocs.GetSchemaSummary(type);
    }

    /// <summary>
    /// Determines whether a property is read-only — either because the CLR property has no public setter or
    /// because it carries <c>[ReadOnly(true)]</c>. Read-only properties are surfaced as <c>"readOnly": true</c>
    /// in the OpenAPI schema so client generators produce immutable DTOs.
    /// </summary>
    private static bool IsReadOnlyProperty(JsonPropertyInfo property, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType)
    {
        // Prefer the explicit CLR property info from the AttributeProvider; fall back to
        // looking up by name from the declaring type (necessary for source-generated
        // serializer contexts where AttributeProvider may be null).
        var clrProperty = property.AttributeProvider as PropertyInfo
            ?? bodyType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);

        if (clrProperty is null)
        {
            return false;
        }

        // Explicit [ReadOnly(true)] attribute takes precedence.
        var readOnlyAttr = clrProperty.GetCustomAttribute<System.ComponentModel.ReadOnlyAttribute>();
        if (readOnlyAttr is { IsReadOnly: true })
        {
            return true;
        }

        // No public setter means the property is read-only from the consumer's perspective.
        return clrProperty.GetSetMethod() is null;
    }

    /// <summary>
    /// Determines whether a body property is nullable by checking <c>Nullable&lt;T&gt;</c> for value types
    /// and NRT annotations (via <see cref="NullabilityInfoContext"/>) for reference types.
    /// </summary>
    private static bool IsNullableProperty(JsonPropertyInfo property, NullabilityInfoContext nullabilityContext)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        // For reference types, check NRT annotation via the underlying CLR member.
        if (property.AttributeProvider is PropertyInfo clrProperty)
        {
            var nullabilityInfo = nullabilityContext.Create(clrProperty);

            return nullabilityInfo.WriteState == NullabilityState.Nullable
                   || nullabilityInfo.ReadState == NullabilityState.Nullable;
        }

        // When no CLR property info is available, default to non-nullable to match the OpenAPI 3.0
        // convention that properties without "nullable: true" are non-nullable.
        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="propertyName"/>, on the *open* generic type definition of
    /// <paramref name="bodyType"/> (e.g. <c>Expandable&lt;&gt;</c> for a closed <c>Expandable&lt;AzureAdGroup&gt;</c>),
    /// is itself declared as a generic type parameter (e.g. <c>T? Value</c>) rather than a concrete or constructed type.
    /// </summary>
    /// <remarks>
    /// <paramref name="propertyName"/> is the CLR property's name, not its serialised one, which a naming policy changes
    /// (<c>value</c> for <c>Value</c>); it is <see langword="null"/> when the serializer metadata has no CLR property.
    /// <see cref="PropertyInfo.PropertyType"/> on a closed generic's property is always the closed argument
    /// (<c>AzureAdGroup</c>), never the parameter itself, so this must be checked on the open definition's own property.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "The open generic type definition of a covered closed generic body type is preserved alongside it.")]
    private static bool IsGenericTypeParameterProperty(Type bodyType, string? propertyName)
    {
        if (propertyName is null || !bodyType.IsGenericType || bodyType.IsGenericTypeDefinition)
        {
            return false;
        }

        var definitionProperty = bodyType.GetGenericTypeDefinition()
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        return definitionProperty?.PropertyType.IsGenericParameter == true;
    }

    private static int? GetMinLength(IEnumerable<ValidationAttribute> validationAttributes)
    {
        int? minLength = null;

        foreach (var attribute in validationAttributes)
        {
            switch (attribute)
            {
                case StringLengthAttribute stringLengthAttribute when stringLengthAttribute.MinimumLength > 0:
                    minLength = minLength.HasValue
                        ? Math.Max(minLength.Value, stringLengthAttribute.MinimumLength)
                        : stringLengthAttribute.MinimumLength;
                    break;
                case MinLengthAttribute minLengthAttribute when minLengthAttribute.Length >= 0:
                    minLength = minLength.HasValue
                        ? Math.Max(minLength.Value, minLengthAttribute.Length)
                        : minLengthAttribute.Length;
                    break;
                case LengthAttribute lengthAttribute when lengthAttribute.MinimumLength > 0:
                    minLength = minLength.HasValue
                        ? Math.Max(minLength.Value, lengthAttribute.MinimumLength)
                        : lengthAttribute.MinimumLength;
                    break;
            }
        }

        return minLength;
    }

    private static int? GetMaxLength(IEnumerable<ValidationAttribute> validationAttributes)
    {
        int? maxLength = null;

        foreach (var attribute in validationAttributes)
        {
            var candidate = attribute switch
            {
                StringLengthAttribute stringLengthAttribute when stringLengthAttribute.MaximumLength != int.MaxValue => stringLengthAttribute.MaximumLength,
                MaxLengthAttribute maxLengthAttribute when maxLengthAttribute.Length >= 0 => maxLengthAttribute.Length,
                LengthAttribute lengthAttribute when lengthAttribute.MaximumLength != int.MaxValue => lengthAttribute.MaximumLength,
                _ => (int?)null,
            };

            if (!candidate.HasValue)
            {
                continue;
            }

            maxLength = maxLength.HasValue
                ? Math.Min(maxLength.Value, candidate.Value)
                : candidate.Value;
        }

        return maxLength;
    }

    private static string? GetPattern(IEnumerable<ValidationAttribute> validationAttributes)
    {
        foreach (var attr in validationAttributes)
        {
            if (attr is RegularExpressionAttribute regex)
            {
                var pattern = regex.Pattern;
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    return pattern;
                }
            }
        }

        return null;
    }

    private static (decimal? Value, bool IsExclusive) GetMinimum(IEnumerable<ValidationAttribute> validationAttributes, bool includeComparisonBounds)
    {
        decimal? minimum = null;
        var isExclusive = false;

        foreach (var attribute in validationAttributes)
        {
            switch (attribute)
            {
                case RangeAttribute rangeAttribute:
                    ApplyLowerBound(ref minimum, ref isExclusive, ConvertToDecimal(rangeAttribute.Minimum), isExclusiveCandidate: false);
                    break;
                case GreaterThanAttribute greaterThanAttribute when includeComparisonBounds:
                    ApplyLowerBound(ref minimum, ref isExclusive, greaterThanAttribute.MinimumValue, isExclusiveCandidate: true);
                    break;
            }
        }

        return (minimum, isExclusive);
    }

    private static (decimal? Value, bool IsExclusive) GetMaximum(IEnumerable<ValidationAttribute> validationAttributes, bool includeComparisonBounds)
    {
        decimal? maximum = null;
        var isExclusive = false;

        foreach (var attribute in validationAttributes)
        {
            switch (attribute)
            {
                case RangeAttribute rangeAttribute:
                    ApplyUpperBound(ref maximum, ref isExclusive, ConvertToDecimal(rangeAttribute.Maximum), isExclusiveCandidate: false);
                    break;
                case LessThanAttribute lessThanAttribute when includeComparisonBounds:
                    ApplyUpperBound(ref maximum, ref isExclusive, lessThanAttribute.MaximumValue, isExclusiveCandidate: true);
                    break;
            }
        }

        return (maximum, isExclusive);
    }

    private static IReadOnlyList<ValidationAttribute> GetValidationAttributes(
        JsonPropertyInfo property,
        IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> validationAttributesByJsonPropertyName)
    {
        if (property.AttributeProvider?.GetCustomAttributes(inherit: true) is { } allAttrs
            && OpenApiValidationConstraints.FilterValidationAttributes(allAttrs) is { Count: > 0 } attributes)
        {
            return attributes;
        }

        return validationAttributesByJsonPropertyName.TryGetValue(property.Name, out var mappedAttributes)
            ? mappedAttributes
            : [];
    }

    private static void ThrowIfUnsupportedCrossPropertyValidationAttribute(
        string bodyTypeName,
        string propertyName,
        IReadOnlyList<ValidationAttribute> validationAttributes)
    {
        var unsupportedAttribute = validationAttributes.FirstOrDefault(static attribute =>
            attribute is CompareAttribute
            || string.Equals(attribute.GetType().FullName, typeof(GreaterThanPropertyAttribute).FullName, StringComparison.Ordinal)
            || string.Equals(attribute.GetType().FullName, typeof(LessThanPropertyAttribute).FullName, StringComparison.Ordinal));

        if (unsupportedAttribute is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The covered OpenAPI body type '{bodyTypeName}' property '{propertyName}' uses validation attribute '{unsupportedAttribute.GetType().Name}', which the current OpenAPI schema projection cannot represent.");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI schema projection only reflects over app-owned serializer-covered body types whose public properties are intentionally preserved by the serializer context contract.")]
    private static IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> CreateValidationAttributesByJsonPropertyName(
        Type bodyType,
        JsonSerializerContext serializerContext)
    {
        return bodyType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .ToFrozenDictionary(
                property => GetJsonPropertyName(property, serializerContext.Options),
                static property => (IReadOnlyList<ValidationAttribute>)OpenApiValidationConstraints.FilterValidationAttributes(property.GetCustomAttributes(inherit: true)),
                StringComparer.Ordinal);
    }

    private static string GetJsonPropertyName(PropertyInfo property, JsonSerializerOptions serializerOptions)
    {
        return property.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name
            ?? serializerOptions.PropertyNamingPolicy?.ConvertName(property.Name)
            ?? property.Name;
    }

    private static void ApplyLowerBound(ref decimal? currentValue, ref bool currentExclusive, decimal? candidateValue, bool isExclusiveCandidate)
    {
        if (!candidateValue.HasValue)
        {
            return;
        }

        if (!currentValue.HasValue || candidateValue.Value > currentValue.Value)
        {
            currentValue = candidateValue.Value;
            currentExclusive = isExclusiveCandidate;
            return;
        }

        if (candidateValue.Value == currentValue.Value)
        {
            currentExclusive |= isExclusiveCandidate;
        }
    }

    private static void ApplyUpperBound(ref decimal? currentValue, ref bool currentExclusive, decimal? candidateValue, bool isExclusiveCandidate)
    {
        if (!candidateValue.HasValue)
        {
            return;
        }

        if (!currentValue.HasValue || candidateValue.Value < currentValue.Value)
        {
            currentValue = candidateValue.Value;
            currentExclusive = isExclusiveCandidate;
            return;
        }

        if (candidateValue.Value == currentValue.Value)
        {
            currentExclusive |= isExclusiveCandidate;
        }
    }

    private static decimal? ConvertToDecimal(object? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Type interfaces are preserved by the serializer context for types it covers.")]
    private static OpenApiSchemaKind MapKind(Type bodyType, JsonTypeInfoKind kind)
    {
        var nonNullableType = Nullable.GetUnderlyingType(bodyType) ?? bodyType;

        // The serializer writes a byte array as a base64 string, and legacy documented it as one.
        if (nonNullableType == typeof(string)
            || nonNullableType == typeof(byte[])
            || nonNullableType == typeof(char)
            || nonNullableType == typeof(DateOnly)
            || nonNullableType == typeof(TimeOnly)
            || nonNullableType == typeof(DateTime)
            || nonNullableType == typeof(DateTimeOffset)
            || nonNullableType == typeof(Guid)
            || nonNullableType == typeof(Uri)
            || typeof(Type).IsAssignableFrom(nonNullableType))
        {
            return OpenApiSchemaKind.String;
        }

        // NodaTime temporal types serialize as ISO-8601 strings with NodaTime.Serialization.SystemTextJson.
        // Match by full name so Blueprint.Runtime doesn't need a NodaTime package reference.
        var fullName = nonNullableType.FullName;
        if (fullName is not null && fullName.StartsWith("NodaTime.", StringComparison.Ordinal))
        {
            return fullName switch
            {
                "NodaTime.Instant" or "NodaTime.OffsetDateTime" or "NodaTime.ZonedDateTime"
                    or "NodaTime.LocalDateTime" or "NodaTime.LocalDate" or "NodaTime.LocalTime"
                    or "NodaTime.Duration" or "NodaTime.Period" or "NodaTime.Offset" => OpenApiSchemaKind.String,
                _ => OpenApiSchemaKind.Object,
            };
        }

        if (nonNullableType == typeof(bool))
        {
            return OpenApiSchemaKind.Boolean;
        }

        if (nonNullableType == typeof(byte)
            || nonNullableType == typeof(short)
            || nonNullableType == typeof(int)
            || nonNullableType == typeof(long)
            || nonNullableType == typeof(sbyte)
            || nonNullableType == typeof(ushort)
            || nonNullableType == typeof(uint)
            || nonNullableType == typeof(ulong))
        {
            return OpenApiSchemaKind.Integer;
        }

        if (nonNullableType == typeof(float)
            || nonNullableType == typeof(double)
            || nonNullableType == typeof(decimal))
        {
            return OpenApiSchemaKind.Number;
        }

        // When STJ has no metadata (kind == None), infer the shape from CLR interfaces.
        if (kind == JsonTypeInfoKind.None)
        {
            if (OpenApiDictionaryValues.GetValueType(nonNullableType) is not null)
            {
                return OpenApiSchemaKind.Object;
            }

            if (nonNullableType != typeof(string)
                && ((nonNullableType.IsGenericType && nonNullableType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    || nonNullableType.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))))
            {
                return OpenApiSchemaKind.Array;
            }

            // A type a converter writes, such as a strongly typed id, whose type converter converts it to string is
            // documented as the string legacy documented it as.
            if (!nonNullableType.IsEnum && OpenApiStringConvertibleTypes.IsConvertibleToString(nonNullableType))
            {
                return OpenApiSchemaKind.String;
            }
        }

        return kind switch
        {
            JsonTypeInfoKind.None => OpenApiSchemaKind.None,
            JsonTypeInfoKind.Object => OpenApiSchemaKind.Object,
            JsonTypeInfoKind.Enumerable => OpenApiSchemaKind.Array,
            JsonTypeInfoKind.Dictionary => OpenApiSchemaKind.Object,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported serializer metadata kind."),
        };
    }

    /// <summary>
    /// Produces a fully-qualified type name using the same angle-bracket format that Roslyn's
    /// <c>ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)</c> produces. This bridges the
    /// mismatch between <see cref="Type.FullName"/> (backtick notation for generics) and the
    /// source generator's compile-time body type names (angle-bracket notation).
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The type's name as the source generator writes it.</returns>
    internal static string FormatFullNameDisplayStyle(Type type)
    {
        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        var definition = type.GetGenericTypeDefinition().FullName!.Replace('+', '.');
        var backtickIdx = definition.IndexOf('`');
        var baseName = backtickIdx > 0 ? definition[..backtickIdx] : definition;
        var args = string.Join(", ", type.GetGenericArguments().Select(FormatFullNameDisplayStyle));
        return $"{baseName}<{args}>";
    }

    /// <summary>
    /// Names each CLR type the projection meets, and no two alike: types can share a name — a nested type's simple
    /// name, a generic over types that do — and each needs a schema of its own.
    /// </summary>
    /// <remarks>
    /// A type whose name another has taken is given it with the first free counter from 2 appended, as legacy's NJsonSchema
    /// did, and the name it would have had is recorded. Which of the types that share a name keeps it is not settled here,
    /// in the order the projection happens to meet them, but when the document is written, from the recorded names, in the
    /// order the document reaches the types (see <c>OpenApiOutputRenderingContext</c>).
    /// </remarks>
    private sealed class SchemaNamer(OpenApiSchemaNaming naming)
    {
        private readonly Dictionary<Type, string> _namesByType = [];

        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _baseNames = new(StringComparer.Ordinal);

        /// <summary>Gets the name each type named apart from another would have had.</summary>
        public IReadOnlyDictionary<string, string> BaseNames => this._baseNames;

        /// <summary>Returns the name of <paramref name="type"/>, naming it on first sight.</summary>
        /// <param name="type">The CLR type.</param>
        /// <returns>The type's name, which no other type the namer has named shares.</returns>
        public string NameOf(Type type)
        {
            if (this._namesByType.TryGetValue(type, out var name))
            {
                return name;
            }

            var baseName = OpenApiSchemaNames.Format(type, naming);

            // An array body type is found by its element's name with "[]" appended, so an array of an element named apart
            // is named for it.
            if (type.IsArray && type.GetElementType() is { } elementType && this._baseNames.ContainsKey(this.NameOf(elementType)))
            {
                baseName = this.NameOf(elementType) + "[]";
            }

            name = baseName;

            for (var counter = 2; !this._names.Add(name); counter++)
            {
                name = baseName + counter.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.Equals(name, baseName, StringComparison.Ordinal))
            {
                this._baseNames[name] = baseName;
            }

            this._namesByType[type] = name;

            return name;
        }
    }
}

/// <summary>
/// Captures the currently projected schema facts for one covered serializer body type.
/// </summary>
/// <param name="BodyTypeName">The stable CLR body-type name used by the surrounding OpenAPI seams.</param>
/// <param name="Kind">The broad schema shape derived from serializer metadata.</param>
/// <param name="IsCoveredBySerializerContext">Whether the serializer context explicitly covers this type.</param>
/// <param name="PropertyNames">The ordered JSON-visible property names when the serializer metadata exposes an object shape.</param>
/// <param name="Properties">The ordered JSON-visible property metadata projected from serializer metadata.</param>
/// <param name="Description">The schema-level description from <see cref="System.ComponentModel.DescriptionAttribute"/> when specified.</param>
/// <param name="Title">The schema-level title from <see cref="System.ComponentModel.DisplayNameAttribute"/> when specified.</param>
/// <param name="Conditionals">The cross-property conditional requirements this schema's properties declare, resolved to
/// the JSON-visible names the schema declares. Empty when no property carries a conditional validator.</param>
public sealed record OpenApiSchemaModel(
    string BodyTypeName,
    OpenApiSchemaKind Kind,
    bool IsCoveredBySerializerContext,
    ImmutableArray<string> PropertyNames,
    ImmutableArray<OpenApiSchemaPropertyModel> Properties,
    string? Description = null,
    string? Title = null,
    ImmutableArray<OpenApiConditionalRequirement> Conditionals = default)
{
    /// <summary>
    /// Gets a value indicating whether the schema's extension data members may be null, which the schema documents with
    /// <c>additionalProperties: {"nullable": true}</c>.
    /// </summary>
    public bool AdditionalPropertiesAreNullable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the type has a <c>[JsonExtensionData]</c> bucket that is documented as a property of
    /// its own, as legacy documented it, so that no property is marked <see cref="OpenApiSchemaPropertyModel.IsExtensionData"/>
    /// while the schema is still open to further members.
    /// </summary>
    public bool HasExtensionData { get; init; }
}

/// <summary>
/// Captures one JSON-visible property projected from serializer metadata for a covered schema.
/// </summary>
/// <param name="Name">The JSON property name.</param>
/// <param name="Kind">The broad schema shape for the property payload.</param>
/// <param name="IsRequired">Whether serializer metadata marks the property as required.</param>
/// <param name="IsExtensionData">Whether the property acts as JSON extension data.</param>
/// <param name="IsNullable">Whether this property is nullable (NRT-annotated reference type or <c>Nullable&lt;T&gt;</c>).</param>
/// <param name="Format">The OpenAPI format hint (e.g. <c>int32</c>, <c>date-time</c>, <c>uuid</c>) when the CLR type has a standard mapping.</param>
/// <param name="EnumValues">The enum member names when the property is an enum type, or empty when not an enum.</param>
/// <param name="EnumSchemaName">The simplified CLR enum type name used as a component schema key for <c>$ref</c>-based enum rendering, or null when the property is not an enum.</param>
/// <param name="Description">The property description from <c>[Description]</c> attribute when specified.</param>
/// <param name="MinLength">The inclusive minimum string/collection length when validation attributes expose one.</param>
/// <param name="MaxLength">The inclusive maximum string/collection length when validation attributes expose one.</param>
/// <param name="Pattern">The regular-expression pattern when validation attributes expose one.</param>
/// <param name="Minimum">The numeric lower bound when validation attributes expose one.</param>
/// <param name="Maximum">The numeric upper bound when validation attributes expose one.</param>
/// <param name="IsMinimumExclusive">Whether the numeric lower bound is exclusive.</param>
/// <param name="IsMaximumExclusive">Whether the numeric upper bound is exclusive.</param>
/// <param name="ItemsKind">The schema kind for array element items when the property is an array/enumerable.</param>
/// <param name="ItemsFormat">The OpenAPI format of array element items when they have a standard format mapping.</param>
/// <param name="AdditionalPropertiesKind">The schema kind for dictionary values when the property is a dictionary/map.</param>
/// <param name="AdditionalPropertiesFormat">The OpenAPI format of dictionary values when they have a standard format mapping.</param>
/// <param name="ItemsSchemaName">The component schema name for complex array element types, enabling <c>$ref</c> rendering.</param>
/// <param name="AdditionalPropertiesSchemaName">The component schema name for complex dictionary value types, enabling <c>$ref</c> rendering.</param>
/// <param name="AdditionalPropertiesItemsKind">The schema kind for items within dictionary values when values are arrays (e.g. <c>String</c> for <c>Dictionary&lt;string, string[]&gt;</c>).</param>
/// <param name="AdditionalPropertiesItemsFormat">The OpenAPI format of items within dictionary values when the nested items have a standard format.</param>
/// <param name="AdditionalPropertiesItemsSchemaName">The component schema name for complex items within dictionary array values, enabling nested <c>$ref</c> rendering.</param>
/// <param name="IsDeprecated">Whether the property is deprecated (mapped from <see cref="ObsoleteAttribute"/>).</param>
/// <param name="DeprecationReason">The deprecation reason from <see cref="ObsoleteAttribute.Message"/>, or null.</param>
/// <param name="DefaultValue">The default value from <see cref="System.ComponentModel.DefaultValueAttribute"/>, or null.</param>
/// <param name="ExampleValue">A representative example value from <see cref="Blueprint.Model.Attributes.OpenApiExampleAttribute"/>, or null.</param>
/// <param name="IsReadOnly">Whether this property is read-only (no public setter or annotated with <c>[ReadOnly(true)]</c>).</param>
/// <param name="IsWriteOnly">Whether this property is write-only (annotated with <c>[OpenApiWriteOnly]</c>, e.g. a password field).</param>
/// <param name="SchemaName">The component schema name of the object the property holds, when that object has a component of its
/// own, enabling <c>$ref</c> rendering instead of a bare <c>object</c>.</param>
public sealed record OpenApiSchemaPropertyModel(
    string Name,
    OpenApiSchemaKind Kind,
    bool IsRequired,
    bool IsExtensionData,
    bool IsNullable,
    string? Format,
    ImmutableArray<string> EnumValues,
    string? EnumSchemaName,
    string? Description,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    bool IsMinimumExclusive = false,
    bool IsMaximumExclusive = false,
    OpenApiSchemaKind? ItemsKind = null,
    string? ItemsFormat = null,
    OpenApiSchemaKind? AdditionalPropertiesKind = null,
    string? AdditionalPropertiesFormat = null,
    string? ItemsSchemaName = null,
    string? AdditionalPropertiesSchemaName = null,
    OpenApiSchemaKind? AdditionalPropertiesItemsKind = null,
    string? AdditionalPropertiesItemsFormat = null,
    string? AdditionalPropertiesItemsSchemaName = null,
    bool IsDeprecated = false,
    string? DeprecationReason = null,
    object? DefaultValue = null,
    object? ExampleValue = null,
    bool IsReadOnly = false,
    bool IsWriteOnly = false,
    string? SchemaName = null)
{
    /// <summary>
    /// Gets a value indicating whether the property is an object open to members of any value, written with
    /// <c>additionalProperties: {}</c>.
    /// </summary>
    public bool AllowsAdditionalProperties { get; init; }

    /// <summary>
    /// Gets a value indicating whether the property is a collection of references to a component whose elements may be
    /// null, written with <c>"nullable": true</c> beside a <c>oneOf</c> wrapping the items' <c>$ref</c>.
    /// </summary>
    public bool ItemsAreNullable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the property is a dictionary whose values may be null, written with
    /// <c>"nullable": true</c> in its <c>additionalProperties</c>.
    /// </summary>
    public bool AdditionalPropertiesAreNullable { get; init; }

    /// <summary>
    /// Gets the description of the enum type the property references, which its component carries, or
    /// <see langword="null"/> when it is not an enum or has none.
    /// </summary>
    public string? EnumDescription { get; init; }
}

/// <summary>
/// Describes the broad schema shape currently projected from serializer metadata.
/// </summary>
public enum OpenApiSchemaKind
{
    /// <summary>
    /// The serializer metadata did not expose a richer schema shape.
    /// </summary>
    None = 0,

    /// <summary>
    /// The serializer metadata describes an object/document shape.
    /// </summary>
    Object = 1,

    /// <summary>
    /// The serializer metadata describes an array or enumerable shape.
    /// </summary>
    Array = 2,

    /// <summary>
    /// The serializer metadata describes a string scalar.
    /// </summary>
    String = 3,

    /// <summary>
    /// The serializer metadata describes an integer scalar.
    /// </summary>
    Integer = 4,

    /// <summary>
    /// The serializer metadata describes a floating-point or decimal scalar.
    /// </summary>
    Number = 5,

    /// <summary>
    /// The serializer metadata describes a boolean scalar.
    /// </summary>
    Boolean = 6,
}
