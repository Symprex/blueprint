using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents a parameter on a handler method, including how it should be resolved.
/// </summary>
/// <param name="ParameterName">The name of the parameter.</param>
/// <param name="ParameterTypeName">The fully qualified type name of the parameter.</param>
/// <param name="Source">The source from which this parameter should be resolved.</param>
/// <param name="ContextPropertyName">If Source is FromContextProperty, the name of the property to access (e.g., "App", "OperationCancelled").</param>
public sealed record HandlerParameter(
    string ParameterName,
    string ParameterTypeName,
    ParameterSource Source,
    string? ContextPropertyName = null) : IEquatable<HandlerParameter>
{
    /// <summary>
    /// Gets the parameter name in camelCase for use as a local variable.
    /// </summary>
    public string VariableName => char.ToLowerInvariant(this.ParameterName[0]) + this.ParameterName.Substring(1);

    /// <inheritdoc />
    public bool Equals(HandlerParameter? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return this.ParameterName == other.ParameterName &&
               this.ParameterTypeName == other.ParameterTypeName &&
               this.Source == other.Source &&
               this.ContextPropertyName == other.ContextPropertyName;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(this.ParameterName, this.ParameterTypeName, this.Source, this.ContextPropertyName);
    }
}
