using JetBrains.Annotations;

namespace Blueprint.Tasks;

/// <summary>
/// Provides ambient context data that should travel with background tasks through scheduling
/// and execution. Implementations capture values from the current request scope (e.g., tenant ID,
/// user ID, correlation ID) and restore them when the task executes in a new DI scope.
/// </summary>
/// <remarks>
/// <para>
/// This is the compile-time equivalent of legacy Blueprint's <c>IBackgroundTaskContextProvider</c>.
/// In Signature365, context data (tenant, user, correlation) was stored alongside the task
/// and restored before handler execution. In the source-gen architecture, this is achieved
/// through preprocessor injection and executor restoration of header values.
/// </para>
/// <para>
/// Register implementations via DI as singletons. The background task scheduler
/// calls <see cref="IBackgroundTaskPreprocessor.Preprocess"/> for all registered preprocessors,
/// and the task executor reads headers to restore context before execution.
/// </para>
/// </remarks>
[PublicAPI]
public interface IBackgroundTaskContextProvider
{
    /// <summary>
    /// Captures the current ambient context values that should travel with the background task.
    /// Called during task scheduling to snapshot the current scope's context.
    /// </summary>
    /// <returns>
    /// A dictionary of context key-value pairs to store in the envelope headers,
    /// or <c>null</c> if no context needs to be propagated.
    /// </returns>
    IDictionary<string, string>? CaptureContext();

    /// <summary>
    /// Restores ambient context from values previously captured and stored in the task envelope.
    /// Called by the task executor before the handler runs, in the new DI scope.
    /// </summary>
    /// <param name="context">The context values captured during scheduling.</param>
    /// <param name="serviceProvider">The scoped service provider for the task execution.</param>
    void RestoreContext(IDictionary<string, string> context, IServiceProvider serviceProvider);
}
