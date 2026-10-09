using Blueprint.Auditing;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Expansion;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the generated expansion stage expands the <c>Expandable&lt;T&gt;</c> properties of a
/// <see cref="ResourceEvent{TResource}"/>'s data as legacy Blueprint's <c>ResourceExpandMiddleware</c> did.
/// </summary>
/// <remarks>
/// <para>
/// Legacy wrapped a handler's <see cref="ResourceEvent"/> in an <c>OkResult</c>, walked the event's own properties, and
/// passed the event itself to the expander as <c>response</c>. So a property path is rooted at the event
/// (<c>Data.Department</c>), and Signature365's <c>AzureAdGroupApiResourceExpander</c> finds its add-on through
/// <c>response is ResourceEvent&lt;Microsoft365AddOnApiResource&gt;</c>.
/// </para>
/// <para>
/// Legacy expanded before its resource event handler resolved a <see cref="ResourceEvent.SelfQuery"/>, so data loaded by
/// the self-query was never expanded by the command's pipeline, only by the query's own.
/// </para>
/// </remarks>
public class ResourceEventExpansionIntegrationTests
{
    [Fact]
    public async Task When_resource_event_data_has_an_always_expand_property_then_it_is_expanded_with_the_event_as_the_response()
    {
        var (context, pipeline, expander) = Create<UpdateAlwaysExpandEmployeeCommand>(new DefaultHttpContext());

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<AlwaysExpandTestEmployee>>().Subject;
        resourceEvent.Data!.Department.Value.Should().NotBeNull("[AlwaysExpand] on the event's data must be expanded");
        expander.Responses.Should().ContainSingle()
            .Which.Should().BeSameAs(resourceEvent, "legacy passed the event itself as the expander's response");
    }

    [Fact]
    public async Task When_expand_names_the_property_under_Data_then_the_resource_events_data_is_expanded()
    {
        var (context, pipeline, expander) = Create<UpdateExpandableEmployeeCommand>(HttpContextWithExpand("Data.Department"));

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<ExpandableTestEmployee>>().Subject;
        resourceEvent.Data!.Department.Value.Should().NotBeNull();
        expander.Responses.Should().ContainSingle().Which.Should().BeSameAs(resourceEvent);
    }

    [Fact]
    public async Task When_expand_names_the_property_without_the_Data_prefix_then_it_is_not_expanded()
    {
        // The path is rooted at the event, as legacy's property walker rooted it.
        var (context, pipeline, expander) = Create<UpdateExpandableEmployeeCommand>(HttpContextWithExpand("Department"));

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ResourceEvent<ExpandableTestEmployee>>()
            .Which.Data!.Department.Value.Should().BeNull();
        expander.Responses.Should().BeEmpty();
    }

    [Fact]
    public async Task When_resource_event_data_comes_from_its_self_query_then_only_the_querys_pipeline_expands_it()
    {
        var (context, pipeline, expander) = Create<UpdateAlwaysExpandEmployeeViaSelfQueryCommand>(new DefaultHttpContext());

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<AlwaysExpandTestEmployee>>().Subject;
        resourceEvent.Data!.Department.Value.Should().NotBeNull("the self-query's own pipeline expands its [AlwaysExpand] property");
        expander.Responses.Should().ContainSingle("the command's pipeline must not expand the self-query's data a second time")
            .Which.Should().BeSameAs(resourceEvent.Data, "the call is the query's, whose response is its own resource");
    }

    [Fact]
    public async Task When_an_outer_handler_returns_an_event_an_inner_pipeline_loaded_by_its_self_query_then_the_outer_pipeline_expands_it()
    {
        // Legacy's ResourceExpandMiddleware expanded the data of the event its operation returned whenever that data was
        // there to expand (Signature365 8b40ecc961, Common/Api/ResourceExpandMiddleware.cs:53-84, :110-129). Only the
        // pipeline whose self query loaded the data did not, as it expanded before loading it. A command that hands on an
        // event another command's pipeline returned finds its data loaded, so its pipeline expanded it: once by the self
        // query's pipeline, once by the outer command's.
        var (context, pipeline, expander) = Create<PassOnAlwaysExpandEmployeeEventCommand>(new DefaultHttpContext());

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<AlwaysExpandTestEmployee>>().Subject;
        expander.Responses.Should().HaveCount(2, "the self query's pipeline expands its resource, and the outer pipeline the event it hands on");
        expander.Responses[0].Should().BeSameAs(resourceEvent.Data, "the first call is the self query's, whose response is its own resource");
        expander.Responses[1].Should().BeSameAs(resourceEvent, "the second is the outer pipeline's, whose response is the event");
    }

    [Fact]
    public async Task When_an_outer_query_returns_an_event_an_inner_pipeline_loaded_by_its_self_query_then_the_outer_pipeline_expands_it()
    {
        // As above, for a query: legacy's ResourceExpandMiddleware matched every operation (ResourceExpandMiddleware.cs:21-24),
        // queries included, although its ResourceEventHandler, which ran self queries, matched commands only
        // (ResourceEventHandlerMiddlewareBuilder.cs:12-15).
        var (context, pipeline, expander) = Create<HandOnAlwaysExpandEmployeeEventQuery>(new DefaultHttpContext());

        var result = await pipeline.ExecuteAsync(context);

        var resourceEvent = result.Should().BeOfType<ResourceEvent<AlwaysExpandTestEmployee>>().Subject;
        expander.Responses.Should().HaveCount(2, "the self query's pipeline expands its resource, and the outer query's pipeline the event it hands on");
        expander.Responses[1].Should().BeSameAs(resourceEvent, "the second is the outer pipeline's, whose response is the event");
    }

    [Fact]
    public async Task When_a_union_declares_a_resource_event_and_a_type_derived_from_it_then_the_event_is_expanded_once()
    {
        // Legacy expanded the one declared response its handler's result was (ResourceExpandMiddleware.cs:55-62). A
        // ResourceUpdated<T> is also a ResourceEvent<T>, so a union of both must not expand it once as each.
        var (context, pipeline, expander) = Create<UpdateAlwaysExpandEmployeeAsEitherEventCommand>(new DefaultHttpContext());

        await pipeline.ExecuteAsync(context);

        expander.Responses.Should().ContainSingle("the event is one member of the union, and expanded as that member only");
    }

    [Fact]
    public async Task When_resource_event_data_is_expanded_then_the_resource_event_audit_sink_is_given_it_expanded()
    {
        // Legacy expanded an event before auditing it. In its generated pipeline ResourceExpandMiddleware
        // (Signature365 8b40ecc961, Portal/.../AzureAdOauthReturnCommandExecutorPipeline.cs:142-144) ran before
        // ResourceEventHandlerMiddlewareBuilder (:146-149), whose ResourceEventHandler.HandleAsync handed that same event
        // to IResourceEventAuditRepository.AddAsync (Blueprint.Http/Middleware/ResourceEventHandler.cs:72-77).
        // Signature365's repository serialises the event's data as it receives it (EventStore.Contract/Messages/
        // EventMapper.cs:30), so what it is given must already be expanded. The sink copies the value out when it is
        // called, because the pipeline goes on to mutate the same event object.
        var sink = new SnapshottingResourceEventAuditSink();
        var (context, pipeline, _) = Create<UpdateAlwaysExpandEmployeeCommand>(new DefaultHttpContext(), sink);

        await pipeline.ExecuteAsync(context);

        sink.DepartmentNamesWhenRecorded.Should().Equal(
            ["Engineering"],
            "the event is audited once, and after its [AlwaysExpand] data has been expanded");
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline, TestDepartmentExpander Expander) Create<TOperation>(
        HttpContext httpContext,
        IResourceEventAuditSink? resourceEventAuditSink = null)
        where TOperation : IOperation
    {
        var app = new IntegrationTestBlueprintApp();
        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionServiceProvider(expander, resourceEventAuditSink);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var context = new ApiOperationContext(serviceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None)
        {
            SkipAuthorisation = true,
            HttpContext = httpContext,
        };

        return (context, executor.GetPipeline(typeof(TOperation)), expander);
    }

    private static DefaultHttpContext HttpContextWithExpand(string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString($"?expand={path}");

        return httpContext;
    }

    private sealed class ExpansionServiceProvider(IResourceExpander expander, IResourceEventAuditSink? resourceEventAuditSink) : IServiceProvider
    {
        public BlueprintAppExecutor? Executor { get; set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IResourceExpander>))
            {
                return new[] { expander };
            }

            if (serviceType == typeof(IResourceEventAuditSink))
            {
                return resourceEventAuditSink;
            }

            return serviceType == typeof(BlueprintAppExecutor) ? this.Executor : null;
        }
    }

    /// <summary>
    /// Records, for each event it is given, the name of the department its data holds at that moment: a copy, not the
    /// event, which the pipeline may still change after the sink has returned.
    /// </summary>
    private sealed class SnapshottingResourceEventAuditSink : IResourceEventAuditSink
    {
        public List<string> DepartmentNamesWhenRecorded { get; } = [];

        public Task RecordAsync(string eventId, string resourceId, OperationResult handlerResult, ApiOperationContext context, CancellationToken cancellationToken)
        {
            if (handlerResult is ResourceEvent<AlwaysExpandTestEmployee> resourceEvent)
            {
                this.DepartmentNamesWhenRecorded.Add(resourceEvent.Data?.Department.Value?.Name ?? "(not expanded)");
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>Returns a resource event whose inline data has an <c>[AlwaysExpand]</c> property.</summary>
public partial class UpdateAlwaysExpandEmployeeCommand : ICommand<ResourceEvent<AlwaysExpandTestEmployee>>
{
    public static ResourceEvent<AlwaysExpandTestEmployee> Handle(UpdateAlwaysExpandEmployeeCommand operation)
    {
        return new ResourceEvent<AlwaysExpandTestEmployee>(
            ResourceEventChangeType.Updated,
            "updated",
            new AlwaysExpandTestEmployee { Department = new Expandable<DepartmentResource>("dept-event") });
    }
}

/// <summary>Returns a resource event whose inline data has a property expanded only on request.</summary>
public partial class UpdateExpandableEmployeeCommand : ICommand<ResourceEvent<ExpandableTestEmployee>>
{
    public static ResourceEvent<ExpandableTestEmployee> Handle(UpdateExpandableEmployeeCommand operation)
    {
        return new ResourceEvent<ExpandableTestEmployee>(
            ResourceEventChangeType.Updated,
            "updated",
            new ExpandableTestEmployee { Department = new Expandable<DepartmentResource>("dept-on-request") });
    }
}

/// <summary>Hands on the event another command's pipeline returned, whose data that pipeline's self query loaded.</summary>
public partial class PassOnAlwaysExpandEmployeeEventCommand : ICommand<ResourceEvent<AlwaysExpandTestEmployee>>
{
    public static async Task<ResourceEvent<AlwaysExpandTestEmployee>> HandleAsync(PassOnAlwaysExpandEmployeeEventCommand operation, ApiOperationContext context)
    {
        // A nested operation is authorised by default and these tests have no user; expansion, not authorisation, is tested.
        var executor = (BlueprintAppExecutor)context.ServiceProvider.GetService(typeof(BlueprintAppExecutor))!;
        var inner = await executor.ExecuteAsync(context.CreateNested(new UpdateAlwaysExpandEmployeeViaSelfQueryCommand(), skipAuthorisation: true));

        return (ResourceEvent<AlwaysExpandTestEmployee>)inner;
    }
}

/// <summary>A query that hands on the event a command's pipeline returned, whose data that pipeline's self query loaded.</summary>
public partial class HandOnAlwaysExpandEmployeeEventQuery : IQuery<ResourceEvent<AlwaysExpandTestEmployee>>
{
    public static async Task<ResourceEvent<AlwaysExpandTestEmployee>> HandleAsync(HandOnAlwaysExpandEmployeeEventQuery operation, ApiOperationContext context)
    {
        // As PassOnAlwaysExpandEmployeeEventCommand: authorisation is not what is tested.
        var executor = (BlueprintAppExecutor)context.ServiceProvider.GetService(typeof(BlueprintAppExecutor))!;
        var inner = await executor.ExecuteAsync(context.CreateNested(new UpdateAlwaysExpandEmployeeViaSelfQueryCommand(), skipAuthorisation: true));

        return (ResourceEvent<AlwaysExpandTestEmployee>)inner;
    }
}

/// <summary>Returns an updated event from an operation that declares either a resource event or an updated one.</summary>
public partial class UpdateAlwaysExpandEmployeeAsEitherEventCommand
    : ICommand<Results<ResourceEvent<AlwaysExpandTestEmployee>, ResourceUpdated<AlwaysExpandTestEmployee>>>
{
    public static Results<ResourceEvent<AlwaysExpandTestEmployee>, ResourceUpdated<AlwaysExpandTestEmployee>> Handle(UpdateAlwaysExpandEmployeeAsEitherEventCommand operation)
    {
        return new ResourceUpdated<AlwaysExpandTestEmployee>(new AlwaysExpandTestEmployee { Department = new Expandable<DepartmentResource>("dept-either") });
    }
}

/// <summary>Returns a resource event whose data is loaded by its self-query, which expands it itself.</summary>
public partial class UpdateAlwaysExpandEmployeeViaSelfQueryCommand : ICommand<ResourceEvent<AlwaysExpandTestEmployee>>
{
    public static ResourceEvent<AlwaysExpandTestEmployee> Handle(UpdateAlwaysExpandEmployeeViaSelfQueryCommand operation)
    {
        return new ResourceEvent<AlwaysExpandTestEmployee>(
            ResourceEventChangeType.Updated,
            "updated",
            new GetAlwaysExpandResourceQuery { Id = "emp-self-query" });
    }
}
