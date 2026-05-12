namespace Blueprint.Model.Attributes;

/// <summary>
/// Inherits resource permission scope requirements from another operation at compile time.
/// The referenced operation must declare at least one <c>[Resource("noun", "verb")]</c> attribute;
/// its scope pairs are inlined into the current operation's authorization metadata during generation.
/// </summary>
/// <remarks>
/// <para>
/// Inheritance is direct only — if the referenced operation itself uses <c>[FromResource]</c>,
/// the transitive chain is not followed. Only explicit <c>[Resource]</c> declarations on the
/// referenced type are inherited.
/// </para>
/// <para>
/// When a <c>resourceKeyTemplate</c> is provided, it overrides any templates on the source
/// operation's <c>[Resource]</c> attributes. This is the Signature 365 pattern where the
/// referencing operation has a different resource hierarchy from the source operation.
/// </para>
/// <para>
/// Combining <c>[FromResource]</c> with <c>[Resource]</c> on the same operation is a compile-time
/// error (BPAU005). Use one mechanism or the other.
/// </para>
/// <para>
/// Like <c>[Resource]</c>, operations with <c>[FromResource]</c> implicitly require authentication
/// (permissioned access). Combining with <c>[AllowAnonymous]</c> is a compile-time error.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FromResourceAttribute : Attribute
{
    /// <summary>
    /// Initialises a new <see cref="FromResourceAttribute"/> that inherits scope pairs from
    /// the specified source operation.
    /// </summary>
    /// <param name="sourceOperation">
    /// The operation type whose <c>[Resource]</c> declarations should be inherited.
    /// Must declare at least one <c>[Resource]</c> attribute.
    /// </param>
    public FromResourceAttribute(Type sourceOperation)
    {
        this.SourceOperation = sourceOperation;
    }

    /// <summary>
    /// Initialises a new <see cref="FromResourceAttribute"/> that inherits scope pairs from
    /// the specified source operation, overriding the resource key template.
    /// </summary>
    /// <param name="sourceOperation">
    /// The operation type whose <c>[Resource]</c> declarations should be inherited.
    /// Must declare at least one <c>[Resource]</c> attribute.
    /// </param>
    /// <param name="resourceKeyTemplate">
    /// A resource key template (e.g. <c>"/Tenants/{Auth:AccountId}/Items/{Id}"</c>) that overrides
    /// any templates declared on the source operation's <c>[Resource]</c> attributes. This allows
    /// the inheriting operation to participate in the same noun/verb scope pairs but with a different
    /// resource hierarchy path.
    /// </param>
    public FromResourceAttribute(Type sourceOperation, string resourceKeyTemplate)
    {
        this.SourceOperation = sourceOperation;
        this.ResourceKeyTemplate = resourceKeyTemplate;
    }

    /// <summary>
    /// The operation type from which resource permission scope pairs are inherited.
    /// </summary>
    public Type SourceOperation { get; }

    /// <summary>
    /// An optional resource key template that overrides the templates on the source operation's
    /// <c>[Resource]</c> attributes. When set, all inherited scope pairs use this template instead
    /// of the source's original templates.
    /// </summary>
    public string? ResourceKeyTemplate { get; }
}
