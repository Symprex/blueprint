namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents the source from which a handler parameter should be resolved.
/// </summary>
public enum ParameterSource
{
    /// <summary>
    /// The parameter is the operation instance itself (from context.Operation).
    /// </summary>
    Operation,

    /// <summary>
    /// The parameter is the ApiOperationContext itself.
    /// </summary>
    FromContext,

    /// <summary>
    /// The parameter should be resolved from a property on ApiOperationContext
    /// (e.g., context.App, context.OperationCancelled).
    /// </summary>
    FromContextProperty,

    /// <summary>
    /// The parameter should be resolved from the dependency injection container
    /// using context.ServiceProvider.GetRequiredService&lt;T&gt;().
    /// </summary>
    FromDependencyInjection,
}
