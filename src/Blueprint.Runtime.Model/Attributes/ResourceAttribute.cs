namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares a resource permission scope requirement on an operation. The noun identifies
/// the resource type and the verb identifies the action being performed. Multiple
/// <c>[Resource]</c> attributes use any-of semantics: the caller must satisfy at least one.
/// </summary>
/// <remarks>
/// Operations decorated with <c>[Resource]</c> implicitly require authentication (permissioned
/// access). Combining <c>[Resource]</c> with <c>[AllowAnonymous]</c> is a compile-time error.
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
    /// The resource noun identifying the protected resource type.
    /// </summary>
    public string Noun { get; }

    /// <summary>
    /// The action verb identifying the operation being performed on the resource.
    /// </summary>
    public string Verb { get; }
}
