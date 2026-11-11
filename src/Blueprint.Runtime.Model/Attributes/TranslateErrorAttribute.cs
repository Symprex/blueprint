namespace Blueprint.Model.Attributes;

/// <summary>
/// Translates an upstream failure, by its problem <c>type</c>, into a local <c>ApiExceptionFactory</c>'s problem when a
/// gateway forwards to an inner operation.
/// </summary>
/// <remarks>
/// <para>
/// Applies to a gateway operation, or to the gateway's <c>BlueprintApp</c> for every operation it hosts. An operation's
/// translation of a problem type wins over the app's.
/// </para>
/// <para>
/// <see cref="To"/> names a static <c>ApiExceptionFactory</c> field or property of the class this attribute is applied to,
/// usually written with <c>nameof</c>. The translated problem keeps the upstream problem's detail, and has the upstream
/// failure as its inner exception.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TranslateErrorAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="TranslateErrorAttribute"/> class.
    /// </summary>
    /// <param name="fromType">The upstream problem's <c>type</c>, matched exactly.</param>
    /// <param name="to">The name of the static <c>ApiExceptionFactory</c> member, on the attributed class, to answer with.</param>
    public TranslateErrorAttribute(string fromType, string to)
    {
        this.FromType = fromType;
        this.To = to;
    }

    /// <summary>
    /// Gets the upstream problem's <c>type</c>, matched exactly.
    /// </summary>
    public string FromType { get; }

    /// <summary>
    /// Gets the name of the static <c>ApiExceptionFactory</c> member, on the attributed class, to answer with.
    /// </summary>
    public string To { get; }
}
