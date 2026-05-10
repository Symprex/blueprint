namespace Blueprint.Marten;

/// <summary>
/// Provides ambient access to the current scoped <see cref="IServiceProvider"/> for Marten
/// session listeners that run outside the DI scope (e.g. singleton listeners on
/// <c>StoreOptions.Listeners</c>).
/// </summary>
/// <remarks>
/// <para>
/// The generated Blueprint pipeline sets <see cref="Current"/> to the operation's scoped
/// service provider before calling the handler, and clears it after the pipeline completes.
/// This allows <see cref="DomainEventDispatchingSessionListener"/> to resolve
/// <see cref="IDomainEventHandler{TEvent}"/> instances from the correct scope.
/// </para>
/// <para>
/// This pattern avoids the need to register the session listener as a scoped service,
/// which is not supported by Marten's <c>StoreOptions.Listeners</c> singleton list.
/// </para>
/// </remarks>
public static class MartenScopeAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> _current = new();

    /// <summary>
    /// Gets or sets the current scoped service provider. Set by the Blueprint pipeline
    /// and read by <see cref="DomainEventDispatchingSessionListener"/>.
    /// </summary>
    public static IServiceProvider? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
