namespace Blueprint.Authorization;

/// <summary>
/// Why an <see cref="IResourceAuthorizationProvider"/> is being asked, which decides what it may resolve and how strictly
/// it must match.
/// </summary>
/// <remarks>
/// Only <see cref="Execute"/> is asked today. <see cref="Link"/> and <see cref="Listing"/> are part of the contract so that
/// a provider written now already has to say what it does with them; Blueprint starts asking in those modes once link
/// visibility (Symprex/blueprint#79) and authorisation manifests (Symprex/blueprint#81) consult providers.
/// </remarks>
public enum ResourceAuthorizationMode
{
    /// <summary>
    /// The operation is about to execute. Resource keys are resolved against the operation, and the decision is final.
    /// </summary>
    Execute,

    /// <summary>
    /// A link to the operation may be written on <see cref="ResourceAuthorizationRequest.Resource"/>. Resource keys are
    /// resolved against that resource, not the operation, and a denial hides the link.
    /// </summary>
    Link,

    /// <summary>
    /// The caller is listing what it may do before any resource exists, as an authorisation manifest does. Property tokens
    /// cannot be resolved, so a provider may only rule an operation out; never use this mode to permit execution.
    /// </summary>
    Listing,
}
