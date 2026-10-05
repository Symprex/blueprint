using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the <c>ResourceEventSelfQueryModule</c> resolves <see cref="ResourceEvent.SelfQuery"/>
/// by executing the nested query through the generated pipeline and populating <see cref="ResourceEvent.Data"/>.
/// Also verifies correlation-ID stamping and best-effort degradation when the SelfQuery fails.
/// </summary>
public class ResourceEventSelfQueryModuleIntegrationTests
{
    [Fact]
    public async Task When_handler_returns_ResourceDeleted_with_SelfQuery_then_the_SelfQuery_is_not_run()
    {
        // Legacy's ResourceEventHandler loaded an event's data through its self query only when the event was not a
        // deletion: a deleted resource cannot be loaded, and its query may fail or find nothing.
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var pipeline = executor.GetPipeline(typeof(SelfQueryDeleteCommand));
        var context = new ApiOperationContext(serviceProvider, app, SelfQueryDeleteCommand.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceDeleted<SelfQueryTestApiResource>>();
        var resourceEvent = (ResourceDeleted<SelfQueryTestApiResource>)result;
        resourceEvent.Data.Should().BeNull("the self query of a deleted event is not run");
        resourceEvent.SelfQuery.Should().BeOfType<GetSelfQueryTestResourceQuery>().Which.Id.Should().Be("test-42");
    }

    [Fact]
    public async Task When_handler_returns_ResourceEvent_with_SelfQuery_then_CorrelationId_is_stamped_from_Activity()
    {
        using var activity = new System.Diagnostics.Activity("selfquery-correlation-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var pipeline = executor.GetPipeline(typeof(SelfQueryDeleteCommand));
        var context = new ApiOperationContext(serviceProvider, app, SelfQueryDeleteCommand.Configure(), CancellationToken.None)
        {
            Activity = activity,
        };

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = (ResourceDeleted<SelfQueryTestApiResource>)result;
        resourceEvent.CorrelationId.Should().NotBeNullOrEmpty("CorrelationId should be stamped from the active Activity");
        resourceEvent.CorrelationId.Should().Be(activity.Id);
    }

    [Fact]
    public async Task When_handler_returns_ResourceEvent_with_inline_data_then_SelfQuery_is_not_executed()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var pipeline = executor.GetPipeline(typeof(SelfQueryCreateWithDataCommand));
        var context = new ApiOperationContext(serviceProvider, app, SelfQueryCreateWithDataCommand.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceCreated<SelfQueryTestApiResource>>();
        var resourceEvent = (ResourceCreated<SelfQueryTestApiResource>)result;
        resourceEvent.Data.Should().NotBeNull("Data was provided inline, not via SelfQuery");
        resourceEvent.Data!.Name.Should().Be("Inline Data", "Data should be the inline value, not from a SelfQuery");
    }

    [Fact]
    public async Task When_no_executor_registered_then_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();

        // Use a service provider that returns no executor
        var serviceProvider = new SelfQueryTestServiceProvider(app, skipExecutor: true);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        var pipeline = executor.GetPipeline(typeof(SelfQueryDeleteCommand));
        var context = new ApiOperationContext(serviceProvider, app, SelfQueryDeleteCommand.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceDeleted<SelfQueryTestApiResource>>();
        var resourceEvent = (ResourceDeleted<SelfQueryTestApiResource>)result;
        resourceEvent.Data.Should().BeNull("SelfQuery cannot be resolved when no executor is registered");
    }

    [Fact]
    public async Task When_handler_returns_DeferredResourceEventResult_then_ResolvedContent_is_populated()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var pipeline = executor.GetPipeline(typeof(DeferredSelfQueryCommand));
        var context = new ApiOperationContext(serviceProvider, app, DeferredSelfQueryCommand.Configure(), CancellationToken.None);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeAssignableTo<IDeferredResourceEventResult>();
        var deferred = (IDeferredResourceEventResult)result;
        deferred.ResolvedContent.Should().NotBeNull("SelfQuery should have been resolved and stored on the deferred instance");
        deferred.ResourceId.Should().Be("test-42", "ResourceId should be derived from the resolved content");

        var resource = deferred.ResolvedContent.Should().BeOfType<SelfQueryTestApiResource>().Subject;
        resource.Name.Should().Be("Resolved from SelfQuery");
    }

    [Fact]
    public async Task When_handler_returns_DeferredResourceEventResult_then_CorrelationId_is_stamped()
    {
        using var activity = new System.Diagnostics.Activity("deferred-correlation-test");
        activity.Start();

        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var pipeline = executor.GetPipeline(typeof(DeferredSelfQueryCommand));
        var context = new ApiOperationContext(serviceProvider, app, DeferredSelfQueryCommand.Configure(), CancellationToken.None)
        {
            Activity = activity,
        };

        var result = await pipeline.ExecuteAsync(context);

        var deferred = (IDeferredResourceEventResult)result;
        deferred.CorrelationId.Should().NotBeNullOrEmpty("CorrelationId should be stamped from the active Activity");
        deferred.CorrelationId.Should().Be(activity.Id);
    }

    // Legacy loaded an event's data through its self query inside ResourceEventHandler (Signature365 8b40ecc961,
    // Blueprint.Http/Middleware/ResourceEventHandler.cs:48-52), whose middleware matched commands only
    // (ResourceEventHandlerMiddlewareBuilder.cs:12-15, `operation.IsCommand`, Blueprint/ApiOperationDescriptor.cs:124). So a
    // query's event kept the data its handler gave it, none here, and its self query was never run.
    [Fact]
    public async Task When_a_query_returns_a_resource_event_with_a_self_query_then_the_self_query_is_not_run()
    {
        var (context, pipeline) = Create<UpdatedEventOnlyQuery>();

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<SelfQueryTestApiResource>>().Subject;
        resourceEvent.Data.Should().BeNull("legacy ran a self query for a command's event only");
        resourceEvent.SelfQuery.Should().BeOfType<GetSelfQueryTestResourceQuery>().Which.Id.Should().Be("test-42");
    }

    [Fact]
    public async Task When_a_query_returns_a_deferred_resource_event_then_its_self_query_is_not_run()
    {
        // The deferred event is new-only; it follows the rule legacy gave every resource event (above).
        var (context, pipeline) = Create<DeferredEventOnlyQuery>();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeAssignableTo<IDeferredResourceEventResult>()
            .Which.ResolvedContent.Should().BeNull("a query's self query is not run");
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline) Create<TOperation>()
        where TOperation : IOperation
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new SelfQueryTestServiceProvider(app);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        return (new ApiOperationContext(serviceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None), executor.GetPipeline(typeof(TOperation)));
    }

    /// <summary>
    /// Test service provider that supplies the executor for nested SelfQuery dispatch.
    /// Uses lazy resolution so the executor can be set after construction (breaking
    /// the circular dependency between executor → pipeline → service provider → executor).
    /// </summary>
    private sealed class SelfQueryTestServiceProvider : IServiceProvider
    {
        private readonly BlueprintApp _app;
        private readonly bool _skipExecutor;
        private BlueprintAppExecutor? _executor;

        public SelfQueryTestServiceProvider(BlueprintApp app, bool skipExecutor = false)
        {
            this._app = app;
            this._skipExecutor = skipExecutor;
        }

        /// <summary>
        /// Set after executor construction to break the circular dependency.
        /// The service provider returns this lazily when asked for <see cref="BlueprintAppExecutor"/>.
        /// </summary>
        public BlueprintAppExecutor? Executor
        {
            get => this._executor;
            set => this._executor = value;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(BlueprintAppExecutor) && !this._skipExecutor)
            {
                return this._executor;
            }

            return null;
        }
    }
}

/// <summary>
/// An API resource used by SelfQuery tests.
/// </summary>
public class SelfQueryTestApiResource : ApiResource
{
    /// <inheritdoc />
    public override string Object => "selfquery-test-resource";

    /// <summary>Gets or sets the resource identifier.</summary>
    public string Id { get; set; } = "test-42";

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = "Default";

    /// <inheritdoc />
    public override string ResourceKey => this.Id;
}

/// <summary>
/// A query that returns a <see cref="SelfQueryTestApiResource"/>. This is the target of the
/// SelfQuery — the pipeline resolves this query to populate the ResourceEvent.Data.
/// </summary>
public partial class GetSelfQueryTestResourceQuery : IQuery<SelfQueryTestApiResource>
{
    /// <summary>Gets or sets the resource ID to look up.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Handles the query by returning a test resource.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>An OK result containing the test resource.</returns>
    public static SelfQueryTestApiResource Handle(GetSelfQueryTestResourceQuery operation)
    {
        return new SelfQueryTestApiResource
        {
            Id = operation.Id,
            Name = "Resolved from SelfQuery",
        };
    }
}

/// <summary>
/// A command that returns a <see cref="ResourceDeleted{T}"/> with a <see cref="ResourceEvent.SelfQuery"/>
/// to test automatic SelfQuery resolution in the generated pipeline.
/// </summary>
public partial class SelfQueryDeleteCommand : ICommand<ResourceDeleted<SelfQueryTestApiResource>>
{
    /// <summary>Handles the command by returning a ResourceDeleted with a SelfQuery.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A ResourceDeleted result with SelfQuery set and Data null.</returns>
    public static ResourceDeleted<SelfQueryTestApiResource> Handle(SelfQueryDeleteCommand operation)
    {
        return new ResourceDeleted<SelfQueryTestApiResource>(
            new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>
/// A command that returns a <see cref="ResourceCreated{T}"/> with inline data (no SelfQuery).
/// Used to prove the module skips resolution when Data is already populated.
/// </summary>
public partial class SelfQueryCreateWithDataCommand : ICommand<ResourceCreated<SelfQueryTestApiResource>>
{
    /// <summary>Handles the command by returning a ResourceCreated with inline data.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A ResourceCreated result with inline resource data.</returns>
    public static ResourceCreated<SelfQueryTestApiResource> Handle(SelfQueryCreateWithDataCommand operation)
    {
        var resource = new SelfQueryTestApiResource
        {
            Id = "created-1",
            Name = "Inline Data",
        };

        return new ResourceCreated<SelfQueryTestApiResource>(resource);
    }
}

/// <summary>
/// Resource event family for the deferred SelfQuery integration test.
/// Declares a Created event that uses <c>[ResourceEventSelfQuery]</c> to defer content resolution.
/// </summary>
[ResourceEventFamily(typeof(SelfQueryTestApiResource), ResourceObject = "selfquery-test-resource")]
public static partial class SelfQueryTestResourceEvents
{
    /// <summary>
    /// A deferred "created" event that resolves its content via a nested SelfQuery execution.
    /// </summary>
    [ResourceEventReason(ChangeType.Created, "created")]
    public sealed partial record Created([ResourceEventSelfQuery] GetSelfQueryTestResourceQuery SelfQuery);
}

/// <summary>A query that returns an updated event carrying only its self query.</summary>
public partial class UpdatedEventOnlyQuery : IQuery<ResourceEvent<SelfQueryTestApiResource>>
{
    /// <summary>Handles the query by returning an event with a self query and no data.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>An updated resource event whose data is left to its self query.</returns>
    public static ResourceEvent<SelfQueryTestApiResource> Handle(UpdatedEventOnlyQuery operation)
    {
        return new ResourceEvent<SelfQueryTestApiResource>(
            ResourceEventChangeType.Updated,
            "updated",
            new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>A query that returns a deferred resource event, whose content is left to its self query.</summary>
public partial class DeferredEventOnlyQuery : IQuery<DeferredResourceEventResult<SelfQueryTestApiResource>>
{
    /// <summary>Handles the query by returning a deferred resource event with a self query.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>A deferred resource event.</returns>
    public static DeferredResourceEventResult<SelfQueryTestApiResource> Handle(DeferredEventOnlyQuery operation)
    {
        return new SelfQueryTestResourceEvents.Created(new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}

/// <summary>
/// A command that returns a <see cref="DeferredResourceEventResult{T}"/> via the generated
/// <c>[ResourceEventFamily]</c> pattern with <c>[ResourceEventSelfQuery]</c>.
/// </summary>
public partial class DeferredSelfQueryCommand : ICommand<DeferredResourceEventResult<SelfQueryTestApiResource>>
{
    /// <summary>Handles the command by returning a deferred resource event with a SelfQuery.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A DeferredResourceEventResult that the pipeline module will resolve.</returns>
    public static DeferredResourceEventResult<SelfQueryTestApiResource> Handle(DeferredSelfQueryCommand operation)
    {
        return new SelfQueryTestResourceEvents.Created(new GetSelfQueryTestResourceQuery { Id = "test-42" });
    }
}
