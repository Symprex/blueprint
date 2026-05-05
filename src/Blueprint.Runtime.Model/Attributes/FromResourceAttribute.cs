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
    /// The operation type from which resource permission scope pairs are inherited.
    /// </summary>
    public Type SourceOperation { get; }
}
