using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Blueprint.Validation;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Captures projected validation constraints for a single property so that both parameter-level
/// and inline-request-body-level OpenAPI rendering share the same extraction logic.
/// </summary>
internal sealed record OpenApiValidationConstraints(
    bool IsRequired,
    int? MinLength,
    int? MaxLength,
    string? Pattern,
    decimal? Minimum,
    decimal? Maximum,
    bool ExclusiveMinimum,
    bool ExclusiveMaximum,
    string? Format)
{
    /// <summary>
    /// Empty constraints indicating no validation metadata was found.
    /// </summary>
    public static OpenApiValidationConstraints None { get; } = new(false, null, null, null, null, null, false, false, null);

    /// <summary>
    /// Returns <see langword="true"/> when at least one constraint value is non-default
    /// (excluding <see cref="IsRequired"/>, which is tracked separately by the parameter model).
    /// </summary>
    public bool HasSchemaConstraints =>
        this.MinLength.HasValue
        || this.MaxLength.HasValue
        || this.Pattern is not null
        || this.Minimum.HasValue
        || this.Maximum.HasValue
        || this.Format is not null;

    /// <summary>
    /// Extracts validation constraints from the CLR property attributes on an operation type's property.
    /// Conditional validators (<c>RequiredIfAttribute</c>, <c>RequiredIfNotAttribute</c>, <c>NotAllowedIfAttribute</c>)
    /// are intentionally skipped because OpenAPI 3.x cannot represent cross-property conditionality; they remain
    /// enforced at runtime through the generated validation pipeline.
    /// </summary>
    /// <param name="operationType">The operation CLR type to reflect on.</param>
    /// <param name="propertyName">The CLR property name to extract constraints from.</param>
    /// <returns>The extracted validation constraints, or <see cref="None"/> when no attributes are found.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "OpenAPI constraint projection reflects over operation type properties that are preserved by the source generator's generated pipeline code.")]
    public static OpenApiValidationConstraints Extract(Type operationType, string propertyName)
    {
        var property = operationType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return ExtractFromProperty(property);
    }

    /// <summary>
    /// Extracts validation constraints from a pre-resolved CLR property, avoiding redundant
    /// <c>GetProperty</c> calls when the caller already has the <see cref="PropertyInfo"/>.
    /// </summary>
    /// <param name="property">The CLR property to extract constraints from, or <c>null</c> when not found.</param>
    /// <returns>The extracted validation constraints, or <see cref="None"/> when no attributes are found.</returns>
    public static OpenApiValidationConstraints ExtractFromProperty(PropertyInfo? property)
    {
        if (property is null)
        {
            return None;
        }

        var attributes = property.GetCustomAttributes(inherit: true).OfType<ValidationAttribute>().ToArray();
        if (attributes.Length == 0)
        {
            return None;
        }

        var isRequired = attributes.Any(static a => a is RequiredAttribute);
        var minLength = GetMinLength(attributes);
        var maxLength = GetMaxLength(attributes);
        var pattern = GetPattern(attributes);
        var minimum = GetMinimum(attributes);
        var maximum = GetMaximum(attributes);
        var format = GetFormat(attributes);

        return new OpenApiValidationConstraints(
            isRequired,
            minLength,
            maxLength,
            pattern,
            minimum.Value,
            maximum.Value,
            minimum.IsExclusive,
            maximum.IsExclusive,
            format);
    }

    private static int? GetMinLength(IReadOnlyList<ValidationAttribute> validationAttributes)
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

    private static int? GetMaxLength(IReadOnlyList<ValidationAttribute> validationAttributes)
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

    private static string? GetPattern(IReadOnlyList<ValidationAttribute> validationAttributes)
    {
        return validationAttributes
            .OfType<RegularExpressionAttribute>()
            .Select(static attribute => attribute.Pattern)
            .FirstOrDefault(static pattern => string.IsNullOrWhiteSpace(pattern) == false);
    }

    private static (decimal? Value, bool IsExclusive) GetMinimum(IReadOnlyList<ValidationAttribute> validationAttributes)
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

    private static (decimal? Value, bool IsExclusive) GetMaximum(IReadOnlyList<ValidationAttribute> validationAttributes)
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

    /// <summary>
    /// Infers an OpenAPI <c>format</c> hint from well-known validation attributes
    /// (<c>EmailAddressAttribute</c> → <c>"email"</c>, <c>PhoneAttribute</c> → <c>"phone"</c>,
    /// <c>UrlAttribute</c> → <c>"uri"</c>).
    /// </summary>
    private static string? GetFormat(IReadOnlyList<ValidationAttribute> validationAttributes)
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
            }
        }

        return null;
    }
}
