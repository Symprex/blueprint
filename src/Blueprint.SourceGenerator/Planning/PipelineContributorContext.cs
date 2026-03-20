using Blueprint.SourceGenerator.Generators.Pipelines.Model;

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
}
