using Blueprint.SourceGenerator.CodeWriter;
using Blueprint.SourceGenerator.Planning;
using CSharpSymbolDisplay = Microsoft.CodeAnalysis.CSharp.SymbolDisplay;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Projects the ordered default exception-translator policy into additive descriptor feature data so downstream hosts can
/// inspect stable failure facts without re-discovering catch ownership from generated pipeline code.
/// </summary>
public sealed class FailureOutcomesModule : IBlueprintSourceGeneratorModule, IDescriptorContributor
{
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

        var registrations = policy.Translators
            .Select(static translator => new FailureOutcomeRegistration(
                translator.TranslatorId,
                translator.HandledExceptionTypeName,
                translator.ResultTypeName))
            .ToArray();

        foreach (var registration in registrations)
        {
            context.AddMetadata(registration);
        }

        context.AddRenderStep(writer => RenderDescriptor(writer, context.GetMetadata<FailureOutcomeRegistration>()));
    }

    private static void RenderDescriptor(ICodeWriter codeWriter, IEnumerable<FailureOutcomeRegistration> registrations)
    {
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

    private sealed record FailureOutcomeRegistration(
        string TranslatorId,
        string HandledExceptionTypeName,
        string ResultTypeName);
}
