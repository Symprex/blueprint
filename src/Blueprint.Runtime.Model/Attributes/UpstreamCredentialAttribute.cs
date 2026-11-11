namespace Blueprint.Model.Attributes;

/// <summary>
/// Selects the upstream credential strategy a gateway operation's forwards authenticate with, in place of the default,
/// which forwards the caller's own <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// <para>
/// Apply <see cref="UpstreamCredentialAttribute{TCredential}"/>, or an attribute derived from it. A strategy that takes
/// arguments, such as which property of the operation holds a tenant id, declares its own attribute deriving from
/// <see cref="UpstreamCredentialAttribute{TCredential}"/> with those arguments as properties, and is given that attribute
/// when it creates the credential for a forward.
/// </para>
/// <para>
/// An operation carries at most one; a forward from an operation that carries more fails.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class UpstreamCredentialAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="UpstreamCredentialAttribute"/> class.
    /// </summary>
    private protected UpstreamCredentialAttribute()
    {
    }

    /// <summary>
    /// Gets the strategy's type, resolved from the request's services for each forward.
    /// </summary>
    public abstract Type CredentialType { get; }
}

/// <summary>
/// Selects <typeparamref name="TCredential"/>, resolved from the request's services, as the strategy that creates the
/// credential each of this gateway operation's forwards authenticates with, in place of the caller's own
/// <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// Not sealed, so a strategy that takes arguments can declare an attribute of its own, deriving from this one, that carries
/// them as properties.
/// </remarks>
/// <typeparam name="TCredential">
/// The strategy: an <c>IUpstreamCredentialStrategy</c> from <c>Blueprint.HttpClient</c>, registered in the gateway's services.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
#pragma warning disable CA1813 // Unsealed so a strategy can declare an argument-bearing attribute of its own.
public class UpstreamCredentialAttribute<TCredential> : UpstreamCredentialAttribute
#pragma warning restore CA1813
{
    /// <inheritdoc />
    public override Type CredentialType => typeof(TCredential);
}
