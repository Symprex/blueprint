using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// A <see cref="IBackgroundTaskPreprocessor"/> that captures ambient context from all registered
/// <see cref="IBackgroundTaskContextProvider"/> instances and stores it in the envelope headers
/// with a <c>ctx:</c> prefix to distinguish context headers from trace headers.
/// </summary>
/// <remarks>
/// <para>
/// This enables multi-tenancy, user context, and correlation ID propagation to background tasks.
/// The <see cref="TaskExecutor"/> reads these headers back and calls
/// <see cref="IBackgroundTaskContextProvider.RestoreContext"/> before handler execution.
/// </para>
/// </remarks>
public sealed class ContextCapturingPreprocessor : IBackgroundTaskPreprocessor
{
    /// <summary>
    /// The prefix used for context headers in the envelope to distinguish them from trace headers.
    /// </summary>
    public const string ContextHeaderPrefix = "ctx:";

    private readonly IEnumerable<IBackgroundTaskContextProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextCapturingPreprocessor"/> class.
    /// </summary>
    /// <param name="providers">The registered context providers to capture from.</param>
    public ContextCapturingPreprocessor(IEnumerable<IBackgroundTaskContextProvider> providers)
    {
        this._providers = providers;
    }

    /// <inheritdoc />
    public void Preprocess(BackgroundTaskEnvelope envelope)
    {
        foreach (var provider in this._providers)
        {
            var context = provider.CaptureContext();

            if (context is null or { Count: 0 })
            {
                continue;
            }

            envelope.Headers ??= new Dictionary<string, string>();

            foreach (var (key, value) in context)
            {
                envelope.Headers[$"{ContextHeaderPrefix}{key}"] = value;
            }
        }
    }
}
