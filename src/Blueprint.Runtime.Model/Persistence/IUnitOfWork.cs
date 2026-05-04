using JetBrains.Annotations;

namespace Blueprint.Persistence;

/// <summary>
/// Abstraction for unit-of-work persistence coordination within the generated pipeline.
/// The generated pipeline calls <see cref="SaveChangesAsync"/> in the PostExecution stage
/// after a successful command handler execution, allowing consumers to commit tracked changes.
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
    /// Persists all tracked changes accumulated during handler execution.
    /// Called by the generated pipeline in the PostExecution stage after verifying the
    /// handler returned a successful result (not a validation failure, authentication failure,
    /// or unhandled exception).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the save operation.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
