using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Gateway;

/// <summary>
/// An <see cref="IUpstreamCredentialStrategy"/> that authenticates forwards as the gateway's own service identity, with the
/// bearer token configured under <see cref="Startup.ServiceTokenKey"/>, whoever the caller is.
/// </summary>
/// <param name="configuration">Supplies <see cref="Startup.ServiceTokenKey"/>.</param>
public sealed class ServiceTokenCredentialStrategy(IConfiguration configuration) : IUpstreamCredentialStrategy
{
    /// <inheritdoc />
    public IUpstreamCredential CreateCredential(ApiOperationContext context, IOperation inner, UpstreamCredentialAttribute attribute)
    {
        var token = configuration[Startup.ServiceTokenKey]
            ?? throw new InvalidOperationException($"No service token is configured under '{Startup.ServiceTokenKey}'.");

        return new BearerTokenCredential(token);
    }
}
