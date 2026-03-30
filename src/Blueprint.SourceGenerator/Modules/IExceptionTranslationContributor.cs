using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes exception translators that participate in the shared ordered translation policy.
/// </summary>
public interface IExceptionTranslationContributor : IBlueprintContributor
{
    /// <summary>
    /// Contributes exception translators for the current source-generation configuration.
    /// </summary>
    /// <param name="context">The contribution context.</param>
    void ContributeToExceptionTranslation(ExceptionTranslationContributorContext context);
}
