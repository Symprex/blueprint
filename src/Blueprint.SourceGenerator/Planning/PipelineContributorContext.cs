using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Context passed to pipeline contributors.
/// </summary>
public sealed class PipelineContributorContext
{
    private readonly string _contributorId;
    private readonly OperationModel _operation;
    private readonly PipelinePlan _plan;
    private readonly IOperationLookup _operationLookup;
    private readonly OperationHandlerModel? _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineContributorContext"/> class.
    /// </summary>
    /// <param name="contributorId">The active contributor identifier.</param>
    /// <param name="operation">The current operation.</param>
    /// <param name="plan">The pipeline plan under construction.</param>
    /// <param name="operationLookup">A lookup for related operations.</param>
    /// <param name="handler">The discovered handler metadata when available.</param>
    public PipelineContributorContext(
        string contributorId,
        OperationModel operation,
        PipelinePlan plan,
        IOperationLookup operationLookup,
        OperationHandlerModel? handler = null)
    {
        this._contributorId = contributorId;
        this._operation = operation;
        this._plan = plan;
        this._operationLookup = operationLookup;
        this._handler = handler;
    }

    /// <summary>
    /// Gets the current operation metadata.
    /// </summary>
    public OperationModel Operation => this._operation;

    /// <summary>
    /// Gets the discovered handler metadata when available.
    /// </summary>
    public OperationHandlerModel? Handler => this._handler;

    /// <summary>
    /// Adds a render step for the active contributor.
    /// </summary>
    /// <param name="stage">The target stage.</param>
    /// <param name="render">The contributor-owned render callback.</param>
    public void AddRenderStep(PipelineStage stage, Action<PipelineRenderContext> render)
    {
        this._plan.AddRenderStep(new PipelineRenderStep(stage, this._contributorId, render));
    }

    /// <summary>
    /// Marks the generated pipeline as requiring an async method signature even when
    /// the handler itself is synchronous. Use when a contributor adds render steps that
    /// emit <c>await</c> expressions (for example async body reading in the Population stage).
    /// </summary>
    public void RequireAsync()
    {
        this._plan.RequiresAsync = true;
    }

    /// <summary>
    /// Registers a variable produced by the active contributor.
    /// </summary>
    /// <param name="key">The variable key.</param>
    /// <param name="availableFrom">The stage at which the value is available.</param>
    /// <param name="sourceKind">The source kind for the value.</param>
    /// <param name="lifetime">The variable lifetime.</param>
    /// <param name="allowMultiple">Whether multiple producers are allowed for the same key.</param>
    public void RegisterVariable(
        VariableKey key,
        PipelineStage availableFrom,
        VariableSourceKind sourceKind,
        VariableLifetime lifetime,
        bool allowMultiple = false)
    {
        this._plan.EnsureContribution(availableFrom, this._contributorId);
        this._plan.VariableRegistry.RegisterVariable(
            new VariableRegistration(
                key,
                ProducerId: this._contributorId,
                AvailableFrom: availableFrom,
                SourceKind: sourceKind,
                Lifetime: lifetime,
                AllowMultiple: allowMultiple));
    }

    /// <summary>
    /// Registers a variable required by the active contributor.
    /// </summary>
    /// <param name="key">The variable key.</param>
    /// <param name="requiredBy">The stage by which the variable must be available.</param>
    /// <param name="isRequired">Whether the value is required.</param>
    public void RequireVariable(VariableKey key, PipelineStage requiredBy, bool isRequired = true)
    {
        this._plan.EnsureContribution(requiredBy, this._contributorId);
        this._plan.VariableRegistry.RequireVariable(
            new VariableRequirement(key, this._contributorId, requiredBy, isRequired));
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
    /// Finds any named type by its metadata name.
    /// </summary>
    /// <param name="metadataName">The metadata name to resolve.</param>
    /// <returns>The matching type symbol, if found.</returns>
    public INamedTypeSymbol? FindTypeByMetadataName(string metadataName)
    {
        return this._operationLookup.FindTypeByMetadataName(metadataName);
    }

    /// <summary>
    /// Adds a diagnostic to the active pipeline plan.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        this._plan.AddDiagnostic(diagnostic);
    }

    /// <summary>
    /// Registers a class-level helper method (or other member) on the generated pipeline class.
    /// The callback is deferred until code generation applies it to the <see cref="CodeWriter.ClassBuilder"/>
    /// before the source is built, so the member is present alongside <c>ExecuteAsync</c>.
    /// </summary>
    /// <param name="configure">A callback that receives the pipeline class builder and adds the member.</param>
    public void AddDeferredClassMember(Action<CodeWriter.ClassBuilder> configure)
    {
        this._plan.AddDeferredClassMember(configure);
    }

    /// <summary>
    /// Finds all self-link URL templates declared for the given resource type across the compilation.
    /// </summary>
    /// <param name="resourceType">The resource type symbol to look up self-link templates for.</param>
    /// <returns>Self-link binding information, which may be empty when no self-links target this resource type.</returns>
    public IReadOnlyList<SelfLinkBindingInfo> FindSelfLinksForResourceType(INamedTypeSymbol resourceType)
    {
        return this._operationLookup.FindSelfLinksForResourceType(resourceType);
    }
}
