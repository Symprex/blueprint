using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves where a generated pipeline opens its Rebus transaction scope: after the tenant is resolved and the operation
/// authorised, as legacy opened it, so a message sent from those steps is sent at once rather than held in the scope, and
/// before the handler, which runs inside it.
/// </summary>
/// <remarks>
/// Legacy set the tenant (<c>AzureAdOauthReturnCommandExecutorPipeline.cs:61-66</c> at Signature365 <c>8b40ecc961</c>),
/// authorised (<c>:68-89</c>) and validated (<c>:91-114</c>) before <c>StartRebusTransactionScopeMiddleware</c> opened the
/// scope (<c>:116-117</c>). Nothing ambient during those steps means a send from them left immediately, and was not
/// dropped when a later step refused the operation.
/// </remarks>
public class RebusTransactionScopeStartIntegrationTests
{
    [Fact]
    public async Task Tenant_resolution_and_authorisation_run_outside_the_scope_and_the_handler_inside_it()
    {
        var observations = new ScopeObservations();
        await using var provider = BuildProvider(observations);
        using var scope = provider.CreateScope();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, provider);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("AccountId", "tenant-42")], "ScopeStartTestAuth"));
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(ScopeStartCommand)), CancellationToken.None)
        {
            UserPrincipal = principal,
            HttpContext = new DefaultHttpContext { User = principal },
        };

        var result = await executor.GetPipeline(typeof(ScopeStartCommand)).ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        observations.Steps.Should().Equal("tenant resolution", "authorisation", "handler");
        observations.AmbientDuringTenantResolution.Should().BeNull("legacy set the tenant before it opened the scope");
        observations.AmbientDuringAuthorisation.Should().BeNull("legacy authorised before it opened the scope");
        observations.AmbientDuringHandler.Should().NotBeNull("the handler runs inside the pipeline's scope");
    }

    private static ServiceProvider BuildProvider(ScopeObservations observations)
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddSingleton(observations);

        // Root singletons, as the pipeline shell resolves them from the root provider.
        services.AddSingleton<ITenantAccessor, TestTenantAccessor>();
        services.AddSingleton<ITenantResolver, ObservingTenantResolver>();
        services.AddSingleton<IOperationAuthoriser, ObservingOperationAuthoriser>();

        return services.BuildServiceProvider();
    }

    private sealed class TestTenantAccessor : ITenantAccessor
    {
        public string? Current { get; set; }
    }

    private sealed class ObservingTenantResolver(ScopeObservations observations) : ITenantResolver
    {
        public string? ResolveTenantId(ClaimsPrincipal principal)
        {
            observations.Steps.Add("tenant resolution");
            observations.AmbientDuringTenantResolution = AmbientTransactionContext.Current;

            return principal.FindFirst("AccountId")?.Value;
        }
    }

    private sealed class ObservingOperationAuthoriser(ScopeObservations observations) : IOperationAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(ScopeStartCommand);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            observations.Steps.Add("authorisation");
            observations.AmbientDuringAuthorisation = AmbientTransactionContext.Current;

            return Task.FromResult(ExecutionAllowed.Yes);
        }
    }
}

/// <summary>Records which Rebus transaction, if any, was ambient during each step of a pipeline.</summary>
public sealed class ScopeObservations
{
    public List<string> Steps { get; } = [];

    public ITransactionContext? AmbientDuringTenantResolution { get; set; }

    public ITransactionContext? AmbientDuringAuthorisation { get; set; }

    public ITransactionContext? AmbientDuringHandler { get; set; }
}

/// <summary>A command exposed over HTTP, so that its authorisers run.</summary>
[RootLink("/scope-start-commands")]
public partial class ScopeStartCommand : ICommand
{
    public static Task HandleAsync(ScopeStartCommand operation, ScopeObservations observations)
    {
        observations.Steps.Add("handler");
        observations.AmbientDuringHandler = AmbientTransactionContext.Current;

        return Task.CompletedTask;
    }
}
