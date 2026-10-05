using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;
using Blueprint.SourceGenerator.Tests.Integration.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that whether a link is shown is decided for each resource, as legacy Blueprint decided it, however many
/// resources of one request link to the same operation.
/// </summary>
/// <remarks>
/// Legacy's <c>ApiAuthoriserAggregator.CanShowLinkAsync</c> asked every applicable <c>IApiAuthoriser</c>, on every call,
/// with the resource it was given, and kept nothing between calls (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint/Authorisation/ApiAuthoriserAggregator.cs:22-62</c>). Signature365's own authorisers decide by the resource:
/// <c>S365ClaimsApiAuthoriser</c> and <c>RegenerateEntraIdClientSecretCommandLinkChecker</c>. So a list whose items the
/// caller may act on differently showed each item's link as that item's check said.
/// </remarks>
public sealed class LinkVisibilityPerResourceTests
{
    [Theory]
    [InlineData("allowed,denied,allowed", "True,False,True")]
    [InlineData("denied,allowed,denied", "False,True,False")]
    public async Task Each_resource_of_a_list_is_decided_by_its_own_check(string resources, string expected)
    {
        var results = await CheckEachAsync(resources, services => services.AddSingleton<IApiAuthoriser, ByResourceApiAuthoriser>());

        results.Should().Equal(expected.Split(',').Select(bool.Parse));
    }

    [Theory]
    [InlineData("allowed,denied,allowed", "True,False,True")]
    [InlineData("denied,allowed,denied", "False,True,False")]
    public async Task Each_resource_is_decided_by_its_own_state_based_check_after_the_permission_check_allows_it(string resources, string expected)
    {
        // A permission check that allows every resource, and a state-based checker that decides by the resource.
        var results = await CheckEachAsync(resources, services =>
        {
            services.AddSingleton<IApiAuthoriser, AllowingApiAuthoriser>();
            services.AddSingleton<ILinkVisibilityAuthoriser, ByResourceLinkVisibilityAuthoriser>();
        });

        results.Should().Equal(expected.Split(',').Select(bool.Parse));
    }

    [Fact]
    public async Task A_link_checker_one_container_registers_is_asked_after_another_container_checked_the_same_operation()
    {
        // Two containers in one process, as two apps or two test hosts are: what applies in the first says nothing about
        // the second. The operation is one no other test checks a link of.
        var descriptor = GetFormLevelValidationQuery.Configure();

        var withoutChecker = await CheckEachAsync("denied", services => services.AddSingleton<IApiAuthoriser, AllowingApiAuthoriser>(), descriptor);
        var withChecker = await CheckEachAsync("denied", services => services.AddSingleton<ILinkVisibilityAuthoriser, ByResourceLinkVisibilityAuthoriser>(), descriptor);

        withoutChecker.Should().Equal(true);
        withChecker.Should().Equal(false);
    }

    private static async Task<List<bool>> CheckEachAsync(string resources, Action<IServiceCollection> configure, ApiOperationDescriptor? descriptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IApiAuthoriserAggregator, ApiAuthoriserAggregator>();
        configure(services);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // One request: one scope, one aggregator, every resource of the response.
        var aggregator = scope.ServiceProvider.GetRequiredService<IApiAuthoriserAggregator>();
        var app = new IntegrationTestBlueprintApp();
        descriptor ??= GetLinkTestResourceQuery.Configure();
        var context = new ApiOperationContext(scope.ServiceProvider, app, descriptor, CancellationToken.None);

        var results = new List<bool>();

        foreach (var name in resources.Split(','))
        {
            var result = await aggregator.CanShowLinkAsync(context, descriptor, new LinkTestApiResource { Id = name, Name = name });
            results.Add(result.IsAllowed);
        }

        return results;
    }

    /// <summary>Allows the link of a resource whose name is <c>allowed</c>, as a Signature365 authoriser decides by the resource.</summary>
    private sealed class ByResourceApiAuthoriser : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ExecutionAllowed.YesTask;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ((LinkTestApiResource)resource).Name == "allowed"
                ? ExecutionAllowed.YesTask
                : Task.FromResult(ExecutionAllowed.No("not this one", "You may not act on this resource", ExecutionAllowedFailureType.Authorisation));
        }
    }

    /// <summary>A permission check that allows every link.</summary>
    private sealed class AllowingApiAuthoriser : IApiAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return ExecutionAllowed.YesTask;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    /// <summary>A state-based link checker that shows the link of a resource whose name is <c>allowed</c>.</summary>
    private sealed class ByResourceLinkVisibilityAuthoriser : ILinkVisibilityAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ((LinkTestApiResource)resource).Name == "allowed"
                ? ExecutionAllowed.YesTask
                : Task.FromResult(ExecutionAllowed.No("not in this state", "Not available", ExecutionAllowedFailureType.Authorisation));
        }
    }
}
