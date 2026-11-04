using Blueprint.Model;
using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// Abstraction for unit-of-work persistence coordination within the generated pipeline.
/// The generated pipeline calls <see cref="SaveChangesAsync"/> in the PostExecution stage
/// after a successful handler execution of any operation without <c>[DoNotSaveChanges]</c> (a command, query or
/// background task), allowing consumers to commit tracked changes.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are resolved from DI (typically scoped per-operation). When no implementation
/// is registered, the generated pipeline skips persistence coordination entirely.
/// </para>
/// <para>
/// Disposal is owned by the DI scope, not by the generated pipeline. Implementations should
/// implement <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/> and register as scoped
/// services so the DI container manages their lifetime.
/// </para>
/// <para>
/// Concrete implementations live in optional companion packages (e.g. Marten, EF Core) or in
/// the consuming app's DI configuration.
/// </para>
/// </remarks>
[PublicAPI]
public interface IUnitOfWork
{
    /// <summary>
    /// Begins the unit of work for <paramref name="operation"/>, before its handler runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the generated pipeline on the same rule as <see cref="SaveChangesAsync"/> — every operation without
    /// <c>[DoNotSaveChanges]</c>, left out of a nested execution where the save is — after authorisation and validation,
    /// so a refused or invalid request begins nothing. A unit of work over a relational session opens its transaction
    /// here, so that writes the session issues before the save (an identity-keyed insert, an auto-flush before a query)
    /// commit with it, or not at all when the handler throws.
    /// </para>
    /// <para>
    /// The operation and its descriptor let an implementation decide per operation, for example from
    /// <see cref="ApiOperationDescriptor.TypeAttributes"/> or the HTTP methods it is exposed on. The pipeline's context
    /// is not passed because it is defined in Blueprint.Runtime, which this contract layer does not reference.
    /// </para>
    /// <para>
    /// No rollback is called when the handler throws or the operation fails: the DI scope disposes the unit of work,
    /// and a session disposed with an uncommitted transaction rolls it back. The default implementation does nothing,
    /// so a unit of work that needs no begin step does not implement it.
    /// </para>
    /// </remarks>
    /// <param name="operation">The operation about to be handled.</param>
    /// <param name="descriptor">The descriptor of <paramref name="operation"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the begin operation.</param>
    /// <returns>A task representing the asynchronous begin operation.</returns>
    ValueTask BeginAsync(object operation, ApiOperationDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <summary>
    /// Persists all tracked changes accumulated during handler execution.
    /// Called by the generated pipeline in the PostExecution stage after verifying the
    /// handler returned a successful result (not a validation failure, authentication failure,
    /// or unhandled exception).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the save operation.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
