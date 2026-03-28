using Blueprint.SourceGenerator.Modules;
using Blueprint.SourceGenerator.Planning;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Artifacts;

/// <summary>
/// Projects operation-discovery manifest planning data into the approved shared manifest envelope.
/// </summary>
public static class OperationsDiscoveryManifestEmitter
{
    private const string ArtifactName = "operations";
    private const string SectionName = "operations-discovery";

    /// <summary>
    /// Creates the canonical build-time operations manifest for one Blueprint app.
    /// </summary>
    /// <param name="appTypeName">The fully-qualified app type name that owns the manifest.</param>
    /// <param name="assemblyName">The assembly that produced the manifest.</param>
    /// <param name="generatorVersion">The generator version that produced the manifest shape.</param>
    /// <param name="manifests">The per-operation manifest planning results to project.</param>
    /// <returns>The shared manifest envelope containing operation discovery summaries.</returns>
    public static GeneratedManifestEnvelope CreateBuildManifest(
        string appTypeName,
        string assemblyName,
        string generatorVersion,
        IEnumerable<GeneratedDebugManifest> manifests)
    {
        if (manifests is null)
        {
            throw new ArgumentNullException(nameof(manifests));
        }

        var manifestList = manifests.ToArray();
        var envelope = GeneratedManifestEnvelope.CreateBuildManifest(ArtifactName, appTypeName, assemblyName, generatorVersion);

        envelope.AddSection(
            SectionName,
            nameof(OperationDiscoveryManifestModule),
            new JObject
            {
                ["operationCount"] = manifestList.Length,
            });

        foreach (var manifest in manifestList.OrderBy(static entry => entry.Operation.FullTypeName, StringComparer.Ordinal))
        {
            envelope.AddOperation(manifest.Operation.FullTypeName, CreateOperationPayload(manifest));
        }

        return envelope;
    }

    private static JObject CreateOperationPayload(GeneratedDebugManifest manifest)
    {
        return new JObject(
            manifest.Entries
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => new JProperty(entry.Key, entry.Value.DeepClone())));
    }
}
