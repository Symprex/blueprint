using System.Diagnostics;
using Blueprint.Auditing;
using Blueprint.Authorization;
using Blueprint.Diagnostics;
using Blueprint.Middleware;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Middleware;
using Blueprint.Tenancy;

namespace Blueprint;

/// <summary>
/// Base class for generated operation executor pipelines that centralises activity lifecycle, structured logging,
/// and exception translation so individual pipelines only implement the operation-specific execution core.
/// </summary>
/// <remarks>
/// The shell also resolves and caches common cross-cutting services (tenancy, property population, auditing)
/// so that every generated pipeline shares the same field layout without per-pipeline field duplication.
/// Generated code references the protected properties to invoke these services at the correct pipeline stage.
/// </remarks>
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
    private readonly IServiceProvider? _rootServiceProvider;
    private BlueprintAppExecutor? _executorCached;
    private bool _executorResolved;

    /// <summary>
    /// Gets the pipeline-scoped logger created from the <see cref="ILoggerFactory"/> supplied at construction time.
    /// Generated pipelines use this when injecting <c>ILogger</c> into handler base-class properties
    /// (e.g., the <c>ApiOperations.Logger</c> pattern used by Signature 365 handlers).
    /// </summary>
    protected ILogger Logger => this._logger;

    /// <summary>
    /// Gets the tenant accessor cached at pipeline construction time, or <see langword="null"/> when
    /// first-class tenancy is not registered. Generated tenant module code reads this to store the
    /// resolved tenant ID during the Population stage.
    /// </summary>
    protected ITenantAccessor? TenantAccessor { get; }

    /// <summary>
    /// Gets the tenant resolver cached at pipeline construction time, or <see langword="null"/> when
    /// first-class tenancy is not registered. Generated tenant module code reads this to resolve the
    /// tenant from the authenticated principal during the Population stage.
    /// </summary>
    protected ITenantResolver? TenantResolver { get; }

    /// <summary>
    /// Gets the property populators resolved at pipeline construction time. Returns an empty array
    /// when no populators are registered. Generated property population module code iterates this
    /// during the Population stage.
    /// </summary>
    protected IPropertyPopulator[] PropertyPopulators { get; }

    /// <summary>
    /// Gets the audit sink cached at pipeline construction time, or <see langword="null"/> when
    /// auditing is not configured. Generated auditing module code reads this in the PostExecution
    /// stage to record audit events for successful operations.
    /// </summary>
    protected IAuditSink? AuditSink { get; }

    /// <summary>
    /// Gets the resource authorization provider cached at pipeline construction time, or
    /// <see langword="null"/> when none is registered. Receives pre-expanded resource keys
    /// (compile-time string interpolation) for zero-reflection authorization checks.
    /// </summary>
    protected IResourceAuthorizationProvider? ResourceAuthorizationProvider { get; }

    /// <summary>
    /// Gets the API authoriser instances cached at pipeline construction time. Returns an empty
    /// array when no authorisers are registered. Generated authorisation module code iterates this
    /// during the Authorisation stage to apply business-level authorization rules.
    /// </summary>
    protected IApiAuthoriser[] ApiAuthorisers { get; }

    /// <summary>
    /// Gets the per-operation authoriser instances cached at pipeline construction time. Returns an
    /// empty array when no operation authorisers are registered. Generated authorisation module code
    /// iterates this alongside <see cref="ApiAuthorisers"/> during the Authorisation stage.
    /// </summary>
    protected IOperationAuthoriser[] OperationAuthorisers { get; }

    /// <summary>
    /// Gets the user authorisation context factory cached at pipeline construction time, or
    /// <see langword="null"/> when none is registered. Generated authentication module code calls
    /// this to load the user context after identity verification, making claims-based context
    /// available for authorization template expansion.
    /// </summary>
    protected IUserAuthorisationContextFactory? UserAuthorisationContextFactory { get; }

    /// <summary>
    /// Gets the resource-event audit sink cached at pipeline construction time, or
    /// <see langword="null"/> when none is registered. Generated resource-event audit module code
    /// calls this in the PostExecution stage to record resource creation/update/deletion events.
    /// </summary>
    protected IResourceEventAuditSink? ResourceEventAuditSink { get; }

    /// <summary>
    /// Gets the <see cref="BlueprintAppExecutor"/> used for nested operation dispatch (e.g. resolving
    /// <see cref="Blueprint.Http.ResourceEvent.SelfQuery"/>). Lazily resolved from the root service
    /// provider on first access to avoid circular-dependency issues during construction: the executor
    /// creates pipelines, each of which would eagerly resolve the executor that is still being built.
    /// </summary>
    protected BlueprintAppExecutor? Executor
    {
        get
        {
            if (!this._executorResolved)
            {
                this._executorCached = this._rootServiceProvider is not null
                    ? Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<BlueprintAppExecutor>(this._rootServiceProvider)
                    : null;
                this._executorResolved = true;
            }

            return this._executorCached;
        }
    }

    /// <summary>
    /// Gets the <see cref="System.Text.Json.JsonSerializerOptions"/> used for body deserialization,
    /// cached at pipeline construction time. Contains custom converters (e.g. NodaTime) configured
    /// via <see cref="Blueprint.Http.BlueprintJsonOptions"/>. Passed through to
    /// <see cref="PipelineHelpers.ReadJsonBodyAsync{TOperation}"/> so that the generated
    /// <c>PopulateFromJsonBody</c> delegate can use converters for complex types without per-request
    /// DI resolution.
    /// </summary>
    protected System.Text.Json.JsonSerializerOptions? BodyJsonOptions { get; }

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
    /// <remarks>
    /// Kept for backward compatibility with companion generators that do not pass <c>IServiceProvider</c>.
    /// Common cross-cutting services are not resolved — subclasses must manage their own fields.
    /// </remarks>
    protected OperationExecutorShell(ILoggerFactory loggerFactory, string operationTypeName, bool suppressActivity = false)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationTypeName);

        this._logger = loggerFactory.CreateLogger(operationTypeName + "ExecutorPipeline");
        this._operationTypeName = operationTypeName;
        this._activityName = operationTypeName + "Pipeline";
        this._suppressActivity = suppressActivity;
        this.PropertyPopulators = [];
        this.ApiAuthorisers = [];
        this.OperationAuthorisers = [];
    }

    /// <summary>
    /// Initialises a new instance of <see cref="OperationExecutorShell"/> with the supplied logger factory,
    /// operation type name, and service provider. Common cross-cutting services (tenancy, property population,
    /// auditing) are resolved once from the root <paramref name="serviceProvider"/> and cached for the
    /// pipeline's lifetime, eliminating per-pipeline field duplication in generated code.
    /// </summary>
    /// <param name="loggerFactory">The logger factory that creates the pipeline-scoped logger instance.</param>
    /// <param name="operationTypeName">The short operation type name used in activity names and log messages.</param>
    /// <param name="serviceProvider">The root service provider used to resolve cached cross-cutting services.</param>
    /// <param name="suppressActivity">
    /// When <c>true</c>, the pipeline will not create a trace <see cref="Activity"/> for this operation.
    /// Logging, audit sinks, and exception handling remain active.
    /// </param>
    /// <remarks>
    /// Retained for backward compatibility with companion generators that have not yet adopted the
    /// <see cref="PipelineServices"/> overload. Prefer the <see cref="PipelineServices"/> constructor
    /// for new code — it avoids redundant DI lookups when many pipelines share the same service set.
    /// </remarks>
    protected OperationExecutorShell(ILoggerFactory loggerFactory, string operationTypeName, IServiceProvider serviceProvider, bool suppressActivity = false)
        : this(loggerFactory, operationTypeName, suppressActivity)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        this.TenantAccessor = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ITenantAccessor>(serviceProvider);
        this.TenantResolver = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ITenantResolver>(serviceProvider);
        this.PropertyPopulators = PipelineHelpers.ResolveServiceArray<IPropertyPopulator>(serviceProvider);
        this.AuditSink = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IAuditSink>(serviceProvider);
        this.ResourceAuthorizationProvider = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IResourceAuthorizationProvider>(serviceProvider);
        this.ApiAuthorisers = PipelineHelpers.ResolveServiceArray<IApiAuthoriser>(serviceProvider);
        this.OperationAuthorisers = PipelineHelpers.ResolveServiceArray<IOperationAuthoriser>(serviceProvider);
        this.UserAuthorisationContextFactory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IUserAuthorisationContextFactory>(serviceProvider);
        this.ResourceEventAuditSink = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IResourceEventAuditSink>(serviceProvider);

        // Store the root service provider for lazy Executor resolution — see Executor property.
        this._rootServiceProvider = serviceProvider;

        var blueprintJsonOpts = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<Microsoft.Extensions.Options.IOptions<Blueprint.Http.BlueprintJsonOptions>>(serviceProvider);
        this.BodyJsonOptions = blueprintJsonOpts?.Value?.SerializerOptions;
    }

    /// <summary>
    /// Initialises a new instance of <see cref="OperationExecutorShell"/> from a pre-resolved
    /// <see cref="PipelineServices"/> bundle. Property assignments replace per-pipeline DI lookups,
    /// so all pipelines share the same resolved service set with no redundant container queries.
    /// </summary>
    /// <param name="loggerFactory">The logger factory that creates the pipeline-scoped logger instance.</param>
    /// <param name="operationTypeName">The short operation type name used in activity names and log messages.</param>
    /// <param name="services">The pre-resolved cross-cutting service bundle created once per executor.</param>
    /// <param name="suppressActivity">
    /// When <c>true</c>, the pipeline will not create a trace <see cref="Activity"/> for this operation.
    /// Logging, audit sinks, and exception handling remain active.
    /// </param>
    protected OperationExecutorShell(ILoggerFactory loggerFactory, string operationTypeName, PipelineServices services, bool suppressActivity = false)
        : this(loggerFactory, operationTypeName, suppressActivity)
    {
        ArgumentNullException.ThrowIfNull(services);

        this.TenantAccessor = services.TenantAccessor;
        this.TenantResolver = services.TenantResolver;
        this.PropertyPopulators = services.PropertyPopulators;
        this.AuditSink = services.AuditSink;
        this.ResourceAuthorizationProvider = services.ResourceAuthorizationProvider;
        this.ApiAuthorisers = services.ApiAuthorisers;
        this.OperationAuthorisers = services.OperationAuthorisers;
        this.UserAuthorisationContextFactory = services.UserAuthorisationContextFactory;
        this.ResourceEventAuditSink = services.ResourceEventAuditSink;
        this.BodyJsonOptions = services.BodyJsonOptions;

        // Store the root service provider for lazy Executor resolution — see Executor property.
        this._rootServiceProvider = services.ServiceProvider;
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

            return await this.ExecuteCoreAsync(context).ConfigureAwait(false);
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
