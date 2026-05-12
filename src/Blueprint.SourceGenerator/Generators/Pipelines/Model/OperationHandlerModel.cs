using Blueprint.SourceGenerator.Helpers;

namespace Blueprint.SourceGenerator.Generators.Pipelines.Model;

/// <summary>
/// Represents a discovered handler for an operation, including all the metadata needed
/// to generate the executor pipeline.
/// </summary>
/// <param name="OperationTypeName">The simple name of the operation type (e.g., "GetTemplatesQuery").</param>
/// <param name="OperationTypeFullName">The fully qualified name of the operation type.</param>
/// <param name="OperationNamespace">The namespace containing the operation.</param>
/// <param name="HandlerType">The type of handler (inline or external).</param>
/// <param name="HandlerMethodName">The name of the handler method (Invoke, InvokeAsync, Handle, HandleAsync).</param>
/// <param name="IsHandlerStatic">Whether the handler method is static.</param>
/// <param name="IsHandlerAsync">Whether the handler method is async (returns Task or Task&lt;T&gt;).</param>
/// <param name="HandlerReturnTypeName">The fully qualified return type name of the handler method.</param>
/// <param name="HandlerClassName">For external handlers, the simple name of the handler class. Null for inline handlers.</param>
/// <param name="HandlerClassFullName">For external handlers, the fully qualified name of the handler class. Null for inline handlers.</param>
/// <param name="HandlerClassNamespace">For external handlers, the namespace of the handler class. Null for inline handlers.</param>
/// <param name="Parameters">The list of parameters for the handler method.</param>
/// <param name="ConstructorParameters">
/// For external handlers, the constructor parameters of the handler class. Used to generate
/// a direct <c>new Handler(...)</c> call instead of <c>ActivatorUtilities.CreateInstance</c>.
/// Null for inline handlers or when the constructor cannot be statically resolved.
/// </param>
/// <param name="PropertyInjections">
/// For external handlers, any settable base-class properties that should be populated from
/// well-known pipeline expressions after handler instantiation (e.g., <c>Context</c>,
/// <c>Identity</c>, <c>Logger</c>, <c>CancellationToken</c>). Empty for inline handlers
/// or handlers without such properties.
/// </param>
public sealed record OperationHandlerModel(
    string OperationTypeName,
    string OperationTypeFullName,
    string OperationNamespace,
    HandlerType HandlerType,
    string HandlerMethodName,
    bool IsHandlerStatic,
    bool IsHandlerAsync,
    string HandlerReturnTypeName,
    string? HandlerClassName,
    string? HandlerClassFullName,
    string? HandlerClassNamespace,
    EquatableList<HandlerParameter> Parameters,
    EquatableList<ConstructorParameterInfo>? ConstructorParameters = null,
    EquatableList<HandlerPropertyInjection>? PropertyInjections = null) : IEquatable<OperationHandlerModel>
{
    /// <summary>
    /// Gets the name to use for the generated executor pipeline class.
    /// </summary>
    public string PipelineClassName => $"{this.OperationTypeName}ExecutorPipeline";

    /// <summary>
    /// Gets whether this is an inline handler (method on the operation class).
    /// </summary>
    public bool IsInline => this.HandlerType == HandlerType.Inline;

    /// <summary>
    /// Gets whether this is an external handler (method on a separate handler class).
    /// </summary>
    public bool IsExternal => this.HandlerType == HandlerType.External;

    /// <inheritdoc />
    public bool Equals(OperationHandlerModel? other)
    {
        return other is null
            ? false
            : ReferenceEquals(this, other)
            ? true
            : this.OperationTypeName == other.OperationTypeName &&
               this.OperationTypeFullName == other.OperationTypeFullName &&
               this.OperationNamespace == other.OperationNamespace &&
               this.HandlerType == other.HandlerType &&
               this.HandlerMethodName == other.HandlerMethodName &&
               this.IsHandlerStatic == other.IsHandlerStatic &&
               this.IsHandlerAsync == other.IsHandlerAsync &&
               this.HandlerReturnTypeName == other.HandlerReturnTypeName &&
               this.HandlerClassName == other.HandlerClassName &&
               this.HandlerClassFullName == other.HandlerClassFullName &&
               this.HandlerClassNamespace == other.HandlerClassNamespace &&
               this.Parameters == other.Parameters &&
               this.ConstructorParameters == other.ConstructorParameters &&
               this.PropertyInjections == other.PropertyInjections;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(System.HashCode);
        hash.Add(this.OperationTypeName);
        hash.Add(this.OperationTypeFullName);
        hash.Add(this.OperationNamespace);
        hash.Add(this.HandlerType);
        hash.Add(this.HandlerMethodName);
        hash.Add(this.IsHandlerStatic);
        hash.Add(this.IsHandlerAsync);
        hash.Add(this.HandlerReturnTypeName);
        hash.Add(this.HandlerClassName);
        hash.Add(this.HandlerClassFullName);
        hash.Add(this.HandlerClassNamespace);
        hash.Add(this.Parameters);
        hash.Add(this.ConstructorParameters);
        hash.Add(this.PropertyInjections);
        return hash.ToHashCode();
    }
}
