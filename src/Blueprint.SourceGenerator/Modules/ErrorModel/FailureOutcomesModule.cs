using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects the ordered default exception-translator policy into additive descriptor feature data so downstream hosts can
/// inspect stable failure facts without re-discovering catch ownership from generated pipeline code.
/// When the policy matches the well-known default, emits a shared static reference to avoid per-descriptor allocation.
/// </summary>
public sealed class FailureOutcomesModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
    private static readonly HashSet<string> DefaultTranslatorIds =
    [
        "legacy-validation-exception",
        "data-annotations-validation-exception",
        "default-unhandled-exception",
    ];

    /// <inheritdoc />
    public string ModuleId => nameof(FailureOutcomesModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(FailureOutcomesModule);

    /// <inheritdoc />
    public int Order => 60;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [];

    /// <inheritdoc />
    public void ContributeToDescriptor(DescriptorContributorContext context)
    {
        var policy = context.GetExceptionTranslatorPolicy();
        if (policy is null)
        {
            // The pipeline generator already reports policy diagnostics; descriptor projection should not duplicate them
            // per operation when the shared default policy becomes invalid.
            return;
        }

        var outcomes = policy.Translators
            .Select(static translator => new FailureOutcomeRegistration(
                translator.TranslatorId,
                translator.HandledExceptionTypeName,
                translator.ResultTypeName))
            .ToArray();

        foreach (var outcome in outcomes)
        {
            context.AddMetadata(outcome);
        }

        var isDefaultPolicy = policy.Translators.Count == DefaultTranslatorIds.Count
            && policy.Translators.All(static t => DefaultTranslatorIds.Contains(t.TranslatorId));

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<FailureOutcomeRegistration>(), isDefaultPolicy));
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<FailureOutcomeRegistration> registrations, bool useWellKnown)
    {
        if (useWellKnown)
        {
            codeWriter.AppendLine("this.SetFeatureData(global::Blueprint.Model.WellKnownFeatureData.DefaultFailureOutcomes);");
            return;
        }

        var renderedRegistrations = registrations
            .Select(static registration =>
                $"new global::Blueprint.Model.OperationFailureOutcome({RenderStringLiteral(registration.TranslatorId)}, {RenderStringLiteral(registration.HandledExceptionTypeName)}, {RenderStringLiteral(registration.ResultTypeName)})")
            .ToArray();

        if (renderedRegistrations.Length == 0)
        {
            return;
        }

        codeWriter.AppendLine(
            "this.SetFeatureData(new global::Blueprint.Model.OperationFailureOutcomesFeatureData(global::System.Collections.Immutable.ImmutableArray.Create("
            + string.Join(", ", renderedRegistrations)
            + ")));");
    }

    private static string RenderStringLiteral(string value)
    {
        return CSharpSymbolDisplay.FormatLiteral(value, quote: true);
    }
}
