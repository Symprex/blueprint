using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that a route-owned property whose route value is absent keeps the value the operation already has, whatever
/// its type, as legacy Blueprint kept it.
/// </summary>
/// <remarks>
/// Legacy (Signature365 <c>8b40ecc961</c>) set each route property to
/// <c>RouteData.Values.TryGetValue(name, out var v) ? convert(v) : operation.Property</c>
/// (<c>Blueprint.Http/MessagePopulation/HttpRouteMessagePopulationSource.cs:89-107</c>). An operation executed in process
/// was given empty route data (<c>Blueprint.Testing/ApiOperationContextSetup.cs:51-54</c>), so it kept the values it was
/// constructed with. Signature365's <c>ApiFixture</c> and CLI execute operations that way.
/// </remarks>
public class AbsentRouteValueIntegrationTests
{
    private static readonly Guid _widgetId = Guid.Parse("7d9f2c6e-3b1a-4f5e-9c8d-2a4b6c8e0f13");

    [Fact]
    public async Task When_typed_route_values_are_absent_then_the_operations_own_values_are_kept()
    {
        var result = await ExecuteAsync(new TypedRouteValuesQuery { Number = 42, WidgetId = _widgetId, Name = "own" }, new DefaultHttpContext());

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be($"42:{_widgetId}:own");
    }

    [Fact]
    public async Task When_typed_route_values_are_present_then_they_are_bound()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["Number"] = "7";
        httpContext.Request.RouteValues["WidgetId"] = "0b3c5e7a-9d1f-4a2b-8c6d-4e5f6a7b8c9d";
        httpContext.Request.RouteValues["Name"] = "routed";

        var result = await ExecuteAsync(new TypedRouteValuesQuery { Number = 42, WidgetId = _widgetId, Name = "own" }, httpContext);

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("7:0b3c5e7a-9d1f-4a2b-8c6d-4e5f6a7b8c9d:routed");
    }

    [Fact]
    public async Task When_a_typed_route_value_is_present_but_unparseable_then_binding_fails()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["Number"] = "not-a-number";
        httpContext.Request.RouteValues["WidgetId"] = _widgetId.ToString();
        httpContext.Request.RouteValues["Name"] = "routed";

        var result = await ExecuteAsync(new TypedRouteValuesQuery { Number = 42, WidgetId = _widgetId, Name = "own" }, httpContext);

        result.Should().BeOfType<BindingFailureResult>();
    }

    private static async Task<OperationResult> ExecuteAsync(TypedRouteValuesQuery operation, HttpContext httpContext)
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var pipeline = executor.GetPipeline(typeof(TypedRouteValuesQuery));
        var context = new ApiOperationContext(new ServiceCollection().BuildServiceProvider(), app, TypedRouteValuesQuery.Configure(), CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };

        return await pipeline.ExecuteAsync(context);
    }
}

/// <summary>A query whose route carries an int, a Guid and a string.</summary>
[AllowAnonymous]
[RootLink("/absent-route-values/{Number}/{WidgetId}/{Name}")]
public partial class TypedRouteValuesQuery : IQuery<string>
{
    public int Number { get; set; }

    public Guid WidgetId { get; set; }

    public string Name { get; set; } = string.Empty;

    public static string Handle(TypedRouteValuesQuery operation)
    {
        return $"{operation.Number}:{operation.WidgetId}:{operation.Name}";
    }
}
