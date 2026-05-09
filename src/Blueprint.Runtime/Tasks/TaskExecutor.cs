using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Tasks;

namespace Blueprint.Runtime.Tasks;

/// <summary>
/// Entry point for background task execution. Called by provider-specific infrastructure (e.g. Hangfire)
/// when a previously-scheduled task is ready to run. Creates a DI scope, constructs an
/// <see cref="ApiOperationContext"/>, and pushes the task through the generated executor pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Trace propagation: the <see cref="BackgroundTaskEnvelope.Headers"/> dictionary carries W3C Trace Context
/// headers injected by <see cref="IBackgroundTaskPreprocessor"/>. This executor extracts that context so the
/// background execution appears as a child span of the original request that scheduled the task.
/// </para>
/// </remarks>
public sealed class TaskExecutor
{
    /// <summary>
    /// The <see cref="ActivitySource"/> used for task execution activities.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Blueprint.Tasks", "1.0.0");

    private readonly BlueprintAppExecutor _executor;
    private readonly IServiceProvider _rootServiceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskExecutor"/> class.
    /// </summary>
    /// <param name="executor">The Blueprint app executor that dispatches operations to generated pipelines.</param>
    /// <param name="rootServiceProvider">The root service provider for creating per-task DI scopes.</param>
    public TaskExecutor(BlueprintAppExecutor executor, IServiceProvider rootServiceProvider)
    {
        this._executor = executor;
        this._rootServiceProvider = rootServiceProvider;
    }

    /// <summary>
    /// Executes a background task by pushing it through the generated executor pipeline.
    /// </summary>
    /// <param name="envelope">The task envelope containing the task and trace propagation headers.</param>
    /// <param name="configureActivity">Optional callback to add provider-specific tags to the activity.</param>
    /// <param name="cancellationToken">Cancellation token to abort execution.</param>
    /// <returns>A task representing the asynchronous execution.</returns>
    public async Task ExecuteAsync(
        BackgroundTaskEnvelope envelope,
        Action<Activity>? configureActivity = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var parentContext = ExtractParentContext(envelope.Headers);

        using var activity = ActivitySource.StartActivity(
            envelope.Task.GetType().Name,
            ActivityKind.Consumer,
            parentContext);

        if (activity is not null)
        {
            configureActivity?.Invoke(activity);
        }

        using var scope = this._rootServiceProvider.CreateScope();

        this.RestoreContext(envelope.Headers, scope.ServiceProvider);

        var operationType = envelope.Task.GetType();
        var descriptor = this._executor.App.Operations
            .FirstOrDefault(d => d.OperationType == operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for background task type '{operationType.FullName}'. " +
                $"Ensure the task is registered in the BlueprintApp.");

        var context = new ApiOperationContext(
            scope.ServiceProvider,
            this._executor.App,
            descriptor,
            cancellationToken)
        {
            Operation = (IOperation)envelope.Task,
        };

        var result = await this._executor.ExecuteAsync(context);

        if (result is UnhandledExceptionResult unhandled)
        {
            activity?.SetStatus(ActivityStatusCode.Error, unhandled.Exception.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", unhandled.Exception.GetType().FullName },
                { "exception.message", unhandled.Exception.Message },
            }));

            ExceptionDispatchInfo.Capture(unhandled.Exception).Throw();
        }
    }

    private static ActivityContext ExtractParentContext(IDictionary<string, string>? headers)
    {
        if (headers is null || !headers.TryGetValue("traceparent", out var traceparent))
        {
            return default;
        }

        var tracestate = headers.TryGetValue("tracestate", out var ts) ? ts : null;

        if (ActivityContext.TryParse(traceparent, tracestate, out var context))
        {
            return context;
        }

        return default;
    }

    private void RestoreContext(IDictionary<string, string>? headers, IServiceProvider scopedServiceProvider)
    {
        if (headers is null or { Count: 0 })
        {
            return;
        }

        var providers = scopedServiceProvider.GetService<IEnumerable<IBackgroundTaskContextProvider>>();

        if (providers is null)
        {
            return;
        }

        // Extract context headers (prefixed with "ctx:") into a clean dictionary for providers.
        var contextData = new Dictionary<string, string>();

        foreach (var (key, value) in headers)
        {
            if (key.StartsWith(ContextCapturingPreprocessor.ContextHeaderPrefix, StringComparison.Ordinal))
            {
                contextData[key[ContextCapturingPreprocessor.ContextHeaderPrefix.Length..]] = value;
            }
        }

        if (contextData.Count == 0)
        {
            return;
        }

        foreach (var provider in providers)
        {
            provider.RestoreContext(contextData, scopedServiceProvider);
        }
    }
}

