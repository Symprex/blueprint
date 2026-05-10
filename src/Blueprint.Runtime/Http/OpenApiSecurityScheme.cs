namespace Blueprint.Runtime.Http;

/// <summary>
/// Represents a security scheme definition for the OpenAPI <c>components.securitySchemes</c> object.
/// Maps to the OpenAPI 3.x Security Scheme Object specification.
/// </summary>
public sealed class OpenApiSecurityScheme
{
    /// <summary>
    /// Gets or sets the reference name for the scheme, used as the key in <c>securitySchemes</c>
    /// and referenced in per-operation <c>security</c> blocks.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the type of the security scheme. One of <c>"http"</c>, <c>"apiKey"</c>,
    /// <c>"oauth2"</c>, or <c>"openIdConnect"</c>.
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Gets or sets the HTTP auth scheme name. Required when <see cref="Type"/> is <c>"http"</c>
    /// (e.g. <c>"bearer"</c>, <c>"basic"</c>).
    /// </summary>
    public string? Scheme { get; set; }

    /// <summary>
    /// Gets or sets the bearer token format hint (e.g. <c>"JWT"</c>).
    /// Only used when <see cref="Type"/> is <c>"http"</c> and <see cref="Scheme"/> is <c>"bearer"</c>.
    /// </summary>
    public string? BearerFormat { get; set; }

    /// <summary>
    /// Gets or sets an optional description for the scheme.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the header, query, or cookie name. Required when <see cref="Type"/> is <c>"apiKey"</c>.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Gets or sets the location of the API key. One of <c>"query"</c>, <c>"header"</c>, or <c>"cookie"</c>.
    /// Required when <see cref="Type"/> is <c>"apiKey"</c>.
    /// </summary>
    public string? In { get; set; }

    /// <summary>
    /// Gets or sets the OpenID Connect discovery URL. Required when <see cref="Type"/> is <c>"openIdConnect"</c>.
    /// </summary>
    public string? OpenIdConnectUrl { get; set; }

    /// <summary>
    /// Gets the OAuth2 flows for this scheme. Only used when <see cref="Type"/> is <c>"oauth2"</c>.
    /// </summary>
    public OpenApiOAuth2Flows? Flows { get; set; }

    /// <summary>
    /// Creates the default Bearer JWT security scheme that Blueprint uses when no custom schemes are configured.
    /// </summary>
    /// <returns>A <c>bearerAuth</c> scheme with JWT bearer format.</returns>
    public static OpenApiSecurityScheme BearerJwt()
    {
        return new OpenApiSecurityScheme
        {
            Name = "bearerAuth",
            Type = "http",
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
    }
}

/// <summary>
/// OAuth2 flow definitions for an OpenAPI security scheme.
/// </summary>
public sealed class OpenApiOAuth2Flows
{
    /// <summary>Gets or sets the authorization code flow configuration.</summary>
    public OpenApiOAuth2Flow? AuthorizationCode { get; set; }

    /// <summary>Gets or sets the client credentials flow configuration.</summary>
    public OpenApiOAuth2Flow? ClientCredentials { get; set; }

    /// <summary>Gets or sets the implicit flow configuration.</summary>
    public OpenApiOAuth2Flow? Implicit { get; set; }
}

/// <summary>
/// A single OAuth2 flow definition.
/// </summary>
public sealed class OpenApiOAuth2Flow
{
    /// <summary>Gets or sets the authorization URL for the flow.</summary>
    public string? AuthorizationUrl { get; set; }

    /// <summary>Gets or sets the token URL for the flow.</summary>
    public string? TokenUrl { get; set; }

    /// <summary>Gets or sets the refresh URL for the flow.</summary>
    public string? RefreshUrl { get; set; }

    /// <summary>
    /// Gets the available scopes for the OAuth2 flow.
    /// Key is the scope name, value is the human-readable description.
    /// </summary>
    public Dictionary<string, string> Scopes { get; } = new();
}
