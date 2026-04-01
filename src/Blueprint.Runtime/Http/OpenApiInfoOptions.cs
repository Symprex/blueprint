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
}
