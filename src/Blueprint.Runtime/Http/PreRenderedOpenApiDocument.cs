using System.Text;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Holds the pre-rendered OpenAPI 3.x JSON document that was assembled at endpoint registration time.
/// Registered in DI by <see cref="EndpointRouteBuilderExtensions.MapBlueprintApi{T, TExec}"/>
/// so that development utilities like <see cref="BlueprintOpenApiExportExtensions.OutputOpenApi"/>
/// can retrieve the document without re-rendering.
/// </summary>
public sealed class PreRenderedOpenApiDocument
{
    private readonly ReadOnlyMemory<byte> _utf8Json;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRenderedOpenApiDocument"/> class.
    /// </summary>
    /// <param name="json">The pre-rendered OpenAPI 3.x JSON string.</param>
    public PreRenderedOpenApiDocument(string json)
        : this(Encoding.UTF8.GetBytes(json ?? throw new ArgumentNullException(nameof(json))))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PreRenderedOpenApiDocument"/> class from the document as UTF-8.
    /// </summary>
    /// <param name="utf8Json">The pre-rendered OpenAPI 3.x JSON, UTF-8 encoded without a byte order mark. It is not copied.</param>
    public PreRenderedOpenApiDocument(ReadOnlyMemory<byte> utf8Json)
    {
        this._utf8Json = utf8Json;
    }

    /// <summary>
    /// Gets the pre-rendered OpenAPI 3.x JSON document, UTF-8 encoded: how it is held and served.
    /// </summary>
    public ReadOnlyMemory<byte> Utf8Json => this._utf8Json;

    /// <summary>
    /// Gets the pre-rendered OpenAPI 3.x JSON document string, decoded from <see cref="Utf8Json"/> on each read: the
    /// document is held as UTF-8, and a string of it would double what a service keeps for its life.
    /// </summary>
    public string Json => Encoding.UTF8.GetString(this._utf8Json.Span);

    /// <summary>
    /// Gets what the document could not describe as it would have, each named for the consumer to act on: for example,
    /// a request-body property whose type the app's serializer context does not describe.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// A mutable singleton registered in DI before endpoint mapping so that
/// <see cref="EndpointRouteBuilderExtensions.MapBlueprintApi{T, TExec}"/> can store the
/// pre-rendered document for later retrieval by <see cref="BlueprintOpenApiExportExtensions.OutputOpenApi"/>.
/// </summary>
public sealed class PreRenderedOpenApiDocumentHolder
{
    private Lazy<PreRenderedOpenApiDocument>? _document;

    /// <summary>
    /// Gets the pre-rendered OpenAPI document, or <see langword="null"/> if the API has not been mapped yet. When
    /// <see cref="BlueprintHttpOptions.RenderOpenApiDocumentOnFirstRequest" /> is set and nothing has requested the
    /// document yet, reading this renders it.
    /// </summary>
    public PreRenderedOpenApiDocument? Document => this._document?.Value;

    /// <summary>
    /// Stores the document, rendered or to be rendered on first read. Called once during endpoint mapping.
    /// </summary>
    /// <param name="document">The OpenAPI document to store.</param>
    internal void Set(Lazy<PreRenderedOpenApiDocument> document)
    {
        this._document = document;
    }
}
