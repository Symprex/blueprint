using System.Text.RegularExpressions;
using Blueprint.Runtime.Utilities;
using JetBrains.Annotations;

namespace Blueprint.Authorization;

/// <summary>
/// Base attribute that declares a claim requirement on an operation. At runtime, the
/// <see cref="ClaimsRequiredApiAuthoriser"/> evaluates this attribute to determine whether
/// the current user's claims satisfy the demand.
/// </summary>
/// <remarks>
/// <para>
/// The value template supports property token replacement using the pattern
/// <c>{PropertyName}</c> or <c>{PropertyName:FallbackProperty}</c>. At runtime, the tokens
/// are resolved against the operation instance to produce a concrete resource key.
/// </para>
/// <para>
/// Consumers can derive specialized attributes from this base type when they want a reusable way to
/// declare claim requirements across multiple operations.
/// </para>
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class ClaimRequiredAttribute : Attribute
{
    private static readonly Regex ResourceKeyPropertyRegex =
        new("{(?<propName>.*?)(:(?<alternatePropName>.*?))?}", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaimRequiredAttribute"/> class.
    /// </summary>
    /// <param name="claimType">The claim type URN (e.g. <see cref="Blueprint.Runtime.Authorization.ClaimTypes.Permission"/>).</param>
    /// <param name="valueTemplate">A resource key template, possibly containing <c>{PropertyName}</c> tokens.</param>
    /// <param name="valueType">The permission name (e.g. "Edit", "ViewReport").</param>
    public ClaimRequiredAttribute(string claimType, string valueTemplate, string valueType)
    {
        this.ClaimType = claimType;
        this.ValueTemplate = valueTemplate;
        this.ValueType = valueType;
    }

    /// <summary>
    /// Gets the claim type URN (e.g. "urn:claims/permission").
    /// </summary>
    public string ClaimType { get; }

    /// <summary>
    /// Gets the resource key template, possibly containing <c>{PropertyName}</c> tokens.
    /// </summary>
    public string ValueTemplate { get; }

    /// <summary>
    /// Gets the permission name (e.g. "Edit", "ViewReport").
    /// </summary>
    public string ValueType { get; }

    /// <summary>
    /// Resolves the <see cref="ValueTemplate"/> against the given resource instance,
    /// replacing property tokens with actual values from the object's properties.
    /// </summary>
    /// <param name="resource">The operation instance to extract property values from.</param>
    /// <returns>A <see cref="Claim"/> with the resolved resource key.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a referenced property does not exist on the resource type.
    /// </exception>
    public Claim GetClaim(object resource)
    {
        var resolvedValue = ResourceKeyPropertyRegex.Replace(this.ValueTemplate, match =>
        {
            var propName = match.Groups["propName"].Value;
            var alternatePropName = match.Groups["alternatePropName"].Value;

            var value = GetPropertyValue(resource, propName);

            if (value == null && !string.IsNullOrEmpty(alternatePropName))
            {
                value = GetPropertyValue(resource, alternatePropName);
            }

            return value?.ToString() ?? string.Empty;
        });

        return new Claim(this.ClaimType, resolvedValue, this.ValueType);
    }

    private static object? GetPropertyValue(object resource, string propertyName)
    {
        var type = resource.GetType();
        var getter = CompiledPropertyAccessor.GetGetter(type, propertyName);

        if (getter == null)
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' not found on type '{type.FullName}'. " +
                $"Ensure the operation has a public property matching the claim template token.");
        }

        return getter(resource);
    }
}
