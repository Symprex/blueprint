using System.Collections.Immutable;
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
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);

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

        var schemas = coveredBodyTypeNames
            .Where(coveredTypesByName.ContainsKey)
            .Select(bodyTypeName => CreateSchema(bodyTypeName, coveredTypesByName[bodyTypeName], serializerContext))
            .ToImmutableArray();

        return new OpenApiSchemaProjectionContext(document, schemas);
    }

    private static OpenApiSchemaModel CreateSchema(
        string bodyTypeName,
        Type bodyType,
        JsonSerializerContext serializerContext)
    {
        var typeInfo = serializerContext.GetTypeInfo(bodyType)
            ?? throw new InvalidOperationException($"The serializer context did not expose metadata for covered body type '{bodyTypeName}'.");
        var validationAttributesByJsonPropertyName = CreateValidationAttributesByJsonPropertyName(bodyType, serializerContext);
        var properties = typeInfo.Kind == JsonTypeInfoKind.Object
            ? typeInfo.Properties
                .Select(property => CreateProperty(property, bodyTypeName, serializerContext, validationAttributesByJsonPropertyName))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<OpenApiSchemaPropertyModel>.Empty;
        var propertyNames = properties
            .Select(static property => property.Name)
            .ToImmutableArray();

        return new OpenApiSchemaModel(
            bodyTypeName,
            MapKind(bodyType, typeInfo.Kind),
            true,
            propertyNames,
            properties);
    }

    private static OpenApiSchemaPropertyModel CreateProperty(
        JsonPropertyInfo property,
        string bodyTypeName,
        JsonSerializerContext serializerContext,
        IReadOnlyDictionary<string, IReadOnlyList<ValidationAttribute>> validationAttributesByJsonPropertyName)
    {
        var propertyTypeInfo = serializerContext.GetTypeInfo(property.PropertyType);
        var propertyKind = propertyTypeInfo is null
            ? MapKind(property.PropertyType, JsonTypeInfoKind.None)
            : MapKind(property.PropertyType, propertyTypeInfo.Kind);
        var validationAttributes = GetValidationAttributes(property, validationAttributesByJsonPropertyName);
        ThrowIfUnsupportedCrossPropertyValidationAttribute(bodyTypeName, property.Name, validationAttributes);
        var minimum = GetMinimum(validationAttributes);
        var maximum = GetMaximum(validationAttributes);

        return new OpenApiSchemaPropertyModel(
            property.Name,
            propertyKind,
            property.IsRequired,
            property.IsExtensionData,
            GetMinLength(validationAttributes),
            GetMaxLength(validationAttributes),
            GetPattern(validationAttributes),
            minimum.Value,
            maximum.Value,
            minimum.IsExclusive,
            maximum.IsExclusive);
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
        return validationAttributes
            .OfType<RegularExpressionAttribute>()
            .Select(static attribute => attribute.Pattern)
            .FirstOrDefault(static pattern => string.IsNullOrWhiteSpace(pattern) == false);
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
            .ToDictionary(
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
public sealed record OpenApiSchemaModel(
    string BodyTypeName,
    OpenApiSchemaKind Kind,
    bool IsCoveredBySerializerContext,
    ImmutableArray<string> PropertyNames,
    ImmutableArray<OpenApiSchemaPropertyModel> Properties);

/// <summary>
/// Captures one JSON-visible property projected from serializer metadata for a covered schema.
/// </summary>
/// <param name="Name">The JSON property name.</param>
/// <param name="Kind">The broad schema shape for the property payload.</param>
/// <param name="IsRequired">Whether serializer metadata marks the property as required.</param>
/// <param name="IsExtensionData">Whether the property acts as JSON extension data.</param>
/// <param name="MinLength">The inclusive minimum string/collection length when validation attributes expose one.</param>
/// <param name="MaxLength">The inclusive maximum string/collection length when validation attributes expose one.</param>
/// <param name="Pattern">The regular-expression pattern when validation attributes expose one.</param>
/// <param name="Minimum">The numeric lower bound when validation attributes expose one.</param>
/// <param name="Maximum">The numeric upper bound when validation attributes expose one.</param>
/// <param name="IsMinimumExclusive">Whether the numeric lower bound is exclusive.</param>
/// <param name="IsMaximumExclusive">Whether the numeric upper bound is exclusive.</param>
public sealed record OpenApiSchemaPropertyModel(
    string Name,
    OpenApiSchemaKind Kind,
    bool IsRequired,
    bool IsExtensionData,
    int? MinLength = null,
    int? MaxLength = null,
    string? Pattern = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    bool IsMinimumExclusive = false,
    bool IsMaximumExclusive = false);

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
