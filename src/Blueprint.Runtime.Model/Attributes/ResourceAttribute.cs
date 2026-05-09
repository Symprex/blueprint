namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a resource permission scope requirement on an operation. The noun identifies
/// the resource type and the verb identifies the action being performed. Multiple
/// <c>[Resource]</c> attributes use any-of semantics: the caller must satisfy at least one.
/// </summary>
/// <remarks>
/// <para>
/// Operations decorated with <c>[Resource]</c> implicitly require authentication (permissioned
/// access). Combining <c>[Resource]</c> with <c>[AllowAnonymous]</c> is a compile-time error.
/// </para>
/// <para>
/// The optional <see cref="ResourceKeyTemplate"/> property carries a hierarchical resource key
/// template (e.g. <c>"/Tenants/{Auth:AccountId}/Campaigns/{Id}"</c>) used by
/// <c>IResourceAuthorizationProvider</c> to evaluate instance-level permission claims.
/// Placeholder tokens of the form <c>{PropertyName}</c> are bound from the operation's
/// properties at runtime, while <c>{Auth:ClaimType}</c> tokens are bound from the
/// authenticated principal's claims.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ResourceAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="ResourceAttribute"/> with the specified scope pair.
    /// </summary>
    /// <param name="noun">The resource noun (e.g. "account", "tenant").</param>
    /// <param name="verb">The action verb (e.g. "read", "write", "manage").</param>
    public ResourceAttribute(string noun, string verb)
    {
        this.Noun = noun;
        this.Verb = verb;
    }

    /// <summary>
    /// Initialises a new <see cref="ResourceAttribute"/> with the specified scope pair and
    /// resource key template for instance-level authorization.
    /// </summary>
    /// <param name="noun">The resource noun (e.g. "account", "tenant").</param>
    /// <param name="verb">The action verb (e.g. "read", "write", "manage").</param>
    /// <param name="resourceKeyTemplate">
    /// A hierarchical resource key template (e.g. <c>"/Tenants/{Auth:AccountId}/Campaigns/{Id}"</c>).
    /// </param>
    public ResourceAttribute(string noun, string verb, string resourceKeyTemplate)
    {
        this.Noun = noun;
        this.Verb = verb;
        this.ResourceKeyTemplate = resourceKeyTemplate;
    }

    /// <summary>
    /// The resource noun identifying the protected resource type.
    /// </summary>
    public string Noun { get; }

    /// <summary>
    /// The action verb identifying the operation being performed on the resource.
    /// </summary>
    public string Verb { get; }

    /// <summary>
    /// An optional hierarchical resource key template for instance-level authorization.
    /// Tokens of the form <c>{PropertyName}</c> are resolved from the operation's properties,
    /// and <c>{Auth:ClaimType}</c> tokens are resolved from the authenticated principal's claims.
    /// When <see langword="null"/>, only scope-pair (noun+verb) authorization is applied.
    /// </summary>
    public string? ResourceKeyTemplate { get; }
}
