namespace Blueprint.Persistence;

/// <summary>
/// Names the problem an operation answers with when the unit of work's save after its handler is refused because another
/// request wrote first (<see cref="SaveRefusedException"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Factory"/> names a static <c>ApiExceptionFactory</c> field or property of the operation class, usually
/// written with <c>nameof</c>, as <c>[TranslateError]</c>'s target is. The generated pipeline reads it, so it must not be
/// private or have a private getter, and must be public on an operation from a referenced assembly. The generator checks
/// it (BPP002) and documents it among the operation's error responses. The problem it creates has the <see cref="SaveRefusedException"/> as its inner
/// exception. Name a 409 factory: the status is the factory's own, and a refused save is a conflict.
/// </para>
/// <para>
/// Without it, a refused save is answered with Blueprint's own <c>save_refused</c> 409 problem, which is not documented on
/// the operation, as every saving operation could in principle meet it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class SaveRefusedErrorAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="SaveRefusedErrorAttribute"/> class.
    /// </summary>
    /// <param name="factory">The name of the static <c>ApiExceptionFactory</c> member, on the operation class, to answer with.</param>
    public SaveRefusedErrorAttribute(string factory)
    {
        this.Factory = factory;
    }

    /// <summary>
    /// Gets the name of the static <c>ApiExceptionFactory</c> member, on the operation class, to answer with.
    /// </summary>
    public string Factory { get; }
}
