using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Context passed to debug-manifest contributors.
/// </summary>
public sealed class DebugManifestContributorContext
{
    private readonly string _contributorId;
    private readonly OperationModel _operation;
    private readonly GeneratedDebugManifest _manifest;
    private readonly IOperationLookup _operationLookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="DebugManifestContributorContext"/> class.
    /// </summary>
    /// <param name="contributorId">The contributor currently populating the manifest.</param>
    /// <param name="operation">The current operation.</param>
    /// <param name="manifest">The manifest plan under construction.</param>
    /// <param name="operationLookup">A lookup for related operations.</param>
    public DebugManifestContributorContext(
        string contributorId,
        OperationModel operation,
        GeneratedDebugManifest manifest,
        IOperationLookup operationLookup)
    {
        this._contributorId = contributorId;
        this._operation = operation;
        this._manifest = manifest;
        this._operationLookup = operationLookup;
    }

    /// <summary>
    /// Gets the current operation metadata.
    /// </summary>
    public OperationModel Operation => this._operation;

    /// <summary>
    /// Gets the Roslyn symbol for the current operation.
    /// </summary>
    /// <returns>The operation type symbol.</returns>
    public INamedTypeSymbol GetOperationTypeSymbol()
    {
        return this._operation.TypeSymbol;
    }

    /// <summary>
    /// Adds or replaces one manifest entry for the current operation.
    /// </summary>
    /// <param name="key">The manifest entry key.</param>
    /// <param name="value">The manifest entry value.</param>
    public void AddEntry(string key, string value)
    {
        this._manifest.AddEntry(key, value, this._contributorId);
    }

    /// <summary>
    /// Finds a related operation by its metadata name.
    /// </summary>
    /// <param name="metadataName">The metadata name to resolve.</param>
    /// <returns>The matching operation model, if found.</returns>
    public OperationModel? FindOperationByMetadataName(string metadataName)
    {
        return this._operationLookup.FindByMetadataName(metadataName);
    }
}
