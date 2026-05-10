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
public sealed record OpenApiSchemaProjectionContext(
    OpenApiDocumentBuilderContext Document,
    ImmutableArray<OpenApiSchemaModel> Schemas)
{
    /// <summary>
    /// Creates a schema projection snapshot for the covered body types in the supplied OpenAPI document-builder context.
    /// Missing/uncovered body types are intentionally excluded so this seam only projects serializer-backed shapes.
    /// </summary>
    /// <param name="document">The previously prepared OpenAPI document-builder context.</param>
    /// <returns>The schema projection snapshot.</returns>
    public static OpenApiSchemaProjectionContext Create(OpenApiDocumentBuilderContext document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.App.AppOwnedSerializerContext is not JsonSerializerContext serializerContext)
        {
            return new OpenApiSchemaProjectionContext(document, ImmutableArray<OpenApiSchemaModel>.Empty);
        }

        var coveredTypesByName = document.App.AppOwnedSerializerContextBodyTypes
            .Select(static bodyType => new KeyValuePair<string, Type>(FormatTypeName(bodyType), bodyType))
            .ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

        var rawBodyTypeNames = document.Operations
            .Where(static operation => operation.IsSerializerContextCoverageSatisfied)
            .SelectMany(static operation => operation.RequiredSerializerContextBodyTypeNames)
            .Distinct(StringComparer.Ordinal)
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
                schemaBuilder.Add(CreateSchema(bodyTypeName, bodyType, serializerContext, coveredTypesByName));
            }
        }

        return new OpenApiSchemaProjectionContext(document, schemaBuilder.ToImmutable());
    }

    private static OpenApiSchemaModel CreateSchema(
        string bodyTypeName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        JsonSerializerContext serializerContext,
        FrozenDictionary<string, Type> coveredTypesByName)
    {
        var typeInfo = serializerContext.GetTypeInfo(bodyType)
            ?? throw new InvalidOperationException($"The serializer context did not expose metadata for covered body type '{bodyTypeName}'.");
        var validationAttributesByJsonPropertyName = CreateValidationAttributesByJsonPropertyName(bodyType, serializerContext);

        // Create once per schema rather than per-property to avoid repeated allocations.
        var nullabilityContext = new NullabilityInfoContext();

        var properties = typeInfo.Kind == JsonTypeInfoKind.Object
            ? typeInfo.Properties
                .Select(property => CreateProperty(property, bodyType, bodyTypeName, serializerContext, validationAttributesByJsonPropertyName, nullabilityContext, coveredTypesByName))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OpenApiSchemaPropertyModel>.Empty;
        var propertyNames = properties
            .Select(static property => property.Name)
            .ToImmutableArray();

        var schemaDescription = bodyType.GetCustomAttribute<DescriptionAttribute>()?.Description;
        var schemaTitle = bodyType.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName;

        return new OpenApiSchemaModel(
            bodyTypeName,
            MapKind(bodyType, typeInfo.Kind),
            true,
            propertyNames,
            properties,
            schemaDescription,
            schemaTitle);
    }

    private static OpenApiSchemaPropertyModel CreateProperty(
        JsonPropertyInfo property,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type bodyType,
        string bodyTypeName,
        JsonSerializerContext serializerContext,
        IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> validationAttributesByJsonPropertyName,
        NullabilityInfoContext nullabilityContext,
        FrozenDictionary<string, Type> coveredTypesByName)
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
        var enumValues = MapEnumValues(property.PropertyType);
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
        var (itemsKind, itemsFormat, itemsSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Enumerable, coveredTypesByName);
        var (additionalPropertiesKind, additionalPropertiesFormat, additionalPropertiesSchemaName) = ResolveElementSchema(propertyTypeInfo, property.PropertyType, JsonTypeInfoKind.Dictionary, coveredTypesByName);

        return new OpenApiSchemaPropertyModel(
            property.Name,
            propertyKind,
            property.IsRequired,
            property.IsExtensionData,
            isNullable,
            format,
            enumValues,
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
            isDeprecated,
            deprecationReason,
            defaultValue,
            exampleValue,
            isReadOnly,
            isWriteOnly);
    }

    /// <summary>
    /// Resolves the schema kind, format, and optional element type name for element types of
    /// collections and dictionaries. The element type name is the <see cref="FormatTypeName"/>
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
        FrozenDictionary<string, Type> coveredTypesByName)
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
            var schemaName = ResolveSchemaRef(elementType, kind, coveredTypesByName);
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
            var schemaName = ResolveSchemaRef(elementType, kind, coveredTypesByName);
            return (kind, format, schemaName);
        }

        return (null, null, null);
    }

    /// <summary>
    /// Returns the schema component name when an element type is complex (not a primitive) AND
    /// appears in the covered body types set (so the <c>$ref</c> target actually exists).
    /// Primitive elements (string, integer, number, boolean) are rendered inline.
    /// </summary>
    private static string? ResolveSchemaRef(Type elementType, OpenApiSchemaKind? kind, FrozenDictionary<string, Type> coveredTypesByName)
    {
        if (kind is not (null or OpenApiSchemaKind.None or OpenApiSchemaKind.Object))
        {
            return null;
        }

        var typeName = FormatTypeName(elementType);
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
    private static ImmutableArray<string> MapEnumValues(Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (!underlying.IsEnum)
        {
            return ImmutableArray<string>.Empty;
        }

        return Enum.GetNames(underlying).ToImmutableArray();
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
        if (property.AttributeProvider?.GetCustomAttributes(inherit: true).OfType<ValidationAttribute>().ToArray() is { Length: > 0 } attributes)
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
                static property => (IReadOnlyList<ValidationAttribute>)property.GetCustomAttributes(inherit: true).OfType<ValidationAttribute>().ToArray(),
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

    private static string FormatTypeName(Type type)
    {
        return type == typeof(string)
            ? "string"
            : type.FullName ?? type.Name;
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
public sealed record OpenApiSchemaModel(
    string BodyTypeName,
    OpenApiSchemaKind Kind,
    bool IsCoveredBySerializerContext,
    ImmutableArray<string> PropertyNames,
    ImmutableArray<OpenApiSchemaPropertyModel> Properties,
    string? Description = null,
    string? Title = null);

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
