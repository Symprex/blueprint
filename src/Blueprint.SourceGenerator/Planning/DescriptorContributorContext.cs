using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Context passed to descriptor contributors.
/// </summary>
public sealed class DescriptorContributorContext
{
    private readonly OperationModel _operation;
    private readonly DescriptorPlan _plan;
    private readonly IOperationLookup _operationLookup;
    private readonly PipelineExceptionTranslatorPolicy? _exceptionTranslatorPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DescriptorContributorContext"/> class.
    /// </summary>
    /// <param name="operation">The current operation.</param>
    /// <param name="plan">The descriptor plan under construction.</param>
    /// <param name="operationLookup">A lookup for related operations.</param>
    public DescriptorContributorContext(
        OperationModel operation,
        DescriptorPlan plan,
        IOperationLookup operationLookup)
    {
        this._operation = operation;
        this._plan = plan;
        this._operationLookup = operationLookup;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DescriptorContributorContext"/> class with the resolved
    /// exception-translation policy for the active generator configuration.
    /// </summary>
    /// <param name="operation">The current operation.</param>
    /// <param name="plan">The descriptor plan under construction.</param>
    /// <param name="operationLookup">A lookup for related operations.</param>
    /// <param name="exceptionTranslatorPolicy">The resolved exception-translation policy for the active generator configuration.</param>
    internal DescriptorContributorContext(
        OperationModel operation,
        DescriptorPlan plan,
        IOperationLookup operationLookup,
        PipelineExceptionTranslatorPolicy? exceptionTranslatorPolicy)
        : this(operation, plan, operationLookup)
    {
        this._exceptionTranslatorPolicy = exceptionTranslatorPolicy;
    }

    /// <summary>
    /// Gets the Roslyn symbol for the current operation.
    /// </summary>
    /// <returns>The operation type symbol.</returns>
    public INamedTypeSymbol GetOperationTypeSymbol()
    {
        return this._operation.TypeSymbol;
    }

    /// <summary>
    /// Adds a diagnostic to the descriptor plan.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        this._plan.AddDiagnostic(diagnostic);
    }

    /// <summary>
    /// Adds descriptor metadata.
    /// </summary>
    /// <param name="metadata">The metadata instance.</param>
    public void AddMetadata(object metadata)
    {
        this._plan.AddMetadata(metadata);
    }

    /// <summary>
    /// Gets all descriptor metadata of the requested type.
    /// </summary>
    /// <typeparam name="T">The metadata type.</typeparam>
    /// <returns>The matching metadata items.</returns>
    public IEnumerable<T> GetMetadata<T>()
    {
        return this._plan.GetMetadata<T>();
    }

    /// <summary>
    /// Finds a related operation by its metadata name.
    /// </summary>
    /// <param name="metadataName">The metadata name to resolve.</param>
    /// <returns>The matching operation model, if found.</returns>
    public OperationModel? FindOperationByMetadataName(string metadataName)
    {
        return this._operationLookup.FindByMetadataName(metadataName);
    }

    /// <summary>
    /// Finds a named type by its metadata name.
    /// </summary>
    /// <param name="metadataName">The metadata name to resolve.</param>
    /// <returns>The matching type symbol, if found.</returns>
    public INamedTypeSymbol? FindTypeByMetadataName(string metadataName)
    {
        return this._operationLookup.FindTypeByMetadataName(metadataName);
    }

    /// <summary>
    /// Gets the resolved exception-translation policy for the active generator configuration when one is available.
    /// </summary>
    /// <returns>The current exception-translation policy, or <see langword="null"/> when the generator could not build one.</returns>
    internal PipelineExceptionTranslatorPolicy? GetExceptionTranslatorPolicy()
    {
        return this._exceptionTranslatorPolicy;
    }

    /// <summary>
    /// Adds a constrained render step used to emit descriptor code.
    /// </summary>
    /// <param name="renderStep">The render step callback.</param>
    public void AddRenderStep(Action<ICodeWriter> renderStep)
    {
        this._plan.AddRenderStep(renderStep);
    }

    /// <summary>
    /// Gets a value indicating whether the descriptor plan currently contains diagnostics.
    /// </summary>
    /// <returns><c>true</c> if diagnostics exist; otherwise <c>false</c>.</returns>
    public bool HasDiagnostics()
    {
        return this._plan.Diagnostics.Count != 0;
    }
}
