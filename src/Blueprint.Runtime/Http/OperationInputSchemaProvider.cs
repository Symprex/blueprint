namespace Blueprint.Runtime.Http;

/// <summary>
/// The <see cref="IOperationInputSchemaProvider"/> <c>AddBlueprintApi</c> registers, which <c>MapBlueprintApi</c> gives
/// the app's operations, made from the same rendering as its OpenAPI document.
/// </summary>
internal sealed class OperationInputSchemaProvider : IOperationInputSchemaProvider
{
    private Lazy<OperationInputSchemaSet>? _schemas;

    /// <summary>
    /// Gets the <c>MapBlueprintApi</c> call whose operations the schemas describe, or <see langword="null"/> before one.
    /// </summary>
    internal MappedBlueprintApi? MappedApi { get; private set; }

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
    /// <param name="mappedApi">The call mapping the operations the schemas describe.</param>
    internal void Set(Lazy<OperationInputSchemaSet> schemas, MappedBlueprintApi mappedApi)
    {
        this._schemas = schemas;
        this.MappedApi = mappedApi;
    }

    /// <summary>
    /// Gives a provider of the schemas this one holds now, those of the API <c>MapBlueprintApi</c> mapped last, which
    /// keeps them when another API is mapped after it.
    /// </summary>
    /// <returns>The provider.</returns>
    internal OperationInputSchemaProvider Snapshot()
    {
        return new OperationInputSchemaProvider { _schemas = this._schemas, MappedApi = this.MappedApi };
    }

    private OperationInputSchemaSet Schemas => this._schemas?.Value
        ?? throw new InvalidOperationException(
            "The operation input schemas are not known until the API is mapped. Call MapBlueprintApi before asking "
            + $"{nameof(IOperationInputSchemaProvider)} for them.");
}
