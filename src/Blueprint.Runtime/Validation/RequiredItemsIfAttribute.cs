using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Requires a collection property to contain at least one item, but only when another property matches one of the
/// configured dependent values. This is the collection-aware counterpart to <see cref="RequiredIfAttribute"/>:
/// where <see cref="RequiredIfAttribute"/> treats a non-null collection as satisfied (so an empty list passes),
/// this attribute inspects the collection and rejects a <see langword="null"/> or empty collection when the
/// requirement is active. Every element must also be non-<see langword="null"/> and non-whitespace: a collection
/// containing only blank strings is treated the same as an empty collection.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class RequiredItemsIfAttribute : ValidationAttribute
{
    private const string RequiredFieldMessage = "The {0} field must contain at least one item if {1} field is {2}.";
    private readonly HashSet<string?> _dependentValueTexts;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredItemsIfAttribute"/> class.
    /// </summary>
    /// <param name="dependentProperty">The sibling property whose value controls whether this collection becomes required.</param>
    /// <param name="dependentValue">The dependent value, or values, that activate the requirement.</param>
    public RequiredItemsIfAttribute(string dependentProperty, object? dependentValue)
        : base(RequiredFieldMessage)
    {
        if (string.IsNullOrWhiteSpace(dependentProperty))
        {
            throw new ArgumentException("The dependent property name must be provided.", nameof(dependentProperty));
        }

        this.DependentProperty = dependentProperty;
        this.DependentValue = dependentValue;
        this.DependentValues = dependentValue is Array array ? array.Cast<object?>().ToArray() : [dependentValue];
        this._dependentValueTexts = [.. this.DependentValues.Select(static value => value?.ToString())];
    }

    /// <summary>
    /// Gets the sibling property whose value controls whether this collection becomes required.
    /// </summary>
    public string DependentProperty { get; }

    /// <summary>
    /// Gets the original dependent value supplied to the attribute constructor.
    /// </summary>
    public object? DependentValue { get; }

    /// <summary>
    /// Gets the dependent values normalised into an enumerable so arrays and scalar inputs follow the same validation path.
    /// </summary>
    public IEnumerable<object?> DependentValues { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        var dependentValueDisplay = string.Join(" or ", this._dependentValueTexts.Select(static v => v ?? "null"));
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.DependentProperty, dependentValueDisplay);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Blueprint validation attributes inspect preserved public operation properties during runtime fallback validation.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Cross-property validation uses compiled expression delegates for property access. Validation attributes are inherently reflection-based.")]
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        var dependentGetter = CrossPropertyAccessorCache.GetGetter(validationContext.ObjectType, this.DependentProperty);
        if (dependentGetter is null)
        {
            return new ValidationResult(
                $"The property '{this.DependentProperty}' referenced by RequiredItemsIfAttribute on '{validationContext.MemberName}' does not exist on '{validationContext.ObjectType.Name}'.",
                validationContext.MemberName is null ? null : [validationContext.MemberName]);
        }

        var dependentValue = dependentGetter(validationContext.ObjectInstance!);
        if (!this.MatchesDependentValue(dependentValue) || HasItems(value))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(
            this.FormatErrorMessage(validationContext.DisplayName),
            validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }

    private bool MatchesDependentValue(object? candidate)
    {
        return this._dependentValueTexts.Contains(candidate?.ToString());
    }

    /// <summary>
    /// Determines whether a collection contains at least one qualifying item. A qualifying item is
    /// non-<see langword="null"/> and, when its string representation is inspected, not blank — so a collection of
    /// whitespace-only strings is treated the same as an empty collection.
    /// </summary>
    /// <param name="value">The collection value to inspect.</param>
    /// <returns><see langword="true"/> when the collection contains at least one qualifying item.</returns>
    private static bool HasItems(object? value)
    {
        if (value is not IEnumerable enumerable)
        {
            return false;
        }

        var hasAny = false;

        foreach (var item in enumerable)
        {
            hasAny = true;

            if (item is null || string.IsNullOrWhiteSpace(item.ToString()))
            {
                return false;
            }
        }

        return hasAny;
    }
}
