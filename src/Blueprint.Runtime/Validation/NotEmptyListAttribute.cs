using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a collection property is not empty (contains at least one element).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotEmptyListAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotEmptyListAttribute"/> class with the default error message.
    /// </summary>
    public NotEmptyListAttribute()
        : base("The {0} field must contain at least one item.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotEmptyListAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The custom error message template.</param>
    public NotEmptyListAttribute(string errorMessage)
        : base(errorMessage)
    {
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name)
    {
        return string.Format(CultureInfo.CurrentCulture, this.ErrorMessageString, name);
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
            return collection.Count > 0;
        }

        if (value is IEnumerable enumerable)
        {
            var enumerator = enumerable.GetEnumerator();

            try
            {
                return enumerator.MoveNext();
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        return false;
    }
}
