using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a collection property does not exceed a maximum number of items.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MaxItemsListAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MaxItemsListAttribute"/> class.
    /// </summary>
    /// <param name="maximumItems">The maximum number of items allowed in the collection.</param>
    public MaxItemsListAttribute(int maximumItems)
        : base("The {0} field must not contain more than {1} items.")
    {
        this.MaximumItems = maximumItems;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MaxItemsListAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="maximumItems">The maximum number of items allowed in the collection.</param>
    /// <param name="errorMessage">The custom error message template.</param>
    public MaxItemsListAttribute(int maximumItems, string errorMessage)
        : base(errorMessage)
    {
        this.MaximumItems = maximumItems;
    }

    /// <summary>
    /// Gets the maximum number of items allowed in the collection.
    /// </summary>
    public int MaximumItems { get; }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name, this.MaximumItems);
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is ICollection collection)
        {
            return collection.Count <= this.MaximumItems;
        }

        if (value is IEnumerable enumerable)
        {
            var count = 0;
            var enumerator = enumerable.GetEnumerator();

            while (enumerator.MoveNext())
            {
                count++;

                if (count > this.MaximumItems)
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }
}
