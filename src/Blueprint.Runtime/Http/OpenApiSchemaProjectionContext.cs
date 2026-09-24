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
        ArgumentNullException.ThrowIfNull(document);

        if (document.App.AppOwnedSerializerContext is not JsonSerializerContext serializerContext)
        {
            return new OpenApiSchemaProjectionContext(document, ImmutableArray<OpenApiSchemaModel>.Empty, FrozenDictionary<string, string>.Empty)
            {
                Naming = naming,
            };
        }

        var coveredTypesByName = IndexByName(document.App.AppOwnedSerializerContextBodyTypes, naming);

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
            var shortName = OpenApiSchemaNames.Format(bodyType, naming);
            var fullName = bodyType.FullName ?? bodyType.Name;
            fullNameToShortNameBuilder.TryAdd(fullName, shortName);

            if (bodyType.IsGenericType)
            {
                // Source generators produce angle-bracket format names via ToDisplayString.
                // Include this as an alternate key so the lookup succeeds for either format.
                var displayStyleName = FormatFullNameDisplayStyle(bodyType);
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

        var schemaBuilder = ImmutableArray.CreateBuilder<OpenApiSchemaModel>();

        foreach (var bodyTypeName in coveredBodyTypeNames)
        {
            if (coveredTypesByName.TryGetValue(bodyTypeName, out var bodyType))
            {
                try
                {
                    schemaBuilder.Add(CreateSchema(bodyTypeName, bodyType, serializerContext, coveredTypesByName, naming));
                }
                catch (NotSupportedException)
                {
                    // Types with custom JsonConverters that STJ source generation cannot handle
                    // (SYSLIB1220) will throw when GetTypeInfo tries to configure their property
                    // graph. Skip these types from OpenAPI schema projection — they still serialize
                    // at runtime via the reflection fallback in HttpOperationResultExecutor.
                }
            }
        }

        return new OpenApiSchemaProjectionContext(document, schemaBuilder.ToImmutable(), fullNameToShortName)
        {
            Naming = naming,
        };
    }

    /// <summary>
    /// Gets how the schemas in this projection are named from their CLR types, which every other name the document
    /// derives from a type (a request body's, an enum's) follows too.
    /// </summary>
    public OpenApiSchemaNaming Naming { get; init; }

    private static FrozenDictionary<string, Type> IndexByName(IEnumerable<Type> types, OpenApiSchemaNaming naming)
    {
        var typesByName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var name = OpenApiSchemaNames.Format(type, naming);

            if (!typesByName.TryAdd(name, type) && typesByName[name] != type)
            {
                throw new InvalidOperationException(
                    $"The types '{typesByName[name].FullName}' and '{type.FullName}' are both named '{name}' in the OpenAPI document, " +
                    "which cannot hold both under one key. Rename one of them, or give one a name of its own with [OpenApiSchemaName].");
            }
        }

        return typesByName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static OpenApiSchemaModel CreateSchema(
        string bodyTypeName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        JsonSerializerContext serializerContext,
        FrozenDictionary<string, Type> coveredTypesByName,
        OpenApiSchemaNaming naming)
    {
        var typeInfo = serializerContext.GetTypeInfo(bodyType)
            ?? throw new InvalidOperationException($"The serializer context did not expose metadata for covered body type '{bodyTypeName}'.");
        var validationAttributesByJsonPropertyName = CreateValidationAttributesByJsonPropertyName(bodyType, serializerContext);

        // Create once per schema rather than per-property to avoid repeated allocations.
        var nullabilityContext = new NullabilityInfoContext();

        var properties = typeInfo.Kind == JsonTypeInfoKind.Object
            ? typeInfo.Properties
                .Select(property => CreateProperty(property, bodyType, bodyTypeName, serializerContext, validationAttributesByJsonPropertyName, nullabilityContext, coveredTypesByName, naming))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OpenApiSchemaPropertyModel>.Empty;
        var propertyNames = properties
            .Select(static property => property.Name)
            .ToImmutableArray();

        var schemaDescription = bodyType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var schemaTitle = bodyType.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName;

        // Conditional validators are cross-property, so they belong to this schema rather than to any one of its
        // properties, and are gathered from the CLR type rather than from serializer metadata — which carries no
        // attributes of its own.
        var conditionals = ExtractSchemaConditionals(
            bodyType,
            CreateDeclaredJsonNamesByClrName(typeInfo));

        return new OpenApiSchemaModel(
            bodyTypeName,
            MapKind(bodyType, typeInfo.Kind),
            true,
            propertyNames,
            properties,
            schemaDescription,
            schemaTitle,
            conditionals);
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
    /// <returns>The resolved conditional requirements in deterministic order; empty when none apply.</returns>
    internal static ImmutableArray<OpenApiConditionalRequirement> ExtractSchemaConditionals(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        IReadOnlyDictionary<string, string> declaredJsonNamesByClrName)
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
            foreach (var requirement in OpenApiValidationConstraints.ExtractConditionalRequirements(clrProperty))
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

        foreach (var jsonProperty in typeInfo.Properties)
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

    private static OpenApiSchemaPropertyModel CreateProperty(
        JsonPropertyInfo property,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        string bodyTypeName,
        JsonSerializerContext serializerContext,
        IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> validationAttributesByJsonPropertyName,
        NullabilityInfoContext nullabilityContext,
        FrozenDictionary<string, Type> coveredTypesByName,
        OpenApiSchemaNaming naming)
    {
        var propertyTypeInfo = serializerContext.GetTypeInfo(property.PropertyType);
        var propertyKind = propertyTypeInfo is null
            ? MapKind(property.PropertyType, JsonTypeInfoKind.None)
            : MapKind(property.PropertyType, propertyTypeInfo.Kind);
        var validationAttributes = GetValidationAttributes(property, validationAttributesByJsonPropertyName);
        ThrowIfUnsupportedCrossPropertyValidationAttribute(bodyTypeName, property.Name, validationAttributes);
        var minimum = GetMinimum(validationAttributes);
        var maximum = GetMaximum(validationAttributes);
        var isNullable = IsNullableProperty(property, nullabilityContext);
        var format = MapFormatFromValidationAttributes(validationAttributes) ?? MapFormat(property.PropertyType);
        var (enumValues, enumSchemaName) = MapEnumValues(property.PropertyType, naming);
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
        var (itemsKind, itemsFormat, itemsSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Enumerable, coveredTypesByName, naming);
        var (additionalPropertiesKind, additionalPropertiesFormat, additionalPropertiesSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Dictionary, coveredTypesByName, naming);

        // When dictionary values are arrays (e.g. Dictionary<string, string[]>), resolve the nested
        // array element type so the schema can emit "items" inside "additionalProperties".
        OpenApiSchemaKind? additionalPropertiesItemsKind = null;
        string? additionalPropertiesItemsFormat = null;
        string? additionalPropertiesItemsSchemaName = null;

        if (additionalPropertiesKind == OpenApiSchemaKind.Array)
        {
            var dictValueType = ExtractElementType(property.PropertyType, JsonTypeInfoKind.Dictionary);

            if (dictValueType is not null)
            {
                var dictValueTypeInfo = serializerContext.GetTypeInfo(dictValueType);
                (additionalPropertiesItemsKind, additionalPropertiesItemsFormat, additionalPropertiesItemsSchemaName) =
                    ResolveElementSchema(dictValueTypeInfo, dictValueType, JsonTypeInfoKind.Enumerable, coveredTypesByName, naming);
            }
        }

        return new OpenApiSchemaPropertyModel(
            property.Name,
            propertyKind,
            property.IsRequired,
            property.IsExtensionData,
            isNullable,
            format,
            enumValues,
            enumSchemaName,
            description,
            GetMinLength(validationAttributes),
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
            isWriteOnly);
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
        FrozenDictionary<string, Type> coveredTypesByName,
        OpenApiSchemaNaming naming)
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
            var schemaName = ResolveSchemaRef(elementType, kind, coveredTypesByName, naming);
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
            var schemaName = ResolveSchemaRef(elementType, kind, coveredTypesByName, naming);
            return (kind, format, schemaName);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Returns the schema component name when an element type is complex (not a primitive) AND
    /// appears in the covered body types set (so the <c>$ref</c> target actually exists).
    /// Primitive elements (string, integer, number, boolean) are rendered inline.
    /// </summary>
    private static string? ResolveSchemaRef(Type elementType, OpenApiSchemaKind? kind, FrozenDictionary<string, Type> coveredTypesByName, OpenApiSchemaNaming naming)
    {
        if (kind is not (null or OpenApiSchemaKind.None or OpenApiSchemaKind.Object))
        {
            return null;
        }

        var typeName = OpenApiSchemaNames.Format(elementType, naming);
        return coveredTypesByName.ContainsKey(typeName) ? typeName : null;
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
            // Check the type itself first (e.g. IDictionary<string, Link>), then its implemented interfaces.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                return type.GetGenericArguments()[1];
            }

            var dictInterface = type.GetInterfaces()
                .FirstOrDefault(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
            return dictInterface?.GetGenericArguments()[1];
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
    private static (ImmutableArray<string> Values, string? SchemaName) MapEnumValues(Type propertyType, OpenApiSchemaNaming naming)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (!underlying.IsEnum)
        {
            return (ImmutableArray<string>.Empty, null);
        }

        return (Enum.GetNames(underlying).ToImmutableArray(), OpenApiSchemaNames.Format(underlying, naming));
    }

    /// <summary>
    /// Extracts the <see cref="DescriptionAttribute"/> value from a body property's underlying CLR member,
    /// returning <c>null</c> when no description is specified.
    /// </summary>
    private static string? GetPropertyDescription(JsonPropertyInfo property)
    {
        if (property.AttributeProvider is not MemberInfo member)
        {
            return null;
        }

        return member.GetCustomAttribute<DescriptionAttribute>()?.Description;
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

    private static (decimal? Value, bool IsExclusive) GetMinimum(IEnumerable<ValidationAttribute> validationAttributes)
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
                case GreaterThanAttribute greaterThanAttribute:
                    ApplyLowerBound(ref minimum, ref isExclusive, greaterThanAttribute.MinimumValue, isExclusiveCandidate: true);
                    break;
            }
        }

        return (minimum, isExclusive);
    }

    private static (decimal? Value, bool IsExclusive) GetMaximum(IEnumerable<ValidationAttribute> validationAttributes)
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
                case LessThanAttribute lessThanAttribute:
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

        if (nonNullableType == typeof(string)
            || nonNullableType == typeof(char)
            || nonNullableType == typeof(DateOnly)
            || nonNullableType == typeof(TimeOnly)
            || nonNullableType == typeof(DateTime)
            || nonNullableType == typeof(DateTimeOffset)
            || nonNullableType == typeof(Guid)
            || nonNullableType == typeof(Uri))
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
            if ((nonNullableType.IsGenericType && nonNullableType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                || nonNullableType.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
            {
                return OpenApiSchemaKind.Object;
            }

            if (nonNullableType != typeof(string)
                && ((nonNullableType.IsGenericType && nonNullableType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    || nonNullableType.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))))
            {
                return OpenApiSchemaKind.Array;
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
    private static string FormatFullNameDisplayStyle(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName!;
        var backtickIdx = definition.IndexOf('`');
        var baseName = backtickIdx > 0 ? definition[..backtickIdx] : definition;
        var args = string.Join(", ", type.GetGenericArguments().Select(FormatFullNameDisplayStyle));
        return $"{baseName}<{args}>";
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
    ImmutableArray<OpenApiConditionalRequirement> Conditionals = default);

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
    bool IsWriteOnly = false);

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
