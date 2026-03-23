using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Blueprint.SourceGenerator.Artifacts;

/// <summary>
/// Defines the shared machine-readable envelope that future Blueprint manifest producers should extend instead of
/// inventing their own unrelated top-level schemas.
/// </summary>
public sealed class GeneratedManifestEnvelope
{
    private readonly SortedDictionary<string, GeneratedManifestContributorSection> _sections = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, JToken> _operations = new(StringComparer.Ordinal);

    private GeneratedManifestEnvelope(
        string artifactName,
        string artifactFamily,
        string relativePath,
        GeneratedManifestAppIdentity app,
        GeneratedManifestGeneratorIdentity generator)
    {
        this.ArtifactName = artifactName;
        this.ArtifactFamily = artifactFamily;
        this.RelativePath = relativePath;
        this.App = app;
        this.Generator = generator;
    }

    /// <summary>
    /// Gets the current schema version for the shared manifest envelope.
    /// </summary>
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; } = 1;

    /// <summary>
    /// Gets the stable subject name used to derive the default manifest file name.
    /// </summary>
    [JsonProperty("artifactName")]
    public string ArtifactName { get; }

    /// <summary>
    /// Gets the artifact family, distinguishing build-time manifests from later runtime or review projections.
    /// </summary>
    [JsonProperty("artifactFamily")]
    public string ArtifactFamily { get; }

    /// <summary>
    /// Gets the default relative artifact path so generators and tooling project to the same layout by default.
    /// </summary>
    [JsonProperty("relativePath")]
    public string RelativePath { get; }

    /// <summary>
    /// Gets the app identity that this manifest describes.
    /// </summary>
    [JsonProperty("app")]
    public GeneratedManifestAppIdentity App { get; }

    /// <summary>
    /// Gets the generator identity that produced the manifest shape.
    /// </summary>
    [JsonProperty("generator")]
    public GeneratedManifestGeneratorIdentity Generator { get; }

    /// <summary>
    /// Gets the contributor-owned payload sections keyed by their stable shared-envelope section names.
    /// </summary>
    [JsonProperty("sections")]
    public IReadOnlyDictionary<string, GeneratedManifestContributorSection> Sections => this._sections;

    /// <summary>
    /// Gets the per-operation payload entries keyed by a stable operation identifier.
    /// </summary>
    [JsonProperty("operations")]
    public IReadOnlyDictionary<string, JToken> Operations => this._operations;

    /// <summary>
    /// Creates the first canonical build-time manifest envelope for the supplied manifest subject.
    /// </summary>
    /// <param name="artifactName">The stable manifest subject, such as <c>operations</c> or <c>pipeline</c>.</param>
    /// <param name="appTypeName">The fully-qualified app type name that owns the artifact.</param>
    /// <param name="assemblyName">The assembly name that produced the manifest.</param>
    /// <param name="generatorVersion">The generator version that produced the manifest shape.</param>
    /// <returns>A new envelope using the shared build-manifest layout conventions.</returns>
    public static GeneratedManifestEnvelope CreateBuildManifest(
        string artifactName,
        string appTypeName,
        string assemblyName,
        string generatorVersion)
    {
        EnsureValue(artifactName, nameof(artifactName));
        EnsureValue(appTypeName, nameof(appTypeName));
        EnsureValue(assemblyName, nameof(assemblyName));
        EnsureValue(generatorVersion, nameof(generatorVersion));

        return new GeneratedManifestEnvelope(
            artifactName,
            "build-time-manifest",
            GeneratedManifestLayout.GetDefaultRelativePath(artifactName),
            new GeneratedManifestAppIdentity(appTypeName, assemblyName),
            new GeneratedManifestGeneratorIdentity("Blueprint.SourceGenerator", generatorVersion));
    }

    /// <summary>
    /// Adds or replaces a contributor-owned section, rejecting ownership collisions so two modules cannot silently claim
    /// the same section name.
    /// </summary>
    /// <param name="sectionName">The stable section name inside the shared envelope.</param>
    /// <param name="contributorId">The contributor that owns the payload in that section.</param>
    /// <param name="payload">The section payload to expose in the manifest.</param>
    public void AddSection(string sectionName, string contributorId, JToken payload)
    {
        EnsureValue(sectionName, nameof(sectionName));
        EnsureValue(contributorId, nameof(contributorId));
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (this._sections.TryGetValue(sectionName, out var existingSection) &&
            !StringComparer.Ordinal.Equals(existingSection.ContributorId, contributorId))
        {
            throw new InvalidOperationException(
                $"Cannot reassign manifest section '{sectionName}' from contributor '{existingSection.ContributorId}' to contributor '{contributorId}'.");
        }

        this._sections[sectionName] = new GeneratedManifestContributorSection(contributorId, payload.DeepClone());
    }

    /// <summary>
    /// Adds a per-operation payload entry using the agreed stable operation key.
    /// </summary>
    /// <param name="operationKey">The stable operation key, expected to be a fully-qualified operation identifier.</param>
    /// <param name="payload">The payload to store for that operation.</param>
    public void AddOperation(string operationKey, JToken payload)
    {
        EnsureValue(operationKey, nameof(operationKey));
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (this._operations.ContainsKey(operationKey))
        {
            throw new InvalidOperationException(
                $"Cannot add manifest operation '{operationKey}' more than once because operation entries must remain uniquely keyed.");
        }

        this._operations.Add(operationKey, payload.DeepClone());
    }

    /// <summary>
    /// Serializes the envelope to deterministic indented JSON for snapshot review and artifact emission.
    /// </summary>
    /// <returns>The serialized manifest JSON.</returns>
    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    private static void EnsureValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }
}

/// <summary>
/// Provides the shared default artifact layout conventions for generated manifest files.
/// </summary>
public static class GeneratedManifestLayout
{
    /// <summary>
    /// Gets the top-level folder that owns generated inspection artifacts by default.
    /// </summary>
    public const string RootDirectory = ".blueprint";

    /// <summary>
    /// Gets the subfolder reserved for canonical machine-readable manifest files.
    /// </summary>
    public const string ManifestDirectory = "manifests";

    /// <summary>
    /// Builds the default relative manifest path for the supplied stable artifact subject.
    /// </summary>
    /// <param name="artifactName">The manifest subject to name.</param>
    /// <returns>The shared relative path used for canonical build-time manifest emission.</returns>
    public static string GetDefaultRelativePath(string artifactName)
    {
        if (string.IsNullOrWhiteSpace(artifactName))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(artifactName));
        }

        return $"{RootDirectory}/{ManifestDirectory}/{artifactName}.manifest.json";
    }
}

/// <summary>
/// Identifies the app and assembly that own a generated manifest artifact.
/// </summary>
/// <param name="TypeName">The fully-qualified app type name.</param>
/// <param name="AssemblyName">The assembly that owns the generated artifact.</param>
public sealed record GeneratedManifestAppIdentity(
    [property: JsonProperty("typeName")] string TypeName,
    [property: JsonProperty("assemblyName")] string AssemblyName);

/// <summary>
/// Identifies the generator product and version that emitted the manifest contract.
/// </summary>
/// <param name="ProductName">The generator product name.</param>
/// <param name="Version">The generator version.</param>
public sealed record GeneratedManifestGeneratorIdentity(
    [property: JsonProperty("productName")] string ProductName,
    [property: JsonProperty("version")] string Version);

/// <summary>
/// Stores one contributor-owned payload inside the shared manifest envelope.
/// </summary>
/// <param name="ContributorId">The contributor that owns the payload section.</param>
/// <param name="Payload">The contributor payload for that section.</param>
public sealed record GeneratedManifestContributorSection(
    [property: JsonProperty("contributorId")] string ContributorId,
    [property: JsonProperty("payload")] JToken Payload);
