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
    /// Gets or sets an optional description for the API.
    /// When <see langword="null"/> or empty the <c>description</c> property is omitted from the rendered output.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets the list of server objects to include in the OpenAPI <c>servers</c> array.
    /// When empty, the <c>servers</c> section is omitted. Servers are rendered in list order.
    /// </summary>
    public List<OpenApiServerEntry> Servers { get; } = [];
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
