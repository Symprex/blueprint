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
    /// Gets the local the pipeline resolves this parameter into: the parameter's name behind a prefix no other generated local
    /// uses. A handler's author chooses the name, so without the prefix it could be one the pipeline declares itself
    /// (<c>context</c>, <c>operation</c>, <c>handlerResult</c>, ...), a keyword (<c>@event</c>), or one that differs from
    /// another parameter's only in case, and the pipeline would not compile.
    /// </summary>
    public string VariableName => "_handlerArg_" + this.ParameterName;

    /// <inheritdoc />
    public bool Equals(HandlerParameter? other)
    {
        return other is null
            ? false
            : ReferenceEquals(this, other)
            ? true
            : this.ParameterName == other.ParameterName &&
               this.ParameterTypeName == other.ParameterTypeName &&
               this.Source == other.Source &&
               this.ContextPropertyName == other.ContextPropertyName;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return System.HashCode.Combine(this.ParameterName, this.ParameterTypeName, this.Source, this.ContextPropertyName);
    }
}
