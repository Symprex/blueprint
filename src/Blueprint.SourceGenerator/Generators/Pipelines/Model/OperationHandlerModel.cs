using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

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
    EquatableList<HandlerParameter> Parameters) : IEquatable<OperationHandlerModel>
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
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return this.OperationTypeName == other.OperationTypeName &&
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
               this.Parameters == other.Parameters;
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
        return hash.ToHashCode();
    }
}
