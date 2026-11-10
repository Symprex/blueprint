namespace Blueprint.Runtime.Http;

/// <summary>
/// The <see cref="IOperationInputSchemaProvider"/> <c>AddBlueprintApi</c> registers, which <c>MapBlueprintApi</c> gives
/// the app's operations, made from the same rendering as its OpenAPI document.
/// </summary>
internal sealed class OperationInputSchemaProvider : IOperationInputSchemaProvider
{
    private Lazy<OperationInputSchemaSet>? _schemas;

    /// <inheritdoc />
    public IReadOnlyList<OperationInputSchema> GetSchemas()
    {
        return this.Schemas.GetSchemas();
    }

    /// <inheritdoc />
    public OperationInputSchema? GetSchema(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        return this.Schemas.GetSchema(operationName);
    }

    /// <summary>
    /// Gives the provider the app's schemas, made when they are first asked for. Called once during endpoint mapping.
    /// </summary>
    /// <param name="schemas">The schemas, made from the rendering of the app's OpenAPI document.</param>
    internal void Set(Lazy<OperationInputSchemaSet> schemas)
    {
        this._schemas = schemas;
    }

    /// <summary>
    /// Gives a provider of the schemas this one holds now, those of the app <c>MapBlueprintApi</c> mapped last, which
    /// keeps them when another app's API is mapped after it.
    /// </summary>
    /// <returns>The provider.</returns>
    internal OperationInputSchemaProvider Snapshot()
    {
        return new OperationInputSchemaProvider { _schemas = this._schemas };
    }

    private OperationInputSchemaSet Schemas => this._schemas?.Value
        ?? throw new InvalidOperationException(
            "The operation input schemas are not known until the API is mapped. Call MapBlueprintApi before asking "
            + $"{nameof(IOperationInputSchemaProvider)} for them.");
}
