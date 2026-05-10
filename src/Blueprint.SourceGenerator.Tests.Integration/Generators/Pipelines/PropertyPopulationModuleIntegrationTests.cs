using System.Text;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class PropertyPopulationModuleIntegrationTests
{
    [Fact]
    public async Task When_property_population_reads_query_string_then_value_is_bound()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(QueryStringPopulationQuery));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?searchTerm=blueprint");
        var context = CreateContext(app, QueryStringPopulationQuery.Configure(), new QueryStringPopulationQuery(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("blueprint");
    }

    [Fact]
    public async Task When_property_population_reads_complex_body_then_payload_is_deserialized()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(BodyPopulationCommand));
        var json = "{\"payload\":{\"Name\":\"tenant\",\"Count\":3}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Body = new MemoryStream(bytes);
        var context = CreateContext(app, BodyPopulationCommand.Configure(), new BodyPopulationCommand(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("tenant:3");
    }

    [Fact]
    public async Task When_property_population_reads_route_values_then_value_is_bound()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(RoutePopulationQuery));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["ItemId"] = "item-42";
        var context = CreateContext(app, RoutePopulationQuery.Configure(), new RoutePopulationQuery(), httpContext);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("item-42");
    }

    private static ApiOperationContext CreateContext(BlueprintApp app, ApiOperationDescriptor descriptor, object operation, HttpContext httpContext)
    {
        return new ApiOperationContext(new ServiceCollection().BuildServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };
    }
}

[AllowAnonymous]
[RootLink("/population/query")]
public partial class QueryStringPopulationQuery : IQuery<string>
{
    [FromQuery]
    public string SearchTerm { get; set; } = string.Empty;

    public static Task<string> HandleAsync(QueryStringPopulationQuery operation)
    {
        return Task.FromResult(operation.SearchTerm);
    }
}

[AllowAnonymous]
[RootLink("/population/body")]
public partial class BodyPopulationCommand : ICommand<string>
{
    [FromBody]
    public BodyPopulationPayload? Payload { get; set; }

    public static Task<string> HandleAsync(BodyPopulationCommand operation)
    {
        return Task.FromResult($"{operation.Payload!.Name}:{operation.Payload.Count}");
    }
}

[AllowAnonymous]
[RootLink("/population/items/{ItemId}")]
public partial class RoutePopulationQuery : IQuery<string>
{
    [FromRoute]
    public string ItemId { get; set; } = string.Empty;

    public static Task<string> HandleAsync(RoutePopulationQuery operation)
    {
        return Task.FromResult(operation.ItemId);
    }
}

public sealed class BodyPopulationPayload
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}
