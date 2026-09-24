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

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline, TestDepartmentExpander Expander) Create<TOperation>(
        HttpContext httpContext)
        where TOperation : IOperation
    {
        var app = new IntegrationTestBlueprintApp();
        var expander = new TestDepartmentExpander();
        var serviceProvider = new ExpansionServiceProvider(expander);
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        serviceProvider.Executor = executor;

        var context = new ApiOperationContext(serviceProvider, app, app.FindOperation(typeof(TOperation)), CancellationToken.None)
        {
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

    private sealed class ExpansionServiceProvider(IResourceExpander expander) : IServiceProvider
    {
        public BlueprintAppExecutor? Executor { get; set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IResourceExpander>))
            {
                return new[] { expander };
            }

            return serviceType == typeof(BlueprintAppExecutor) ? this.Executor : null;
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
