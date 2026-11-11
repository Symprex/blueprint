using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient;

/// <summary>
/// Creates the <see cref="IUpstreamCredential"/> a gateway operation's forward authenticates with, selected for the
/// operation by <see cref="UpstreamCredentialAttribute{TCredential}"/> and resolved from the request's services.
/// </summary>
/// <remarks>
/// The strategy sees the operation being served and the caller; the credential it creates sees only the request, so the
/// same credential types serve typed clients used outside a pipeline. Work that must await, such as exchanging the
/// caller's token for another, belongs in the credential's <see cref="IUpstreamCredential.ApplyAsync"/>.
/// </remarks>
public interface IUpstreamCredentialStrategy
{
    /// <summary>
    /// Creates the credential for one forward.
    /// </summary>
    /// <param name="context">The gateway operation being served; <see cref="ApiOperationContext.Operation"/> is the operation.</param>
    /// <param name="attribute">
    /// The attribute that selected this strategy, which a strategy that takes arguments declares as its own type deriving
    /// from <see cref="UpstreamCredentialAttribute{TCredential}"/>.
    /// </param>
    /// <returns>The credential to apply to the forwarded request.</returns>
    IUpstreamCredential CreateCredential(ApiOperationContext context, UpstreamCredentialAttribute attribute);
}
