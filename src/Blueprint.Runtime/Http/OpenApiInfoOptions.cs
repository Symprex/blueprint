namespace Blueprint.Runtime.Http;

/// <summary>
/// Customisation options for the OpenAPI <c>info</c> object emitted by the Blueprint HTTP host.
/// Pass an <see cref="System.Action{T}"/> delegate to <c>MapBlueprintApi</c> via the
/// <c>configureOpenApi</c> parameter to override the defaults.
/// </summary>
public sealed class OpenApiInfoOptions
{
    /// <summary>Gets or sets the human-readable title for the API. Defaults to <c>"Blueprint API"</c>.</summary>
    public string Title { get; set; } = "Blueprint API";

    /// <summary>Gets or sets the version string for the API. Defaults to <c>"1.0.0"</c>.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the OpenAPI specification version to declare in the rendered document.
    /// Defaults to <c>"3.0.4"</c>. Set to <c>"3.0.0"</c> for maximum tooling compatibility
    /// (e.g. older code generators or TypeScript client generators that reject newer minor versions).
    /// </summary>
    public string OpenApiVersion { get; set; } = "3.0.4";

    /// <summary>
    /// Gets or sets an optional description for the API.
    /// When <see langword="null"/> or empty the <c>description</c> property is omitted from the rendered output.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets an optional terms-of-service URL for the API.
    /// When <see langword="null"/> or empty the <c>termsOfService</c> property is omitted from the rendered output.
    /// </summary>
    public string? TermsOfService { get; set; }

    /// <summary>
    /// Gets or sets optional contact information for the API.
    /// When <see langword="null"/> the <c>contact</c> object is omitted from the rendered output.
    /// </summary>
    public OpenApiContactInfo? Contact { get; set; }

    /// <summary>
    /// Gets or sets optional license information for the API.
    /// When <see langword="null"/> the <c>license</c> object is omitted from the rendered output.
    /// </summary>
    public OpenApiLicenseInfo? License { get; set; }

    /// <summary>
    /// Gets the list of server objects to include in the OpenAPI <c>servers</c> array.
    /// When empty, the <c>servers</c> section is omitted. Servers are rendered in list order.
    /// </summary>
    public List<OpenApiServerEntry> Servers { get; } = [];

    /// <summary>
    /// Gets the list of security scheme definitions to include in the OpenAPI
    /// <c>components.securitySchemes</c> object. When empty, the default <c>bearerAuth</c>
    /// JWT scheme is emitted for any operation that requires authentication.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Add entries to override the default Bearer JWT scheme with OAuth2, API Key, or
    /// custom security definitions. Each scheme is referenced by its <see cref="OpenApiSecurityScheme.Name"/>
    /// in per-operation and document-level <c>security</c> blocks.
    /// </para>
    /// </remarks>
    public List<OpenApiSecurityScheme> SecuritySchemes { get; } = [];

    /// <summary>
    /// When <see langword="true"/>, the renderer adds a synthetic <c>200</c> response that mirrors
    /// the schema of the first body-bearing <c>2xx</c> success response (e.g. <c>201 Created</c>
    /// or <c>202 Accepted</c>) for every operation that does not already declare a <c>200</c> body.
    /// The original semantic status codes are preserved alongside the synthetic alias.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for backward compatibility with TypeScript code generators (such as the
    /// Signature 365 Handlebars-based generator) that hardcode <c>responses['200']</c> when
    /// determining the return type of an operation. Without this flag those generators would
    /// treat <c>ResourceEventResult&lt;T&gt;</c> (201) as <c>void</c>.
    /// </para>
    /// <para>
    /// Bodyless success responses (<c>204 No Content</c>) are not aliased because they carry
    /// no schema and would not change the TypeScript return type regardless.
    /// </para>
    /// </remarks>
    public bool UseStatus200ForSuccessResponses { get; set; }

    /// <summary>
    /// When <see langword="true"/>, error responses (<c>422 Unprocessable Entity</c>, <c>409 Conflict</c>, etc.)
    /// use <c>application/json</c> as their media type instead of <c>application/problem+json</c>.
    /// Defaults to <see langword="false"/> (RFC 9457 <c>application/problem+json</c> is used).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for backward compatibility with legacy OpenAPI consumers (such as the
    /// Signature 365 Handlebars-based TypeScript generator) that expect all response bodies
    /// to use <c>application/json</c>, even for ProblemDetails / ValidationProblemDetails payloads.
    /// </para>
    /// </remarks>
    public bool UsePlainJsonForErrorResponses { get; set; }

    /// <summary>
    /// Gets or sets an optional delegate that transforms CLR type names into OpenAPI schema
    /// component keys. When <see langword="null"/> the default strategy is used, which extracts
    /// the simple class name (e.g. <c>MyApp.Api.TemplateResource</c> → <c>TemplateResource</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this to produce shorter or project-specific schema names. For example, to strip
    /// a common namespace prefix:
    /// </para>
    /// <code>
    /// options.SchemaNameFormatter = clrName =>
    ///     clrName.Replace("Symprex.Signature365.", "").Replace('.', '_');
    /// </code>
    /// <para>
    /// The formatter receives the full CLR type name (e.g. <c>MyNamespace.MyType</c>) and must
    /// return a string safe for use as a JSON property key. Names must be unique across all
    /// schema components in the document.
    /// </para>
    /// </remarks>
    public Func<string, string>? SchemaNameFormatter { get; set; }
}

/// <summary>
/// Contact information for the exposed API, rendered in the OpenAPI <c>info.contact</c> object.
/// At least one property should be non-null for the contact block to appear in the output.
/// </summary>
public sealed class OpenApiContactInfo
{
    /// <summary>Gets or sets the identifying name of the contact person or organisation.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the URL pointing to the contact information (e.g. a support page).</summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets the email address of the contact person or organisation.</summary>
    public string? Email { get; set; }

    /// <summary>Returns <see langword="true"/> when at least one contact field is populated.</summary>
    internal bool HasValue => !string.IsNullOrEmpty(this.Name) || !string.IsNullOrEmpty(this.Url) || !string.IsNullOrEmpty(this.Email);
}

/// <summary>
/// License information for the exposed API, rendered in the OpenAPI <c>info.license</c> object.
/// </summary>
public sealed class OpenApiLicenseInfo
{
    /// <summary>Gets or sets the license name used for the API (required by the OpenAPI spec when the license object is present).</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets an optional URL to the full license text.</summary>
    public string? Url { get; set; }
}

/// <summary>
/// Represents a single entry in the OpenAPI <c>servers</c> array, providing a base URL
/// and optional description for client code generation and API exploration.
/// </summary>
public sealed class OpenApiServerEntry
{
    /// <summary>Gets or sets the server URL (e.g. <c>"https://api.example.com/v1"</c>).</summary>
    public required string Url { get; set; }

    /// <summary>Gets or sets an optional human-readable description for this server.</summary>
    public string? Description { get; set; }
}
