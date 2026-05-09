using System.Text.RegularExpressions;

namespace Blueprint.Validation;

/// <summary>
/// Validates that a string value represents a resource key — a type prefix followed by a slash
/// and a GUID (e.g. <c>Something/5C65D124-F856-46A4-BB3F-19764429BA1F</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ResourceKeyAttribute : RegexAttribute
{
    private static readonly Regex ResourceKeyPattern =
        new(
            @"^[a-z][a-z0-9]+/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceKeyAttribute"/> class with the default error message.
    /// </summary>
    public ResourceKeyAttribute()
        : base(ResourceKeyPattern)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceKeyAttribute"/> class with a custom error message.
    /// </summary>
    /// <param name="errorMessage">The error message to display on validation failure.</param>
    public ResourceKeyAttribute(string errorMessage)
        : base(ResourceKeyPattern, errorMessage)
    {
    }
}
