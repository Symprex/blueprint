using Blueprint.SourceGenerator.Artifacts;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Scale;

/// <summary>
/// Builds deterministic machine-readable invalidation summaries from Tier 2 synthetic fixture edits so locality
/// scenarios can be reviewed and compared without relying only on imperative test assertions.
/// </summary>
public static class SyntheticFixtureInvalidationReport
{
    private const string ArtifactName = "scale-invalidation";
    private const string SectionName = "synthetic-scale-harness";
    private const string ContributorId = "Blueprint.SourceGenerator.Tests.Scale";
    private const string DefaultGeneratorVersion = "0.0.0.0";

    /// <summary>
    /// Creates a manifest-envelope report describing one synthetic edit scenario and the generated outputs it changed.
    /// </summary>
    /// <param name="fixture">The synthetic fixture that defines the stable scenario being edited.</param>
    /// <param name="mutationKind">A stable label describing the kind of source edit that was applied.</param>
    /// <param name="mutationSourcePath">The synthetic source file path that was edited.</param>
    /// <param name="editResult">The tracked before/after compilation result for the edit.</param>
    /// <returns>A deterministic manifest envelope for the invalidation scenario.</returns>
    public static GeneratedManifestEnvelope Create(
        SyntheticBlueprintFixture fixture,
        string mutationKind,
        string mutationSourcePath,
        MultiGeneratorEditResult editResult)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutationKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutationSourcePath);
        ArgumentNullException.ThrowIfNull(editResult);

        var generatorVersion = typeof(GeneratedManifestEnvelope).Assembly.GetName().Version?.ToString() ?? DefaultGeneratorVersion;
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            artifactName: ArtifactName,
            appTypeName: $"{fixture.NamespaceName}.{fixture.AppName}",
            assemblyName: typeof(SyntheticFixtureInvalidationReport).Assembly.GetName().Name ?? "Blueprint.SourceGenerator.Tests",
            generatorVersion: generatorVersion);

        manifest.AddSection(
            sectionName: SectionName,
            contributorId: ContributorId,
            payload: CreatePayload(fixture, mutationKind, mutationSourcePath, editResult));

        return manifest;
    }

    private static JObject CreatePayload(
        SyntheticBlueprintFixture fixture,
        string mutationKind,
        string mutationSourcePath,
        MultiGeneratorEditResult editResult)
    {
        return new JObject
        {
            ["scenario"] = CreateScenarioPayload(fixture, editResult),
            ["mutation"] = CreateMutationPayload(mutationKind, mutationSourcePath),
            ["generators"] = CreateGeneratorsPayload(editResult),
        };
    }

    private static JObject CreateScenarioPayload(SyntheticBlueprintFixture fixture, MultiGeneratorEditResult editResult)
    {
        return new JObject
        {
            ["namespaceName"] = fixture.NamespaceName,
            ["appName"] = fixture.AppName,
            ["operationCount"] = fixture.OperationCount,
            ["sourceFileCount"] = fixture.SourceFiles.Length,
            ["trackedGeneratorCount"] = GetTrackedGeneratorNames(editResult).Count,
        };
    }

    private static JObject CreateMutationPayload(string mutationKind, string mutationSourcePath)
    {
        return new JObject
        {
            ["kind"] = mutationKind,
            ["sourcePath"] = mutationSourcePath,
        };
    }

    private static JObject CreateGeneratorsPayload(MultiGeneratorEditResult editResult)
    {
        var generatorsPayload = new JObject();

        foreach (var generatorName in GetTrackedGeneratorNames(editResult))
        {
            var changedOutputs = editResult.GetChangedGeneratedSourceHintNames(generatorName);

            generatorsPayload.Add(
                generatorName,
                new JObject
                {
                    ["changedOutputCount"] = changedOutputs.Length,
                    ["changedOutputs"] = new JArray(changedOutputs),
                });
        }

        return generatorsPayload;
    }

    private static IReadOnlyList<string> GetTrackedGeneratorNames(MultiGeneratorEditResult editResult)
    {
        return editResult.Initial.GeneratedSources.Keys
            .Concat(editResult.Updated.GeneratedSources.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static generatorName => generatorName, StringComparer.Ordinal)
            .ToArray();
    }
}
