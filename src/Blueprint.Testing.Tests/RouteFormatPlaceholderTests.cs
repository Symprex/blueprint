using System.Text.Json.Serialization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that <see cref="BlueprintTestHost{TApp,TExec}"/>'s route-value population recognises a legacy
/// <c>{Name(format)}</c> placeholder as route-owned, as <c>RoutePlaceholderSyntax</c> (R97) does (R111).
/// </summary>
/// <remarks>
/// The bound name is whatever is before a <c>:</c> (alternate resource property) or <c>(</c> (legacy format specifier).
/// <c>PopulateRouteValues</c>'s own regex only stripped <c>:</c>, so <c>"Id(N)"</c> was taken whole as the property name,
/// matched no property on the operation, and no route value was ever set for <c>Id</c> — the generated pipeline would
/// then read the operation's own default for it, as it does for any route value ASP.NET routing never supplied.
/// </remarks>
public sealed class RouteFormatPlaceholderTests : IDisposable
{
    private readonly BlueprintTestHost<RouteFormatTestApp, RouteFormatTestAppExecutor> _host;

    public RouteFormatPlaceholderTests()
    {
        this._host = BlueprintTestHostBuilder<RouteFormatTestApp, RouteFormatTestAppExecutor>.Create().Build();
    }

    [Fact]
    public async Task A_route_placeholder_with_a_legacy_format_specifier_is_set_in_RouteValues()
    {
        Microsoft.AspNetCore.Routing.RouteValueDictionary? routeValues = null;

        await this._host.ExecuteAsync(
            new GetFormattedWidgetQuery { Id = "widget-1" },
            ctx =>
            {
                ctx.SkipAuthorisation();
                routeValues = ctx.OperationContext.HttpContext!.Request.RouteValues;
            });

        routeValues.Should().NotBeNull();
        routeValues!.ContainsKey("Id").Should().BeTrue();
        routeValues["Id"].Should().Be("widget-1");
    }

    public void Dispose()
    {
        this._host.Dispose();
    }
}

/// <summary>A self-scanning app for this file's fixture operation alone.</summary>
[BlueprintAppScan]
[BlueprintAppSerializerContext(JsonSerializerContextType = typeof(RouteFormatTestAppJsonContext))]
public partial class RouteFormatTestApp : BlueprintApp;

/// <summary>The serializer context this app's HTTP surface needs.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
public partial class RouteFormatTestAppJsonContext : JsonSerializerContext;

/// <summary>An operation whose only URL uses a legacy <c>(format)</c> placeholder, as <c>ApiOperationLink.cs:29</c> at
/// Signature365 <c>8b40ecc961</c> parsed one.</summary>
[RootLink("/route-format/{Id(N)}")]
[AllowAnonymous]
[Blueprint.Auditing.DoNotAudit]
public partial class GetFormattedWidgetQuery : IQuery<string>
{
    public string Id { get; set; } = string.Empty;

    public static string Handle(GetFormattedWidgetQuery query)
    {
        return query.Id;
    }
}
