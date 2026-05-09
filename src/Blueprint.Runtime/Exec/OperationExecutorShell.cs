using System.Diagnostics;
using Blueprint.Diagnostics;
using Blueprint.Middleware;
using Blueprint.Runtime.Exec;

namespace Blueprint;

/// <summary>
/// Base class for generated operation executor pipelines that centralises activity lifecycle, structured logging,
/// and exception translation so individual pipelines only implement the operation-specific execution core.
/// </summary>
public abstract class OperationExecutorShell : IOperationExecutorPipeline
{
    private static readonly Action<ILogger, string, Exception?> _logApiOperationExecuting =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, "ApiOperationExecuting"),
            "Executing API operation {OperationType} with handler");

    private static readonly Action<ILogger, string, Exception?> _logNonFatalException =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, "Non500Exception"),
            "A non-5xx ApiException has occurred with message {ExceptionMessage}");

    private static readonly Action<ILogger, string, Exception?> _logUnhandledException =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(5, "UnhandledException"),
            "An unhandled exception has occurred with message {ExceptionMessage}");

    private readonly ILogger _logger;
    private readonly string _operationTypeName;
    private readonly string _activityName;
    private readonly bool _suppressActivity;

    /// <summary>
    /// Initialises a new instance of <see cref="OperationExecutorShell"/> with the supplied logger factory and
    /// operation type name used for activity tracing and structured log entries.
    /// </summary>
    /// <param name="loggerFactory">The logger factory that creates the pipeline-scoped logger instance.</param>
    /// <param name="operationTypeName">The short operation type name used in activity names and log messages.</param>
    /// <param name="suppressActivity">
    /// When <c>true</c>, the pipeline will not create a trace <see cref="Activity"/> for this operation.
    /// Logging, audit sinks, and exception handling remain active.
    /// </param>
    protected OperationExecutorShell(ILoggerFactory loggerFactory, string operationTypeName, bool suppressActivity = false)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationTypeName);

        this._logger = loggerFactory.CreateLogger(operationTypeName + "ExecutorPipeline");
        this._operationTypeName = operationTypeName;
        this._activityName = operationTypeName + "Pipeline";
        this._suppressActivity = suppressActivity;
    }

    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(ApiOperationContext context)
    {
        using var activity = this._suppressActivity
            ? null
            : BlueprintActivitySource.ActivitySource.StartActivity(
                this._activityName,
                ActivityKind.Internal);

        try
        {
            _logApiOperationExecuting(this._logger, this._operationTypeName, null);

            return await this.ExecuteCoreAsync(context);
        }
        catch (Validation.ValidationException e)
        {
            context.Activity?.SetTag("otel.status_code", "OK");
            return new ValidationFailedResult(e);
        }
        catch (System.ComponentModel.DataAnnotations.ValidationException e)
        {
            context.Activity?.SetTag("otel.status_code", "OK");
            return ValidationMiddlewareBuilder.ToValidationFailedResult(e);
        }
        catch (Exception e)
        {
            if (e is ApiException apiException && apiException.HttpStatus < 500)
            {
                _logNonFatalException(this._logger, e.Message, e);
                context.Activity?.SetTag("otel.status_code", "OK");
            }
            else
            {
                BlueprintActivitySource.RecordException(context.Activity, e, false);
                _logUnhandledException(this._logger, e.Message, e);
                context.Activity?.SetTag("otel.status_code", "ERROR");
                context.Activity?.SetTag("otel.status_description", e.Message);
            }

            return new UnhandledExceptionResult(e);
        }
    }

    /// <summary>
    /// Executes the operation-specific pipeline stages (population, authentication, validation, handler invocation).
    /// Generated pipelines override this method with the tailored per-operation pipeline body.
    /// </summary>
    /// <param name="context">The operation execution context.</param>
    /// <returns>The operation result produced by the pipeline.</returns>
    protected abstract Task<OperationResult> ExecuteCoreAsync(ApiOperationContext context);
}
