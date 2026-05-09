using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Returns the list of operations the current user is authorized to access. This is the
/// standard "auth manifest" pattern used by SPAs to determine which UI features to show.
/// </summary>
[ApiOperations("Authorization")]
[SelfLink(typeof(AuthManifestApiResource), "/me/authManifest")]
[HttpGet]
[MustBeAuthenticated]
[Description("Returns the authorization manifest listing accessible operations for the current user.")]
public partial class GetMyAuthManifestQuery : IQuery<AuthManifestApiResource>
{
}

/// <summary>
/// Resource representing the authorization manifest — which operations the current user can access.
/// </summary>
public partial class AuthManifestApiResource : ApiResource
{
    /// <summary>
    /// Gets the list of operation names that the current user is authorized to access.
    /// </summary>
    public List<string> AccessibleOperations { get; } = [];
}
