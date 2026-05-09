using Blueprint.Model;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Collects ordered operation processors that can customise rendered OpenAPI operation objects before the final
/// document is assembled. Processors run once at startup when the OpenAPI document is pre-rendered. This is the
/// equivalent of legacy Blueprint's <c>OpenApiOptions.ConfigureOperation</c> hook.
/// </summary>
/// <remarks>
/// Common uses: removing auto-populated parameters (e.g. TenantId) from the public OpenAPI spec, adding
/// vendor extensions, overriding summaries or descriptions.
/// </remarks>
public sealed class OpenApiOperationProcessorOptions
{
    /// <summary>
    /// Gets the ordered operation processors to apply. Each processor receives the rendered operation model
    /// alongside the source <see cref="ApiOperationDescriptor"/> and returns a (possibly modified) operation model.
    /// Processors run in registration order; later processors observe the output of earlier ones.
    /// </summary>
    public IList<Func<OpenApiOperationModel, ApiOperationDescriptor, OpenApiOperationModel>> Processors { get; } = [];
}
