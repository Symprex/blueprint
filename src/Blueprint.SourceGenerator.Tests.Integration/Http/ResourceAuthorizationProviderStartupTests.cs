using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>
/// Proves that an app whose operations declare scope pairs cannot be mapped without an
/// <see cref="IResourceAuthorizationProvider"/> to judge them.
/// </summary>
/// <remarks>
/// With none registered, the generated scope check used to pass every authenticated caller (Symprex/blueprint#41). The
/// run-time check now refuses instead, and <c>MapBlueprintApi</c> stops the app starting, so the omission is found at
/// start-up rather than as a 403 on every such operation. The integration app declares
/// <see cref="UpdateTenantAuthorizationCommand"/> with <c>[Resource]</c>.
/// </remarks>
public sealed class ResourceAuthorizationProviderStartupTests
{
    [Fact]
    public async Task Mapping_an_app_with_scope_pair_operations_and_no_provider_throws_naming_the_operations_and_the_fix()
    {
        await using var app = BuildApp(configure: null);

        var map = () => app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        // The message names the app, the operations (the first ten alphabetically, then a count), and the fix.
        map.Should().Throw<InvalidOperationException>()
            .Which.Message.Should()
            .StartWith($"{nameof(IntegrationTestBlueprintApp)} has operations that declare [Resource] scope pairs (")
            .And.MatchRegex(@"\(\w+(, \w+)* and \d+ more\)")
            .And.Contain(nameof(IResourceAuthorizationProvider))
            .And.Contain("AddResourceAuthorizationProvider");
    }

    [Fact]
    public async Task Mapping_an_app_with_scope_pair_operations_and_a_provider_succeeds()
    {
        await using var app = BuildApp(blueprint => blueprint.AddResourceAuthorizationProvider<DenyingResourceAuthorizationProvider>());

        var map = () => app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        map.Should().NotThrow();
    }

    private static WebApplication BuildApp(Action<BlueprintServiceBuilder>? configure)
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Logging.ClearProviders();
        builder.Services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(configure);

        return builder.Build();
    }

    private sealed class DenyingResourceAuthorizationProvider : IResourceAuthorizationProvider
    {
        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }
}
