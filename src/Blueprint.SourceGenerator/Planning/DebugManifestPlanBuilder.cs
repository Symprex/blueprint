using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Modules;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Builds the shared per-operation debug-manifest plan that later artifact emitters can project into stable envelopes.
/// </summary>
internal static class DebugManifestPlanBuilder
{
    /// <summary>
    /// Builds a debug manifest for the requested operation using the currently configured contributor execution plan.
    /// </summary>
    /// <param name="operationFullClassName">The fully qualified metadata name of the operation implementation.</param>
    /// <param name="compilation">The Roslyn compilation that contains the operation and contributor metadata.</param>
    /// <param name="executionPlanResult">The configured contributor execution plan.</param>
    /// <returns>A successful debug manifest or the diagnostics that prevented one from being built.</returns>
    public static Result<GeneratedDebugManifest> Build(
        string operationFullClassName,
        Compilation compilation,
        Result<BlueprintModuleExecutionPlan> executionPlanResult)
    {
        if (executionPlanResult.Value is null)
        {
            return Result<GeneratedDebugManifest>.Failure(executionPlanResult.Diagnostics);
        }

        var operationImplementationType = compilation.GetTypeByMetadataName(NormalizeMetadataName(operationFullClassName));

        if (operationImplementationType is null)
        {
            return Result<GeneratedDebugManifest>.Failure(
                new DiagnosticInfo(
                    FoundationModuleDiagnosticDescriptors.MissingOperationType,
                    (Location?)null,
                    [
                        operationFullClassName,
                        "debug-manifest",
                    ]));
        }

        var operationModel = OperationModel.Create(operationImplementationType);
        var debugManifest = new GeneratedDebugManifest(operationModel);
        var operationLookup = new CompilationOperationLookup(compilation);

        foreach (var contributor in executionPlanResult.Value.DebugManifestContributors)
        {
            contributor.ContributeToManifest(new DebugManifestContributorContext(operationModel, debugManifest, operationLookup));
        }

        return Result.Success(debugManifest);
    }

    private static string NormalizeMetadataName(string metadataName)
    {
        return metadataName.StartsWith("global::", StringComparison.Ordinal)
            ? metadataName.Substring("global::".Length)
            : metadataName;
    }
}
