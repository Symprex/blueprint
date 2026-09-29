using System.Collections.Immutable;
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
    /// Gets a value indicating whether the property rejects an empty string for its <see cref="RequiredAttribute"/>, with
    /// no <see cref="StringLengthAttribute"/> whose minimum legacy documented instead. See
    /// <see cref="GetDocumentedMinLength"/>, which is its only reader.
    /// </summary>
    public bool RequiresNonEmptyString { get; init; }

    /// <summary>
    /// Gets a value indicating whether the property carries a <see cref="StringLengthAttribute"/>, whatever its
    /// <see cref="StringLengthAttribute.MinimumLength"/> — including the default of 0, which <see cref="GetMinLength"/>
    /// otherwise treats as "no minimum declared" since it constrains nothing. See <see cref="GetDocumentedMinLength"/>,
    /// which is its only reader, for why a value of 0 still matters under legacy naming.
    /// </summary>
    public bool HasStringLengthAttribute { get; init; }

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
    /// This covers only the constraints that belong to the property's own schema. The conditional validators
    /// (<c>RequiredIfAttribute</c>, <c>RequiredIfNotAttribute</c>, <c>RequiredItemsIfAttribute</c>,
    /// <c>RequiredItemsIfNotAttribute</c>) are cross-property and belong to the <em>containing</em> schema, so they are
    /// extracted separately by <see cref="ExtractConditionalRequirements"/> and rendered as <c>allOf</c>/<c>if</c>/<c>then</c>.
    /// <c>NotAllowedIfAttribute</c> remains unprojected: JSON Schema can express it, but nothing downstream consumes it yet
    /// and an unproven projection of a prohibition is the direction that risks blocking valid payloads client-side.
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
    /// <param name="includeComparisonBounds">
    /// Whether the bounds Blueprint's own comparison attributes declare count (see <see cref="GetMinimum"/>). <see langword="false"/>
    /// under <see cref="OpenApiSchemaNaming.Legacy"/>, whose NJsonSchema did not know them.
    /// </param>
    /// <returns>The extracted validation constraints, or <see cref="None"/> when no attributes are found.</returns>
    public static OpenApiValidationConstraints ExtractFromProperty(PropertyInfo? property, bool includeComparisonBounds = true)
    {
        if (property is null)
        {
            return None;
        }

        var allAttributes = property.GetCustomAttributes(inherit: true);
        var attributes = FilterValidationAttributes(allAttributes);
        if (attributes.Count == 0)
        {
            return None;
        }

        var isRequired = attributes.Any(static a => a is RequiredAttribute);
        var minLength = GetMinLength(attributes);
        var maxLength = GetMaxLength(attributes);
        var pattern = GetPattern(attributes);
        var minimum = GetMinimum(attributes, includeComparisonBounds);
        var maximum = GetMaximum(attributes, includeComparisonBounds);
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
            format)
        {
            RequiresNonEmptyString = IsRequiredNonEmptyString(attributes),
            HasStringLengthAttribute = attributes.Any(static a => a is StringLengthAttribute),
        };
    }

    /// <summary>
    /// Returns whether a property's validation attributes make it reject an empty string for its
    /// <see cref="RequiredAttribute"/> (one that does not set <see cref="RequiredAttribute.AllowEmptyStrings"/>), with no
    /// <see cref="StringLengthAttribute"/> on it.
    /// </summary>
    /// <remarks>
    /// The <see cref="StringLengthAttribute"/> exception is legacy's: its NJsonSchema replaced the <c>[Required]</c> string's
    /// minimum length with the attribute's own minimum, so a string bounded only above had a minimum of 0, which constrains
    /// nothing and which this document leaves out, as it does for such a string that is not <c>[Required]</c>.
    /// </remarks>
    /// <param name="validationAttributes">The property's validation attributes.</param>
    /// <returns><see langword="true"/> when a non-enum string property is documented with a minimum length of 1.</returns>
    internal static bool IsRequiredNonEmptyString(IEnumerable<ValidationAttribute> validationAttributes)
    {
        var isRequired = false;

        foreach (var attribute in validationAttributes)
        {
            switch (attribute)
            {
                case StringLengthAttribute:
                    return false;
                case RequiredAttribute { AllowEmptyStrings: false }:
                    isRequired = true;
                    break;
            }
        }

        return isRequired;
    }

    /// <summary>
    /// Gets the <c>minLength</c> a request body's or schema component's property is documented with: its declared minimum
    /// length, or else 1 for a string, not an enum, that <see cref="IsRequiredNonEmptyString"/>, as legacy Blueprint's
    /// NJsonSchema documented it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NJsonSchema (11.1, which legacy used) gives a <see cref="RequiredAttribute"/> property <c>minLength: 1</c> when its
    /// JSON type is <c>string</c> and it is not an enum, unless <see cref="RequiredAttribute.AllowEmptyStrings"/> is set;
    /// a <see cref="StringLengthAttribute"/> or <see cref="MinLengthAttribute"/> replaces it with its own minimum. That
    /// includes a GUID, a date and a URI, which are strings in the document too. A <c>[Required]</c> string rejects an empty
    /// value whichever rule marks properties required, so the minimum cannot reject what the server accepts.
    /// </para>
    /// <para>
    /// Not for a parameter: legacy described parameters without NJsonSchema's property rules, and no legacy parameter,
    /// <c>[Required]</c> query strings among them, carries a <c>minLength</c> it was not declared with.
    /// </para>
    /// </remarks>
    /// <param name="declaredMinLength">The minimum length the property's validation attributes declare, if any.</param>
    /// <param name="requiresNonEmptyString">Whether the property <see cref="IsRequiredNonEmptyString"/>.</param>
    /// <param name="isNonEnumString">Whether the property is documented as a string that is not an enum.</param>
    /// <param name="hasStringLengthAttribute">
    /// Whether the property carries a <see cref="StringLengthAttribute"/> at all (see <see cref="HasStringLengthAttribute"/>).
    /// </param>
    /// <param name="useLegacyStringLengthZero">
    /// <see langword="true"/> under <see cref="OpenApiSchemaNaming.Legacy"/>: legacy's NJsonSchema echoed a
    /// <see cref="StringLengthAttribute"/>'s <see cref="StringLengthAttribute.MinimumLength"/> verbatim, including its
    /// default of 0 (Signature365's committed Portal document, e.g. a description bounded only by
    /// <c>[StringLength(200)]</c>, carries <c>"minLength": 0</c>) — a fact this document otherwise leaves out under
    /// Default naming, since a minimum of 0 constrains nothing.
    /// </param>
    /// <returns>The minimum length to document, or <see langword="null"/> for none.</returns>
    internal static int? GetDocumentedMinLength(
        int? declaredMinLength,
        bool requiresNonEmptyString,
        bool isNonEnumString,
        bool hasStringLengthAttribute = false,
        bool useLegacyStringLengthZero = false)
    {
        if (declaredMinLength is not null)
        {
            return declaredMinLength;
        }

        if (useLegacyStringLengthZero && hasStringLengthAttribute)
        {
            return 0;
        }

        return requiresNonEmptyString && isNonEnumString ? 1 : null;
    }

    /// <summary>
    /// Extracts the cross-property conditional requirements declared on a property by the conditional validators
    /// (<c>RequiredIf</c>, <c>RequiredIfNot</c>, <c>RequiredItemsIf</c>, <c>RequiredItemsIfNot</c>), for projection onto
    /// the containing object schema as <c>allOf</c>/<c>if</c>/<c>then</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Property names on the returned requirements are CLR names. The caller resolves them to the JSON-visible names
    /// used by the containing schema and drops any requirement whose target or dependent is not a member of it — this
    /// type has no knowledge of binding.
    /// </para>
    /// <para>
    /// Two skips are deliberate, and both keep the projection from claiming more than the runtime enforces. A target
    /// that is a non-nullable value type is skipped because its requirement can never fail — <c>HasRequiredValue</c>
    /// returns true for any non-null, non-string value, and the generated fast path likewise produces no rule — so
    /// emitting one would reject payloads the server accepts. An item requirement on a property that is not a
    /// recognisable single-element collection is skipped for the same reason: without an element type there is no
    /// constraint that is certain not to over-reach.
    /// </para>
    /// <para>
    /// A <c>null</c> dependent value is <em>not</em> skipped. Legacy Blueprint's projection skips it as a server-side
    /// no-op, which is untrue of these attributes: <c>DependentValues</c> is compared through <c>value?.ToString()</c>,
    /// so a null dependent value matches exactly when the dependent property is null and the requirement genuinely
    /// activates. Skipping it would drop a constraint the runtime enforces.
    /// </para>
    /// </remarks>
    /// <param name="property">The CLR property to extract conditional requirements from, or <c>null</c> when not found.</param>
    /// <returns>The conditional requirements declared on the property, in dependent-property name order; empty when none apply.</returns>
    public static ImmutableArray<OpenApiConditionalRequirement> ExtractConditionalRequirements(PropertyInfo? property)
    {
        if (property is null)
        {
            return [];
        }

        var scalarTargetKind = GetScalarTargetKind(property.PropertyType);
        var itemsTargetKind = GetItemsTargetKind(property.PropertyType);

        var builder = ImmutableArray.CreateBuilder<OpenApiConditionalRequirement>();

        foreach (var attribute in property.GetCustomAttributes(inherit: true))
        {
            switch (attribute)
            {
                case RequiredIfAttribute requiredIf:
                    Add(requiredIf.DependentProperty, requiredIf.DependentValues, negated: false, scalarTargetKind);
                    break;
                case RequiredIfNotAttribute requiredIfNot:
                    Add(requiredIfNot.DependentProperty, requiredIfNot.DependentValues, negated: true, scalarTargetKind);
                    break;
                case RequiredItemsIfAttribute requiredItemsIf:
                    Add(requiredItemsIf.DependentProperty, requiredItemsIf.DependentValues, negated: false, itemsTargetKind);
                    break;
                case RequiredItemsIfNotAttribute requiredItemsIfNot:
                    Add(requiredItemsIfNot.DependentProperty, requiredItemsIfNot.DependentValues, negated: true, itemsTargetKind);
                    break;
            }
        }

        // Reflection does not guarantee an order for repeated attributes on one member, and the rendered document is a
        // contract with an ETag over it, so impose one rather than let the JSON shuffle between builds. Dependent name
        // alone is not a total order: a scalar RequiredIf and an items RequiredItemsIf on the same property share both
        // target (this property) and dependent, and differ only in TargetKind, so that is the tiebreak that actually
        // separates them. DependentValueNegated and the dependent values themselves are the final fallback for the
        // (currently unreachable) case where even the target kind ties.
        return
        [
            .. builder
                .OrderBy(static requirement => requirement.DependentPropertyName, StringComparer.Ordinal)
                .ThenBy(static requirement => requirement.TargetKind)
                .ThenBy(static requirement => requirement.DependentValueNegated)
                .ThenBy(static requirement => FormatDependentValuesKey(requirement.DependentValues), StringComparer.Ordinal),
        ];

        void Add(string dependentProperty, IEnumerable<object?> dependentValues, bool negated, OpenApiConditionalTargetKind? targetKind)
        {
            if (targetKind is not { } kind || !TryNormaliseDependentValues(dependentValues, out var normalisedValues))
            {
                return;
            }

            builder.Add(new OpenApiConditionalRequirement(
                dependentProperty,
                normalisedValues,
                negated,
                property.Name,
                kind));
        }
    }

    /// <summary>
    /// Normalises the dependent values for JSON rendering, failing when any of them has no faithful JSON representation.
    /// Enum members and <see cref="char"/> values are projected to strings because both render as <c>type: string</c> in
    /// the document; booleans, the integral and floating-point types, strings and <see langword="null"/> pass through to
    /// their natural JSON form.
    /// </summary>
    /// <remarks>
    /// Guessing at a representation would be tolerable while an unmatched condition could only fail to fire — that
    /// under-enforces, which is safe. It is not tolerable under the negated (<c>RequiredIfNot</c>/<c>RequiredItemsIfNot</c>)
    /// form, where the test is wrapped in <c>not</c>: a condition that can never match becomes one that always matches, so
    /// the requirement always applies and the generated client rejects payloads the server accepts. An unrepresentable
    /// value therefore drops the whole requirement rather than being approximated.
    /// </remarks>
    /// <param name="dependentValues">The raw dependent values from the attribute.</param>
    /// <param name="normalisedValues">The normalised values when every one of them can be represented.</param>
    /// <returns><see langword="true"/> when every dependent value has a faithful JSON representation.</returns>
    private static bool TryNormaliseDependentValues(IEnumerable<object?> dependentValues, out ImmutableArray<object?> normalisedValues)
    {
        var builder = ImmutableArray.CreateBuilder<object?>();

        foreach (var value in dependentValues)
        {
            switch (value)
            {
                case null:
                case bool:
                case string:
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                case float or double or decimal:
                    builder.Add(value);
                    break;

                // Enums serialise as their member name, and a char as a one-character string; both render as
                // "type": "string" in the document, so the string form is the one that can match.
                case Enum:
                case char:
                    builder.Add(value.ToString());
                    break;

                default:
                    normalisedValues = [];
                    return false;
            }
        }

        normalisedValues = builder.ToImmutable();
        return true;
    }

    /// <summary>
    /// Renders normalised dependent values as a single ordinal-comparable key, for use as the final ordering tiebreak
    /// when two requirements share every other field. A unit-separator control character keeps <c>["a", "b"]</c> from
    /// comparing equal to <c>["ab"]</c>. The separator is written as the escape <c>'\u001F'</c>; no raw control byte appears in this file, so the literal survives formatting, patching and copy-paste intact.
    /// </summary>
    /// <param name="dependentValues">The normalised dependent values to render.</param>
    /// <returns>The ordinal-comparable key for the values.</returns>
    internal static string FormatDependentValuesKey(ImmutableArray<object?> dependentValues)
    {
        return string.Join('\u001F', dependentValues.Select(static value => value?.ToString() ?? string.Empty));
    }

    /// <summary>
    /// Maps a scalar target's CLR type onto the constraint the runtime enforces for it, or <c>null</c> when the
    /// requirement cannot fail at runtime and therefore must not be projected.
    /// </summary>
    private static OpenApiConditionalTargetKind? GetScalarTargetKind(Type targetType)
    {
        if (targetType == typeof(string))
        {
            return OpenApiConditionalTargetKind.NonBlankString;
        }

        // Mirrors ConditionalRequiredValidationFastPathStrategyHelpers.TryGetRequiredStateExpressions: a non-nullable
        // value type has no "missing" state, so neither the fast path nor the runtime attribute can ever reject it.
        return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
            ? null
            : OpenApiConditionalTargetKind.NonNull;
    }

    /// <summary>
    /// Maps a collection target's CLR type onto the item constraint the runtime enforces for it, or <c>null</c> when the
    /// element type cannot be determined.
    /// </summary>
    private static OpenApiConditionalTargetKind? GetItemsTargetKind(Type targetType)
    {
        // RequiredItemsIfAttribute.HasItems rejects an element whose ToString() is blank, whatever the element type, so
        // a List<int> of [1, 2] satisfies it. Narrowing items to "type": "string" regardless of element type — as legacy
        // Blueprint does — would reject that payload client-side while the server accepts it.
        return GetCollectionElementType(targetType) switch
        {
            null => null,
            { } elementType when elementType == typeof(string) => OpenApiConditionalTargetKind.NonBlankStringItems,
            _ => OpenApiConditionalTargetKind.NonNullItems,
        };
    }

    /// <summary>
    /// Resolves the element type of a single-element collection (an array or a one-argument generic such as
    /// <c>List&lt;T&gt;</c>), or <c>null</c> when the type is not one.
    /// </summary>
    private static Type? GetCollectionElementType(Type propertyType)
    {
        if (propertyType == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
        {
            return null;
        }

        if (propertyType.IsArray)
        {
            return propertyType.GetElementType();
        }

        return propertyType.IsGenericType && propertyType.GetGenericArguments() is { Length: 1 } typeArguments
            ? typeArguments[0]
            : null;
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
                case NotEmptyListAttribute:
                    minLength = minLength.HasValue
                        ? Math.Max(minLength.Value, 1)
                        : 1;
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
                MaxItemsListAttribute maxItemsListAttribute => maxItemsListAttribute.MaximumItems,
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

    /// <summary>
    /// Gets a property's lower bound from its <see cref="RangeAttribute"/> and, when <paramref name="includeComparisonBounds"/>
    /// is set, Blueprint's <see cref="GreaterThanAttribute"/> and <see cref="GreaterThanOrEqualToAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Legacy documented none of the comparison attributes (R147): they are plain <c>ValidationAttribute</c>s, which its
    /// NJsonSchema did not read, and no property of Signature365's twelve <c>[GreaterThan]</c> ones carries a bound in any
    /// committed document. <see cref="GetMaximum"/> is the same for the upper bound.
    /// </remarks>
    private static (decimal? Value, bool IsExclusive) GetMinimum(IReadOnlyList<ValidationAttribute> validationAttributes, bool includeComparisonBounds)
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
                case GreaterThanOrEqualToAttribute greaterThanOrEqualToAttribute when includeComparisonBounds:
                    ApplyLowerBound(ref minimum, ref isExclusive, greaterThanOrEqualToAttribute.MinimumValue, isExclusiveCandidate: false);
                    break;
            }
        }

        return (minimum, isExclusive);
    }

    private static (decimal? Value, bool IsExclusive) GetMaximum(IReadOnlyList<ValidationAttribute> validationAttributes, bool includeComparisonBounds)
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
                case LessThanOrEqualToAttribute lessThanOrEqualToAttribute when includeComparisonBounds:
                    ApplyUpperBound(ref maximum, ref isExclusive, lessThanOrEqualToAttribute.MaximumValue, isExclusiveCandidate: false);
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
    /// <c>UrlAttribute</c> → <c>"uri"</c>, <c>CreditCardAttribute</c> → <c>"credit-card"</c>).
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
                case CreditCardAttribute:
                    return "credit-card";
            }
        }

        return null;
    }

    /// <summary>
    /// Filters an <see cref="Attribute.GetCustomAttributes(MemberInfo, bool)"/> result array to only
    /// <see cref="ValidationAttribute"/> instances without the intermediate LINQ <c>OfType</c> + <c>ToArray</c>
    /// allocation on the hot path.
    /// </summary>
    /// <param name="allAttributes">The raw attribute array from reflection.</param>
    /// <returns>A read-only list containing only the <see cref="ValidationAttribute"/> instances, or an empty list.</returns>
    internal static IReadOnlyList<ValidationAttribute> FilterValidationAttributes(object[] allAttributes)
    {
        List<ValidationAttribute>? result = null;

        for (var i = 0; i < allAttributes.Length; i++)
        {
            if (allAttributes[i] is ValidationAttribute va)
            {
                result ??= new List<ValidationAttribute>(allAttributes.Length - i);
                result.Add(va);
            }
        }

        return result ?? (IReadOnlyList<ValidationAttribute>)[];
    }
}
