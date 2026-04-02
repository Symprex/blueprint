namespace Blueprint.Runtime.Http;

/// <summary>
/// Collects ordered schema processors that can customise rendered OpenAPI schema components before the final JSON
/// payload is emitted by the HTTP host.
/// </summary>
public sealed class OpenApiSchemaProcessorOptions
{
    /// <summary>
    /// Gets the ordered schema processors to apply. Processors run once at startup when the OpenAPI document is
    /// pre-rendered, and later processors observe the output of earlier ones.
    /// </summary>
    public IList<Action<OpenApiSchemaProcessorContext>> Processors { get; } = [];
}

/// <summary>
/// Carries the current schema component through an OpenAPI schema-processor callback.
/// </summary>
public sealed class OpenApiSchemaProcessorContext
{
    /// <summary>
    /// Initialises a new processor context for the supplied schema component.
    /// </summary>
    /// <param name="component">The current schema component being customised.</param>
    public OpenApiSchemaProcessorContext(OpenApiSchemaComponentModel component)
    {
        this.Component = component;
    }

    /// <summary>
    /// Gets or sets the current schema component being customised. Processors may replace it with a derived copy so later
    /// processors observe the accumulated output.
    /// </summary>
    public OpenApiSchemaComponentModel Component { get; set; }
}
