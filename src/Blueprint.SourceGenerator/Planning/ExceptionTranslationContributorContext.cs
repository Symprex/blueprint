using Blueprint.SourceGenerator.Generators.Pipelines;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Collects exception translators contributed by modules before policy ordering and conflict validation run.
/// </summary>
public sealed class ExceptionTranslationContributorContext
{
    private readonly List<IPipelineExceptionTranslator> _translators = [];

    /// <summary>
    /// Adds one exception translator to the active contribution set.
    /// </summary>
    /// <param name="translator">The translator to add.</param>
    public void AddTranslator(IPipelineExceptionTranslator translator)
    {
        if (translator is null)
        {
            throw new ArgumentNullException(nameof(translator));
        }

        this._translators.Add(translator);
    }

    /// <summary>
    /// Adds several exception translators to the active contribution set.
    /// </summary>
    /// <param name="translators">The translators to add.</param>
    public void AddTranslators(IEnumerable<IPipelineExceptionTranslator> translators)
    {
        if (translators is null)
        {
            throw new ArgumentNullException(nameof(translators));
        }

        foreach (var translator in translators)
        {
            this.AddTranslator(translator);
        }
    }

    /// <summary>
    /// Returns the contributed translators in contribution order so the policy can apply its own deterministic sorting.
    /// </summary>
    /// <returns>The contributed translators.</returns>
    public IReadOnlyList<IPipelineExceptionTranslator> GetTranslators()
    {
        return this._translators;
    }
}
