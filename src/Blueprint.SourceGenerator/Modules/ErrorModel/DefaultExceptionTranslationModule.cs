using Blueprint.SourceGenerator.Generators.Pipelines;
using Blueprint.SourceGenerator.Planning;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Contributes Blueprint's built-in exception translators through the module system so companion packages can extend
/// the same ordered policy without bypassing descriptor or host failure metadata.
/// </summary>
public sealed class DefaultExceptionTranslationModule : IBlueprintSourceGeneratorModule, IExceptionTranslationContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(DefaultExceptionTranslationModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(DefaultExceptionTranslationModule);

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToExceptionTranslation(ExceptionTranslationContributorContext context)
    {
        context.AddTranslators(
        [
            LegacyValidationExceptionTranslator.Instance,
            DataAnnotationsValidationExceptionTranslator.Instance,
            DefaultPipelineExceptionTranslator.Instance,
        ]);
    }
}
