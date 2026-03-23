using System.Collections.Immutable;
using Blueprint.SourceGenerator.Artifacts;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Tests.Scale;

/// <summary>
/// Builds deterministic build-telemetry summaries from the synthetic Tier 2 harness so later `.binlog` and
/// design-time-build capture can publish into a reviewed contract instead of inventing a new artifact family.
/// </summary>
public static class SyntheticBuildTelemetryReport
{
    private const string ArtifactName = "build-telemetry";
    private const string SectionName = "synthetic-build-harness";
    private const string ContributorId = "Blueprint.SourceGenerator.Tests.Scale";
    private const string DefaultGeneratorVersion = "0.0.0.0";

    /// <summary>
    /// Creates a manifest-envelope report describing one synthetic build telemetry capture.
    /// </summary>
    /// <param name="fixture">The synthetic fixture that defines the scenario being compiled.</param>
    /// <param name="captureName">The stable label describing the capture kind, such as <c>cold-build</c>.</param>
    /// <param name="isDesignTimeBuild">Whether the captured telemetry represents a design-time build shape.</param>
    /// <param name="duration">The measured duration for the capture.</param>
    /// <param name="result">The tracked generator result captured for the compiled scenario.</param>
    /// <param name="mutationKind">An optional stable label describing the source edit applied before the capture.</param>
    /// <param name="mutationSourcePath">The optional synthetic source file path that was edited before the capture.</param>
    /// <returns>A deterministic manifest envelope for the build telemetry capture.</returns>
    public static GeneratedManifestEnvelope Create(
        SyntheticBlueprintFixture fixture,
        string captureName,
        bool isDesignTimeBuild,
        TimeSpan duration,
        MultiGeneratorGenerationResult result,
        string? mutationKind = null,
        string? mutationSourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureName);
        ArgumentNullException.ThrowIfNull(result);

        var hasMutationKind = !string.IsNullOrWhiteSpace(mutationKind);
        var hasMutationSourcePath = !string.IsNullOrWhiteSpace(mutationSourcePath);
        if (hasMutationKind != hasMutationSourcePath)
        {
            throw new ArgumentException("Mutation kind and mutation source path must either both be provided or both be omitted.");
        }

        var generatorVersion = typeof(GeneratedManifestEnvelope).Assembly.GetName().Version?.ToString() ?? DefaultGeneratorVersion;
        var manifest = GeneratedManifestEnvelope.CreateBuildManifest(
            artifactName: ArtifactName,
            appTypeName: $"{fixture.NamespaceName}.{fixture.AppName}",
            assemblyName: typeof(SyntheticBuildTelemetryReport).Assembly.GetName().Name ?? "Blueprint.SourceGenerator.Tests",
            generatorVersion: generatorVersion);

        manifest.AddSection(
            sectionName: SectionName,
            contributorId: ContributorId,
            payload: CreatePayload(fixture, captureName, isDesignTimeBuild, duration, result, mutationKind, mutationSourcePath));

        return manifest;
    }

    private static JObject CreatePayload(
        SyntheticBlueprintFixture fixture,
        string captureName,
        bool isDesignTimeBuild,
        TimeSpan duration,
        MultiGeneratorGenerationResult result,
        string? mutationKind,
        string? mutationSourcePath)
    {
        var payload = new JObject
        {
            ["scenario"] = CreateScenarioPayload(fixture, result),
            ["capture"] = CreateCapturePayload(captureName, isDesignTimeBuild, duration),
            ["generators"] = CreateGeneratorsPayload(result),
        };

        if (!string.IsNullOrWhiteSpace(mutationKind))
        {
            payload["mutation"] = CreateMutationPayload(mutationKind, mutationSourcePath!);
        }

        return payload;
    }

    private static JObject CreateScenarioPayload(SyntheticBlueprintFixture fixture, MultiGeneratorGenerationResult result)
    {
        return new JObject
        {
            ["namespaceName"] = fixture.NamespaceName,
            ["appName"] = fixture.AppName,
            ["operationCount"] = fixture.OperationCount,
            ["sourceFileCount"] = fixture.SourceFiles.Length,
            ["trackedGeneratorCount"] = GetTrackedGeneratorNames(result).Count,
            ["diagnosticCount"] = result.Diagnostics.Length,
        };
    }

    private static JObject CreateCapturePayload(string captureName, bool isDesignTimeBuild, TimeSpan duration)
    {
        return new JObject
        {
            ["captureName"] = captureName,
            ["isDesignTimeBuild"] = isDesignTimeBuild,
            ["durationMs"] = Convert.ToInt64(Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero)),
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

    private static JObject CreateGeneratorsPayload(MultiGeneratorGenerationResult result)
    {
        var generatorsPayload = new JObject();

        foreach (var generatorName in GetTrackedGeneratorNames(result))
        {
            var generatedSources = result.GetGeneratedSources(generatorName);
            var trackedSteps = result.TrackedSteps.TryGetValue(generatorName, out var generatorTrackedSteps)
                ? generatorTrackedSteps
                : ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>>.Empty;

            generatorsPayload.Add(
                generatorName,
                new JObject
                {
                    ["generatedOutputCount"] = generatedSources.Length,
                    ["generatedOutputLineCount"] = generatedSources.Sum(static generatedSource => CountLines(generatedSource.SourceText)),
                    ["trackedStages"] = CreateTrackedStagesPayload(trackedSteps),
                });
        }

        return generatorsPayload;
    }

    private static JObject CreateTrackedStagesPayload(
        ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> trackedSteps)
    {
        var trackedStagesPayload = new JObject();

        foreach (var stageName in trackedSteps.Keys.OrderBy(static stageName => stageName, StringComparer.Ordinal))
        {
            trackedStagesPayload.Add(stageName, trackedSteps[stageName].Length);
        }

        return trackedStagesPayload;
    }

    private static int CountLines(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return 0;
        }

        var lineCount = 1;
        foreach (var character in sourceText)
        {
            if (character == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    private static IReadOnlyList<string> GetTrackedGeneratorNames(MultiGeneratorGenerationResult result)
    {
        return result.GeneratedSources.Keys
            .Concat(result.TrackedSteps.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static generatorName => generatorName, StringComparer.Ordinal)
            .ToArray();
    }
}
