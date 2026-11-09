namespace Blueprint.Runtime.Http;

/// <summary>
/// Where an app's API is mapped and how its OpenAPI document is described, as <c>AddBlueprintApi</c> declared them. Both
/// <c>MapBlueprintApi</c> and the <c>BlueprintEmitOpenApi</c> build target, which stops the app before
/// <c>MapBlueprintApi</c> is reached, render the document from these, so the document written at build time is the one
/// the app serves.
/// </summary>
/// <remarks>
/// Registered as a keyed service, keyed by the app's type, so that each app registered with the service provider has its
/// own.
/// </remarks>
internal sealed class BlueprintApiMapping
{
    /// <summary>
    /// Gets or sets the base path the API is mapped under, or <see langword="null" /> when none was declared.
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Gets or sets the delegate that customises the document's <c>info</c> block, if any.
    /// </summary>
    public Action<OpenApiInfoOptions>? ConfigureOpenApi { get; set; }

    /// <summary>
    /// Gets or sets the delegate that registers schema processors, if any.
    /// </summary>
    public Action<OpenApiSchemaProcessorOptions>? ConfigureOpenApiSchemas { get; set; }

    /// <summary>
    /// Gets or sets the delegate that registers operation processors, if any.
    /// </summary>
    public Action<OpenApiOperationProcessorOptions>? ConfigureOpenApiOperations { get; set; }

    /// <summary>
    /// Gets a value indicating whether anything was declared, in which case <c>MapBlueprintApi</c> may not be given its own
    /// arguments as well.
    /// </summary>
    public bool IsDeclared => this.BasePath is not null
        || this.ConfigureOpenApi is not null
        || this.ConfigureOpenApiSchemas is not null
        || this.ConfigureOpenApiOperations is not null;

    /// <summary>
    /// Ends <paramref name="basePath" /> with one slash, as every route and the OpenAPI document's <c>servers</c> entry
    /// expect it.
    /// </summary>
    /// <param name="basePath">The base path, as an app gave it.</param>
    /// <returns>The base path ending with one slash.</returns>
    public static string NormaliseBasePath(string basePath)
    {
        // Ensure ends with a slash, but only one, as legacy did (Signature365 8b40ecc961,
        // Blueprint.Http/ApplicationBuilderExtensions.cs:45). Every route is this followed by a URL without its leading slash
        // (RouteUnder), as legacy appended its leading-slash-trimmed link URLs (:105; Blueprint/ApiOperationLink.cs:48), and
        // every absolute path is that with a leading slash: "", "/" -> "/"; "api", "api/", "/api/" -> "/api/".
        return basePath.TrimEnd('/') + '/';
    }
}
