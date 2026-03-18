namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Mutable pipeline planning state for a single operation.
/// </summary>
/// <param name="Operation">The operation being planned.</param>
/// <param name="VariableRegistry">The variable registry for the plan.</param>
public sealed record PipelinePlan(OperationModel Operation, VariableRegistry VariableRegistry)
{
    private readonly List<PipelineContribution> _contributions = [];

    /// <summary>
    /// Gets the ordered pipeline contributions.
    /// </summary>
    public IReadOnlyList<PipelineContribution> Contributions => this._contributions;

    /// <summary>
    /// Adds a contribution to the pipeline plan.
    /// </summary>
    /// <param name="contribution">The contribution to add.</param>
    public void AddContribution(PipelineContribution contribution)
    {
        this._contributions.Add(contribution);
    }
}

/// <summary>
/// A single ordered pipeline contribution.
/// </summary>
/// <param name="Stage">The target stage.</param>
/// <param name="ContributorId">The contributing contributor identifier.</param>
/// <param name="Description">A short description of the contribution.</param>
public sealed record PipelineContribution(
    PipelineStage Stage,
    string ContributorId,
    string Description);
