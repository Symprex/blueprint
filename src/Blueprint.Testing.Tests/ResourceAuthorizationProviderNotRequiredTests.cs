using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.Testing.Tests;

/// <summary>
/// Proves that an app with no scope-pair operations still maps without an <c>IResourceAuthorizationProvider</c>.
/// </summary>
/// <remarks>
/// <c>MapBlueprintApi</c> refuses to map an app whose operations declare <c>[Resource]</c> scope pairs when no provider is
/// registered to judge them (Symprex/blueprint#41). An app that declares none, such as one that authorises through its own
/// attributes and <c>IApiAuthoriser</c>s, must not be made to register one. <see cref="RouteFormatTestApp"/> scans this
/// assembly, which declares no <c>[Resource]</c> operation.
/// </remarks>
public sealed class ResourceAuthorizationProviderNotRequiredTests
{
    [Fact]
    public async Task Mapping_an_app_with_no_scope_pair_operations_needs_no_provider()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddBlueprintApi<RouteFormatTestApp, RouteFormatTestAppExecutor>();

        await using var app = builder.Build();

        var map = () => app.MapBlueprintApi<RouteFormatTestApp, RouteFormatTestAppExecutor>("api/");

        map.Should().NotThrow();
    }
}
