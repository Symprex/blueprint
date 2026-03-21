using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Blueprint.Validation;

/// <summary>
/// Shared runtime helpers for property-level validation.
/// </summary>
public static class ValidationRuntime
{
    private static readonly EmailAddressAttribute _emailAddressAttribute = new();

    /// <summary>
    /// Validates a single property using its declared validation attributes.
    /// </summary>
    /// <param name="propertyAttributes">The property attributes supplied by the descriptor.</param>
    /// <param name="operation">The owning operation instance.</param>
    /// <param name="propertyValue">The current property value.</param>
    /// <param name="memberName">The property name.</param>
    /// <param name="validationResults">The current validation result accumulator.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ValidationContext is constructed for runtime property-validation fallback on generated operation types whose public properties are intentionally preserved.")]
    public static void ValidateProperty(
        IReadOnlyList<Attribute> propertyAttributes,
        object operation,
        object? propertyValue,
        string memberName,
        ref List<ValidationResult>? validationResults)
    {
        var validationContext = new ValidationContext(operation)
        {
            MemberName = memberName,
            DisplayName = GetDisplayName(propertyAttributes, memberName),
        };

        foreach (var requiredAttribute in propertyAttributes.OfType<RequiredAttribute>())
        {
            var requiredResult = requiredAttribute.GetValidationResult(propertyValue, validationContext);
            if (requiredResult != ValidationResult.Success)
            {
                AddValidationResult(requiredResult!, ref validationResults);
                return;
            }
        }

        foreach (var validationAttribute in propertyAttributes.OfType<ValidationAttribute>())
        {
            if (validationAttribute is RequiredAttribute)
            {
                continue;
            }

            var validationResult = validationAttribute.GetValidationResult(propertyValue, validationContext);
            if (validationResult != ValidationResult.Success)
            {
                AddValidationResult(validationResult!, ref validationResults);
            }
        }
    }

    /// <summary>
    /// Checks whether the supplied value matches <see cref="EmailAddressAttribute"/> semantics.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns><c>true</c> when the value is a valid email address; otherwise <c>false</c>.</returns>
    public static bool IsValidEmailAddress(string? value)
    {
        return _emailAddressAttribute.IsValid(value);
    }

    private static void AddValidationResult(ValidationResult validationResult, ref List<ValidationResult>? validationResults)
    {
        validationResults ??= [];
        validationResults.Add(validationResult);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DisplayAttribute.GetName preserves property display-name formatting for runtime validation fallback paths.")]
    private static string GetDisplayName(IReadOnlyList<Attribute> propertyAttributes, string memberName)
    {
        var displayName = propertyAttributes.OfType<DisplayAttribute>()
            .Select(static attribute => attribute.GetName())
            .FirstOrDefault(static name => string.IsNullOrWhiteSpace(name) == false);

        if (displayName is not null)
        {
            return displayName;
        }

        return propertyAttributes.OfType<DisplayNameAttribute>()
            .Select(static attribute => attribute.DisplayName)
            .FirstOrDefault(static name => string.IsNullOrWhiteSpace(name) == false)
            ?? memberName;
    }
}
