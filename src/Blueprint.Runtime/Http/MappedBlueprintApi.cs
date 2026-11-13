namespace Blueprint.Runtime.Http;

/// <summary>
/// One call of <c>MapBlueprintApi</c>, as the metadata of every operation endpoint it maps. An endpoint is told by this
/// instance from the one another call maps for the same operation, as when one app is mapped under two base paths.
/// </summary>
internal sealed class MappedBlueprintApi
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MappedBlueprintApi"/> class.
    /// </summary>
    /// <param name="appType">The app whose API it maps.</param>
    /// <param name="basePath">The base path the API is mapped under, with a leading slash.</param>
    public MappedBlueprintApi(Type appType, string basePath)
    {
        this.AppType = appType;
        this.BasePath = basePath;
    }

    /// <summary>Gets the app whose API it maps.</summary>
    public Type AppType { get; }

    /// <summary>Gets the base path the API is mapped under, with a leading slash.</summary>
    public string BasePath { get; }
}
