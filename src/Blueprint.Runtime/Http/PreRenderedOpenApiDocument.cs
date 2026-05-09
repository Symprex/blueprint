namespace Blueprint.Runtime.Http;

/// <summary>
/// Holds the pre-rendered OpenAPI 3.x JSON document that was assembled at endpoint registration time.
/// Registered in DI by <see cref="EndpointRouteBuilderExtensions.MapBlueprintApi{T, TExec}"/>
/// so that development utilities like <see cref="BlueprintOpenApiExportExtensions.OutputOpenApi"/>
/// can retrieve the document without re-rendering.
/// </summary>
public sealed class PreRenderedOpenApiDocument
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreRenderedOpenApiDocument"/> class.
    /// </summary>
    /// <param name="json">The pre-rendered OpenAPI 3.x JSON string.</param>
    public PreRenderedOpenApiDocument(string json)
    {
        this.Json = json;
    }

    /// <summary>
    /// Gets the pre-rendered OpenAPI 3.x JSON document string.
    /// </summary>
    public string Json { get; }
}

/// <summary>
/// A mutable singleton registered in DI before endpoint mapping so that
/// <see cref="EndpointRouteBuilderExtensions.MapBlueprintApi{T, TExec}"/> can store the
/// pre-rendered document for later retrieval by <see cref="BlueprintOpenApiExportExtensions.OutputOpenApi"/>.
/// </summary>
public sealed class PreRenderedOpenApiDocumentHolder
{
    private PreRenderedOpenApiDocument? _document;

    /// <summary>
    /// Gets the pre-rendered OpenAPI document, or <see langword="null"/> if the API has not been mapped yet.
    /// </summary>
    public PreRenderedOpenApiDocument? Document => this._document;

    /// <summary>
    /// Stores the pre-rendered document. Called once during endpoint mapping.
    /// </summary>
    /// <param name="document">The pre-rendered OpenAPI document to store.</param>
    internal void Set(PreRenderedOpenApiDocument document)
    {
        this._document = document;
    }
}
