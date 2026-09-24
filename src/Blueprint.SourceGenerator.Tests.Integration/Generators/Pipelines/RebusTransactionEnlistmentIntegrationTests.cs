using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Persistence;
using Blueprint.Rebus;
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
/// Proves that a generated pipeline which saves a unit of work calls the consumer's
/// <see cref="IRebusTransactionEnlistment"/>s with its Rebus transaction, once the tenant is resolved and the operation
/// authorised and validated but before the handler runs, and saves the unit of work before any commit callback of that
/// transaction — the order an outbox enlisted in the unit of work needs.
/// </summary>
public class RebusTransactionEnlistmentIntegrationTests
{
    [Fact]
    public async Task When_command_saves_a_unit_of_work_then_its_rebus_transaction_is_enlisted_before_the_handler_runs()
    {
        await using var provider = BuildProvider(registerUnitOfWork: true);
        var (context, pipeline, log) = Create<EnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("enlist", "handler", "save", "commit");
        log.EnlistedTransaction.Should().NotBeNull()
            .And.BeSameAs(log.HandlerAmbientTransaction, "the transaction enlisted is the one the handler's messages go into");
    }

    [Fact]
    public async Task When_command_does_not_save_a_unit_of_work_then_its_transaction_is_not_enlisted()
    {
        await using var provider = BuildProvider(registerUnitOfWork: true);
        var (context, pipeline, log) = Create<NotSavingEnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("handler");
    }

    [Fact]
    public async Task When_no_unit_of_work_is_registered_then_the_transaction_is_not_enlisted()
    {
        // Nothing would save the outbox, so its messages would never be written.
        await using var provider = BuildProvider(registerUnitOfWork: false);
        var (context, pipeline, log) = Create<EnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("handler");
    }

    [Fact]
    public async Task When_the_enlistment_resolves_a_tenant_scoped_dependency_then_it_sees_the_resolved_tenant()
    {
        // Legacy enlisted its Marten session as the outbox only once SetTenantMiddleware had set the tenant
        // (legacy AzureAdOauthReturnCommandExecutorPipeline.cs:61-66 before :119-121). A consumer's enlistment that
        // constructor-injects a tenant-scoped session must get the caller's tenant, not an untenanted session the handler
        // then shares.
        await using var provider = BuildProvider(registerUnitOfWork: true, registerTenancy: true);
        var (context, pipeline, log) = Create<EnlistedCommand>(provider, CreatePrincipal("tenant-42"));

        var result = await pipeline.ExecuteAsync(context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal("enlist", "handler", "save", "commit");
        log.EnlistedTenant.Should().Be("tenant-42");
    }

    [Fact]
    public async Task When_the_operation_fails_validation_then_its_transaction_is_not_enlisted()
    {
        // Legacy returned the validation failure (legacy AzureAdOauthReturnCommandExecutorPipeline.cs:109-114) before
        // it opened the scope and enlisted the outbox (:116-121).
        await using var provider = BuildProvider(registerUnitOfWork: true);
        var (context, pipeline, log) = Create<ValidatedEnlistedCommand>(provider);

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_operation_is_not_authorised_then_its_transaction_is_not_enlisted()
    {
        // Legacy's authorisers ran, and threw on a denial (legacy AzureAdOauthReturnCommandExecutorPipeline.cs:68-89),
        // before it opened the scope and enlisted the outbox (:116-121).
        await using var provider = BuildProvider(registerUnitOfWork: true, registerDenyingAuthoriser: true);
        var (context, pipeline, log) = Create<AuthorisedEnlistedCommand>(provider, CreatePrincipal("tenant-42"));

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ForbiddenResult>();
        log.Entries.Should().BeEmpty();
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline, EnlistmentLog Log) Create<TCommand>(
        ServiceProvider provider,
        ClaimsPrincipal? principal = null)
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, provider);
        var scope = provider.CreateScope();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(TCommand)), CancellationToken.None);

        if (principal is not null)
        {
            context.UserPrincipal = principal;
            context.HttpContext = new DefaultHttpContext { User = principal };
        }

        return (context, executor.GetPipeline(typeof(TCommand)), scope.ServiceProvider.GetRequiredService<EnlistmentLog>());
    }

    private static ServiceProvider BuildProvider(bool registerUnitOfWork, bool registerTenancy = false, bool registerDenyingAuthoriser = false)
    {
        var services = new ServiceCollection();

        // The pipeline opens its Rebus scope only when an IBus is registered.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<EnlistmentLog>();
        services.AddScoped<TenantScopedSession>();
        services.AddScoped<IRebusTransactionEnlistment, LoggingEnlistment>();

        if (registerUnitOfWork)
        {
            services.AddScoped<IUnitOfWork, LoggingUnitOfWork>();
        }

        if (registerTenancy)
        {
            // Root singletons, as the pipeline shell resolves them from the root provider.
            services.AddSingleton<ITenantAccessor, TestTenantAccessor>();
            services.AddSingleton<ITenantResolver, AccountIdTenantResolver>();
        }

        if (registerDenyingAuthoriser)
        {
            services.AddSingleton<IOperationAuthoriser, DenyingOperationAuthoriser>();
        }

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(string accountId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim("AccountId", accountId)], "EnlistmentTestAuth"));
    }

    private sealed class LoggingEnlistment(EnlistmentLog log, TenantScopedSession session) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            log.Entries.Add("enlist");
            log.EnlistedTransaction = transactionContext;
            log.EnlistedTenant = session.Tenant;

            // Stands in for the transport's send callback and a consumer's outbox check alike.
            transactionContext.OnCommit(_ =>
            {
                log.Entries.Add("commit");

                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingUnitOfWork(EnlistmentLog log) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            log.Entries.Add("save");

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for S365's Marten session, whose factory reads the current tenant once, when the session is created.
    /// </summary>
    private sealed class TenantScopedSession(IServiceProvider services)
    {
        public string? Tenant { get; } = services.GetService<ITenantAccessor>()?.Current;
    }

    private sealed class TestTenantAccessor : ITenantAccessor
    {
        public string? Current { get; set; }
    }

    private sealed class AccountIdTenantResolver : ITenantResolver
    {
        public string? ResolveTenantId(ClaimsPrincipal principal)
        {
            return principal.FindFirst("AccountId")?.Value;
        }
    }

    private sealed class DenyingOperationAuthoriser : IOperationAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType == typeof(AuthorisedEnlistedCommand);
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            return Task.FromResult(ExecutionAllowed.No("Denied by the test", "denied", ExecutionAllowedFailureType.Authorisation));
        }
    }
}

/// <summary>Records what the enlistment, the handler and the unit of work saw, in order.</summary>
public sealed class EnlistmentLog
{
    public List<string> Entries { get; } = [];

    public ITransactionContext? EnlistedTransaction { get; set; }

    public string? EnlistedTenant { get; set; }

    public ITransactionContext? HandlerAmbientTransaction { get; set; }
}

/// <summary>A command whose pipeline saves the unit of work.</summary>
public partial class EnlistedCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(EnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");
        log.HandlerAmbientTransaction = AmbientTransactionContext.Current;

        return Task.CompletedTask;
    }
}

/// <summary>A command whose pipeline does not save the unit of work.</summary>
[DoNotSaveChanges]
public partial class NotSavingEnlistedCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(NotSavingEnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");

        return Task.CompletedTask;
    }
}

/// <summary>A command whose pipeline saves the unit of work, and fails validation when it has no name.</summary>
public partial class ValidatedEnlistedCommand : Blueprint.Model.ICommand
{
    [Required]
    public string? Name { get; set; }

    public static Task HandleAsync(ValidatedEnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");

        return Task.CompletedTask;
    }
}

/// <summary>A command whose pipeline saves the unit of work, exposed over HTTP so that its authorisers run.</summary>
[RootLink("/enlisted-authorised-commands")]
public partial class AuthorisedEnlistedCommand : Blueprint.Model.ICommand
{
    public static Task HandleAsync(AuthorisedEnlistedCommand operation, EnlistmentLog log)
    {
        log.Entries.Add("handler");

        return Task.CompletedTask;
    }
}
