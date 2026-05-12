namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Describes a settable property on an external handler class that should be populated from
/// a well-known pipeline expression after the handler is instantiated. This supports the
/// Signature 365 <c>ApiOperations</c> pattern where handlers inherit <c>Context</c>,
/// <c>Identity</c>, <c>Logger</c>, and <c>CancellationToken</c> as base-class properties.
/// </summary>
/// <param name="PropertyName">The name of the property on the handler class (e.g., <c>"Identity"</c>).</param>
/// <param name="SourceExpression">
/// The C# expression to assign to the property, evaluated in the pipeline's <c>ExecuteCoreAsync</c> scope.
/// For example, <c>"context.UserAuthorisationContext"</c> or <c>"context.OperationCancelled"</c>.
/// </param>
public sealed record HandlerPropertyInjection(
    string PropertyName,
    string SourceExpression) : IEquatable<HandlerPropertyInjection>
{
    /// <inheritdoc />
    public bool Equals(HandlerPropertyInjection? other)
    {
        return other is not null
               && this.PropertyName == other.PropertyName
               && this.SourceExpression == other.SourceExpression;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(this.PropertyName, this.SourceExpression);
    }
}
