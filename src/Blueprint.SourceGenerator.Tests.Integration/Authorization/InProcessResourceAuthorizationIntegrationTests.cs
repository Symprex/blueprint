using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that a permissioned operation (one with <c>[Resource]</c>) executed in process, with no <c>HttpContext</c>, is
/// authorised by the registered <see cref="IResourceAuthorizationProvider"/>s as it is over HTTP: the unlinked path, a
/// Rebus message and a nested execution each refuse a caller the providers deny and allow one they grant, unless the
/// context skips authorisation or the operation's descriptor allows anonymous access (Symprex/blueprint#78, D4).
/// </summary>
/// <remarks>
/// The check takes its principal from <see cref="ApiOperationContext.UserPrincipal"/> and is skipped exactly as the
/// authoriser step is, by <see cref="ApiOperationContext.SkipAuthorisation"/> and the descriptor's
/// <see cref="ApiOperationDescriptor.AnonymousAccessAllowed"/>. A subscribed Rebus message's descriptor allows anonymous
/// access from start-up, so a delivered message is not asked of the providers, as it is not asked of the authorisers.
/// </remarks>
public sealed class InProcessResourceAuthorizationIntegrationTests
{
    private const string UserId = "user-7";

    private const string AccountId = "account-9";

    [Fact]
    public async Task An_unlinked_permissioned_operation_a_provider_denies_is_forbidden_with_the_providers_message()
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("internal reason", "You may not read this widget", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(new PermissionedUnlinkedCommand(), services => services.AddSingleton<IResourceAuthorizationProvider>(provider), AuthenticatedUser());

        result.Should().BeOfType<ForbiddenResult>().Which.Failure!.Value.Message.Should().Be("You may not read this widget");
        provider.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_a_provider_denies_as_unauthenticated_is_unauthenticated()
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("deactivated", "Your account has been deactivated", ExecutionAllowedFailureType.Authentication));

        var result = await ExecuteAsync(new PermissionedUnlinkedCommand(), services => services.AddSingleton<IResourceAuthorizationProvider>(provider), AuthenticatedUser());

        result.Should().BeOfType<UnauthenticatedResult>();
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_a_provider_grants_runs_and_the_provider_is_asked_about_the_context_principal()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);
        var user = AuthenticatedUser();

        var result = await ExecuteAsync(new PermissionedUnlinkedCommand(), services => services.AddSingleton<IResourceAuthorizationProvider>(provider), user);

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("executed");
        provider.Requests.Should().ContainSingle().Which.Should().Match<ResourceAuthorizationRequest>(static r =>
            r.Mode == ResourceAuthorizationMode.Execute
            && r.Resource == null
            && r.ScopePairs.Count == 1
            && r.ScopePairs[0].Noun == "widget"
            && r.ScopePairs[0].Verb == "read");
        provider.Principals.Should().ContainSingle().Which.Should().BeSameAs(user);
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_with_no_provider_registered_is_forbidden()
    {
        // Fail closed, as over HTTP: with scope pairs and nothing to judge them, the operation is refused (Symprex/blueprint#41).
        var result = await ExecuteAsync(new PermissionedUnlinkedCommand(), static _ => { }, AuthenticatedUser());

        result.Should().BeSameAs(ForbiddenResult.Instance);
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_is_refused_by_the_second_provider_when_the_first_allows()
    {
        var first = new RecordingProvider(ExecutionAllowed.Yes);
        var second = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new PermissionedUnlinkedCommand(),
            services => services.AddSingleton<IResourceAuthorizationProvider>(first).AddSingleton<IResourceAuthorizationProvider>(second),
            AuthenticatedUser());

        result.Should().BeOfType<ForbiddenResult>();
        first.Requests.Should().HaveCount(1);
        second.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_executed_with_no_user_is_refused_as_an_anonymous_caller_without_asking_a_provider()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await ExecuteAsync(new PermissionedUnlinkedCommand(), services => services.AddSingleton<IResourceAuthorizationProvider>(provider));

        result.Should().BeSameAs(UnauthenticatedResult.Instance);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unlinked_permissioned_operation_executed_by_an_unauthenticated_principal_is_refused_as_an_anonymous_caller()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await ExecuteAsync(
            new PermissionedUnlinkedCommand(),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId)])));

        result.Should().BeSameAs(UnauthenticatedResult.Instance);
        provider.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unlinked_permissioned_operation_that_skips_authorisation_is_not_asked_of_the_providers(bool withUser)
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new PermissionedUnlinkedCommand(),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            withUser ? AuthenticatedUser() : null,
            configure: static context => context.SkipAuthorisation = true);

        result.Should().BeOfType<OkResult<string>>();
        provider.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Over_HTTP_the_shared_check_asks_about_the_requests_user_unless_the_context_skips_authorisation(bool skip, int expectedRequests)
    {
        // Every permissioned pipeline, HTTP included, calls this one check. A context skipping authorisation, as a resource
        // event's self query does, skips the providers as it skips the authentication step and the authorisers: as
        // legacy Blueprint skipped every authoriser, Signature 365's resource check among them
        var provider = new RecordingProvider(ExecutionAllowed.Yes);
        var app = new IntegrationTestBlueprintApp();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = AuthenticatedUser() };
        var context = new ApiOperationContext(new ServiceCollection().BuildServiceProvider(), app, app.FindOperation(typeof(PermissionedUnlinkedCommand))!, httpContext.RequestAborted)
        {
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
            SkipAuthorisation = skip,
        };

        var denial = await PipelineHelpers.CheckOperationResourceAuthorizationAsync(context, [provider], new ResolvedScopePair("widget", "read", null, null));

        denial.Should().BeNull();
        provider.Requests.Should().HaveCount(expectedRequests);
        provider.Principals.Where(p => !ReferenceEquals(p, httpContext.User)).Should().BeEmpty();
    }

    [Fact]
    public async Task Over_HTTP_a_descriptor_that_allows_anonymous_access_is_still_asked_of_the_providers()
    {
        // A Rebus consumer marks a subscribed operation's descriptor anonymous, but MapBlueprintApi still routes the operation
        // when it has a URL: a request to it must not skip the providers that would deny it
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));
        var app = new IntegrationTestBlueprintApp();
        var descriptor = app.FindOperation(typeof(PermissionedUnlinkedCommand))!;
        descriptor.AnonymousAccessAllowed = true;
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = AuthenticatedUser() };
        var context = new ApiOperationContext(new ServiceCollection().BuildServiceProvider(), app, descriptor, httpContext.RequestAborted)
        {
            HttpContext = httpContext,
            UserPrincipal = httpContext.User,
        };

        var denial = await PipelineHelpers.CheckOperationResourceAuthorizationAsync(context, [provider], new ResolvedScopePair("widget", "read", null, null));

        denial.Should().BeOfType<ForbiddenResult>();
        provider.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unlinked_permissioned_operation_whose_descriptor_allows_anonymous_access_is_not_asked_of_the_providers(bool withUser)
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new PermissionedUnlinkedCommand(),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            withUser ? AuthenticatedUser() : null,
            configureApp: app => app.FindOperation(typeof(PermissionedUnlinkedCommand))!.AnonymousAccessAllowed = true);

        result.Should().BeOfType<OkResult<string>>();
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unlinked_operation_with_no_resource_declaration_is_not_asked_of_the_providers()
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IResourceAuthorizationProvider>(provider), AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>();
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unlinked_operations_resource_key_template_is_expanded_from_the_operation_and_the_principal()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        await ExecuteAsync(
            new PermissionedUnlinkedTemplateCommand { Id = "widget-3" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            AuthenticatedUser());

        provider.Requests.Should().ContainSingle().Which.ScopePairs.Should().ContainSingle().Which.Should().Be(
            new ResolvedScopePair("widget", "read", "/accounts/{Auth.AccountId}/widgets/{Id}", "/accounts/account-9/widgets/widget-3"));
    }

    [Fact]
    public async Task An_unlinked_operation_with_a_resource_key_template_executed_with_no_user_is_refused_rather_than_failing()
    {
        // The template reads the principal's claims: with no principal it must expand safely, then be refused.
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await ExecuteAsync(
            new PermissionedUnlinkedTemplateCommand { Id = "widget-3" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider));

        result.Should().BeSameAs(UnauthenticatedResult.Instance);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_nested_permissioned_operation_is_denied_or_allowed_with_the_principal_it_inherits()
    {
        var refusing = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));
        await using var refusingProvider = BuildAppProvider(services => services.AddSingleton<IResourceAuthorizationProvider>(refusing));
        var user = AuthenticatedUser();
        var (refusedParent, refusingExecutor) = CreateParentContext(refusingProvider, user);

        var refused = await refusingExecutor.ExecuteAsync(refusedParent.CreateNested(new PermissionedUnlinkedCommand()));

        refused.Should().BeOfType<ForbiddenResult>();
        refusing.Principals.Should().ContainSingle().Which.Should().BeSameAs(user, "the nested context inherits the parent's principal");

        var granting = new RecordingProvider(ExecutionAllowed.Yes);
        await using var grantingProvider = BuildAppProvider(services => services.AddSingleton<IResourceAuthorizationProvider>(granting));
        var (grantedParent, grantingExecutor) = CreateParentContext(grantingProvider, user);

        var granted = await grantingExecutor.ExecuteAsync(grantedParent.CreateNested(new PermissionedUnlinkedCommand()));

        granted.Should().BeOfType<OkResult<string>>();
        granting.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_nested_permissioned_operation_created_from_an_anonymous_context_is_refused_as_an_anonymous_caller()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);
        await using var services = BuildAppProvider(s => s.AddSingleton<IResourceAuthorizationProvider>(provider));
        var (parent, executor) = CreateParentContext(services, user: null);

        var result = await executor.ExecuteAsync(parent.CreateNested(new PermissionedUnlinkedCommand()));

        result.Should().BeSameAs(UnauthenticatedResult.Instance);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_nested_permissioned_operation_created_skipping_authorisation_is_not_asked_of_the_providers()
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));
        await using var services = BuildAppProvider(s => s.AddSingleton<IResourceAuthorizationProvider>(provider));
        var (parent, executor) = CreateParentContext(services, user: null);

        var result = await executor.ExecuteAsync(parent.CreateNested(new PermissionedUnlinkedCommand(), skipAuthorisation: true));

        result.Should().BeOfType<OkResult<string>>();
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_permissioned_message_that_is_not_marked_anonymous_is_denied_or_allowed_by_the_providers()
    {
        // Not subscribed, so its descriptor does not allow anonymous access: it is authorised as any unlinked operation is.
        var refusing = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));
        await using var refusingServices = BuildRebusConsumerProvider(subscribe: false, services => services.AddSingleton<IResourceAuthorizationProvider>(refusing));

        var refused = await ExecuteMessageAsync(refusingServices, AuthenticatedUser());

        refused.Should().BeOfType<ForbiddenResult>();
        refusing.Requests.Should().HaveCount(1);

        var granting = new RecordingProvider(ExecutionAllowed.Yes);
        await using var grantingServices = BuildRebusConsumerProvider(subscribe: false, services => services.AddSingleton<IResourceAuthorizationProvider>(granting));

        var granted = await ExecuteMessageAsync(grantingServices, AuthenticatedUser());

        granted.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("message handled");
        granting.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_permissioned_message_the_consumer_subscribes_to_is_not_asked_of_the_providers_as_its_descriptor_allows_anonymous_access()
    {
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));
        await using var services = BuildRebusConsumerProvider(subscribe: true, s => s.AddSingleton<IResourceAuthorizationProvider>(provider));

        var result = await ExecuteMessageAsync(services, AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("message handled");
        provider.Requests.Should().BeEmpty();
    }

    private static ServiceProvider BuildAppProvider(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection().AddLogging();
        configureServices(services);
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildRebusConsumerProvider(bool subscribe, Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection().AddLogging();
        configureServices(services);
        services.AddBlueprintRebusConsumer(c =>
        {
            if (subscribe)
            {
                c.Subscribe<PermissionedMessage>();
            }
        });
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>();

        return services.BuildServiceProvider();
    }

    private static async Task<OperationResult> ExecuteMessageAsync(ServiceProvider provider, ClaimsPrincipal? user)
    {
        var app = provider.GetRequiredService<IntegrationTestBlueprintApp>();
        var executor = provider.GetRequiredService<IntegrationTestBlueprintAppExecutor>();
        await using var scope = provider.CreateAsyncScope();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(PermissionedMessage))!, CancellationToken.None)
        {
            Operation = new PermissionedMessage(),
            UserPrincipal = user,
        };

        return await executor.ExecuteAsync(context);
    }

    private static (ApiOperationContext Context, IntegrationTestBlueprintAppExecutor Executor) CreateParentContext(ServiceProvider provider, ClaimsPrincipal? user)
    {
        var app = provider.GetRequiredService<IntegrationTestBlueprintApp>();
        var executor = provider.GetRequiredService<IntegrationTestBlueprintAppExecutor>();

        var context = new ApiOperationContext(provider.CreateScope().ServiceProvider, app, app.FindOperation(typeof(UnlinkedAuthorisedCommand))!, CancellationToken.None)
        {
            Operation = new UnlinkedAuthorisedCommand(),
            UserPrincipal = user,
        };

        return (context, executor);
    }

    private static ClaimsPrincipal AuthenticatedUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId), new Claim("AccountId", AccountId)], "TestAuth"));
    }

    private static async Task<OperationResult> ExecuteAsync(
        IOperation operation,
        Action<IServiceCollection> configureServices,
        ClaimsPrincipal? user = null,
        Action<ApiOperationContext>? configure = null,
        Action<IntegrationTestBlueprintApp>? configureApp = null)
    {
        // A fresh app, whose Configure() calls made descriptors of its own, so marking one changes no other test's.
        var app = new IntegrationTestBlueprintApp();
        configureApp?.Invoke(app);

        var services = new ServiceCollection().AddLogging();
        configureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(operation.GetType())!, CancellationToken.None)
        {
            Operation = operation,
            UserPrincipal = user,
        };

        configure?.Invoke(context);

        context.HttpContext.Should().BeNull("an unlinked operation is executed with no HTTP context");

        return await executor.ExecuteAsync(context);
    }

    /// <summary>Answers every request with one decision, recording the requests and the principals it was asked about.</summary>
    private sealed class RecordingProvider(ExecutionAllowed decision) : IResourceAuthorizationProvider
    {
        public List<ResourceAuthorizationRequest> Requests { get; } = [];

        public List<ClaimsPrincipal> Principals { get; } = [];

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            this.Principals.Add(principal);

            return new ValueTask<ExecutionAllowed>(decision);
        }
    }
}

/// <summary>A permissioned operation with no link, so executed only in process.</summary>
[Resource("widget", "read")]
public partial class PermissionedUnlinkedCommand : ICommand<string>
{
    public static string Handle(PermissionedUnlinkedCommand command)
    {
        return "executed";
    }
}

/// <summary>A permissioned operation with no link, whose resource key is a template over the operation and the caller.</summary>
[Resource("widget", "read", "/accounts/{Auth.AccountId}/widgets/{Id}")]
public partial class PermissionedUnlinkedTemplateCommand : ICommand<string>
{
    public string? Id { get; set; }

    public static string Handle(PermissionedUnlinkedTemplateCommand command)
    {
        return "executed";
    }
}

/// <summary>A permissioned message the tests subscribe to, or not, through <c>AddBlueprintRebusConsumer</c>.</summary>
[Resource("widget", "read")]
public partial class PermissionedMessage : ICommand<string>
{
    public static string Handle(PermissionedMessage message)
    {
        return "message handled";
    }
}
