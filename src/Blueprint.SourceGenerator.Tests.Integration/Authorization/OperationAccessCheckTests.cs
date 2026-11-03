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
/// Proves <see cref="ApiOperationContext.CanExecuteAsync{TOperation}(ResourceKey, CancellationToken)"/> answers "may this
/// caller run <c>T</c> against this key?" as <c>T</c>'s own execution would: every registered provider, all-of, in
/// <see cref="ResourceAuthorizationMode.Execute"/> mode, for <c>T</c>'s descriptor, with each of its scope pairs given the
/// key (Symprex/blueprint#129).
/// </summary>
public sealed class OperationAccessCheckTests
{
    private const string AccountId = "account-9";

    [Theory]
    [InlineData("w1", "widget::read", "/accounts/account-9/widgets/w1;widget::read")]
    [InlineData("w1", "widget::read", "/accounts/account-9/*;widget::read")]
    [InlineData("w2", "widget::read", "/accounts/account-9/widgets/w1;widget::read")]
    [InlineData("w1", "widget::read", "/accounts/account-1/*;widget::read")]
    [InlineData("w1", "widget::read", "/accounts/account-9/*;widget::write")]
    [InlineData("w1", "widget::write", "/accounts/account-9/*;widget::read")]
    [InlineData("w1", "widget::read", null)]
    [InlineData("w1", null, "/accounts/account-9/*;widget::read")]
    public async Task It_decides_as_the_operations_own_execution_does(string id, string? scope, string? grant)
    {
        // The key the execution's template resolves to, asked of the check from another operation's context
        var key = new ResourceKey($"/accounts/{AccountId}/widgets/{id}");
        var user = User(scope, grant);

        var executed = await ExecuteAsync(new PermissionedUnlinkedTemplateCommand { Id = id }, RealProviders, user);
        var checkedAccess = await CheckAsync<PermissionedUnlinkedTemplateCommand>(key, RealProviders, user);

        checkedAccess.IsAllowed.Should().Be(executed is OkResult<string>, $"the execution returned {executed.GetType().Name}");
    }

    [Fact]
    public async Task Every_provider_is_asked_in_execute_mode_for_the_named_operation_with_every_pair_given_the_key()
    {
        var first = new RecordingProvider(ExecutionAllowed.Yes);
        var second = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await CheckAsync<TwoPairAccessCheckCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(first).AddSingleton<IResourceAuthorizationProvider>(second),
            User());

        result.IsAllowed.Should().BeTrue();
        first.Requests.Should().ContainSingle();
        second.Requests.Should().ContainSingle();

        var request = first.Requests[0];
        request.Mode.Should().Be(ResourceAuthorizationMode.Execute);
        request.Resource.Should().BeNull();
        request.Descriptor.OperationType.Should().Be(typeof(TwoPairAccessCheckCommand));
        request.ScopePairs.Should().Equal(
            new ResolvedScopePair("widget", "read", "/accounts/{Auth.AccountId}/widgets/{Id}", "/accounts/account-9/widgets/w1"),
            new ResolvedScopePair("widget", "manage", "/accounts/{Auth.AccountId}", "/accounts/account-9/widgets/w1"));
    }

    [Fact]
    public async Task The_first_denial_is_the_answer_and_later_providers_are_not_asked()
    {
        var denial = ExecutionAllowed.No("internal reason", "You may not read this widget", ExecutionAllowedFailureType.Authorisation);
        var first = new RecordingProvider(denial);
        var second = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(first).AddSingleton<IResourceAuthorizationProvider>(second),
            User());

        result.Should().Be(denial);
        second.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_provider_completing_asynchronously_is_awaited()
    {
        var provider = new AsyncProvider(ExecutionAllowed.No("later", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User());

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("later");
    }

    [Fact]
    public async Task The_caller_asked_about_is_the_contexts_principal()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);
        var user = User();

        await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            user);

        provider.Principals.Should().ContainSingle().Which.Should().BeSameAs(user);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caller_with_no_authenticated_principal_is_refused_as_anonymous_without_asking_a_provider(bool withPrincipal)
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            withPrincipal ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")])) : null);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_provider_registered_it_fails_closed()
    {
        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(new ResourceKey("/accounts/account-9/widgets/w1"), static _ => { }, User());

        result.IsAllowed.Should().BeFalse("an execution with scope pairs and nothing to judge them is refused");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
    }

    [Fact]
    public async Task Skipping_authorisation_on_the_asking_context_does_not_skip_the_check()
    {
        // The question is about the caller, not about this context's own execution
        var provider = new RecordingProvider(ExecutionAllowed.No("no", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User(),
            static context => context.SkipAuthorisation = true);

        result.IsAllowed.Should().BeFalse();
        provider.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task The_operation_may_be_named_by_its_type()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        await WithContextAsync(
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User(),
            null,
            static context => context.CanExecuteAsync(typeof(PermissionedUnlinkedTemplateCommand), new ResourceKey("/accounts/account-9/widgets/w1")));

        provider.Requests.Should().ContainSingle().Which.Descriptor.OperationType.Should().Be(typeof(PermissionedUnlinkedTemplateCommand));
    }

    [Fact]
    public async Task An_operation_that_declares_no_scope_pairs_is_refused_as_an_argument()
    {
        var check = () => CheckAsync<UnpermissionedAccessCheckCommand>(new ResourceKey("/accounts/account-9"), static _ => { }, User());

        await check.Should().ThrowAsync<ArgumentException>().WithMessage("*UnpermissionedAccessCheckCommand*scope pairs*");
    }

    [Fact]
    public async Task A_type_that_is_not_an_operation_of_the_app_is_refused_as_an_argument()
    {
        var check = () => WithContextAsync(
            static _ => { },
            User(),
            null,
            static context => context.CanExecuteAsync(typeof(string), new ResourceKey("/accounts/account-9")));

        await check.Should().ThrowAsync<ArgumentException>().WithMessage("*String*not an operation*");
    }

    [Fact]
    public async Task A_default_key_is_refused_as_an_argument()
    {
        var check = () => CheckAsync<PermissionedUnlinkedTemplateCommand>(default, static _ => { }, User());

        await check.Should().ThrowAsync<ArgumentException>();
    }

    private static void RealProviders(IServiceCollection services)
    {
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(static b => b
            .AddResourceAuthorizationProvider(new ScopePairClaimAuthorizationProvider())
            .AddResourceKeyAuthorization()
            .AddResourceGrantSource(new ScopePairClaimResourceGrantSource("grant", "::")));
    }

    private static ClaimsPrincipal User(string? scope = "widget::read", string? grant = null)
    {
        var claims = new List<Claim> { new("sub", "user-1"), new("AccountId", AccountId) };

        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        if (grant is not null)
        {
            claims.Add(new Claim("grant", grant));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static Task<ExecutionAllowed> CheckAsync<TOperation>(
        ResourceKey key,
        Action<IServiceCollection> configureServices,
        ClaimsPrincipal? user,
        Action<ApiOperationContext>? configure = null)
    {
        return WithContextAsync(configureServices, user, configure, context => context.CanExecuteAsync<TOperation>(key));
    }

    /// <summary>
    /// Asks from the context of another operation, <see cref="PermissionedUnlinkedCommand"/>, as a handler asks about other
    /// operations than its own.
    /// </summary>
    private static async Task<ExecutionAllowed> WithContextAsync(
        Action<IServiceCollection> configureServices,
        ClaimsPrincipal? user,
        Action<ApiOperationContext>? configure,
        Func<ApiOperationContext, ValueTask<ExecutionAllowed>> check)
    {
        var app = new IntegrationTestBlueprintApp();
        var services = new ServiceCollection().AddLogging();
        configureServices(services);

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        var context = new ApiOperationContext(scope.ServiceProvider, app, app.FindOperation(typeof(PermissionedUnlinkedCommand))!, CancellationToken.None)
        {
            UserPrincipal = user,
        };

        configure?.Invoke(context);

        return await check(context);
    }

    private static async Task<OperationResult> ExecuteAsync(IOperation operation, Action<IServiceCollection> configureServices, ClaimsPrincipal user)
    {
        var app = new IntegrationTestBlueprintApp();
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

    /// <summary>Gives its decision only after yielding, so the check cannot complete synchronously.</summary>
    private sealed class AsyncProvider(ExecutionAllowed decision) : IResourceAuthorizationProvider
    {
        public async ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Yield();

            return decision;
        }
    }
}

/// <summary>A permissioned operation with two scope pairs and no link, which only the access check asks about.</summary>
[Resource("widget", "read", "/accounts/{Auth.AccountId}/widgets/{Id}")]
[Resource("widget", "manage", "/accounts/{Auth.AccountId}")]
public partial class TwoPairAccessCheckCommand : ICommand<string>
{
    public string? Id { get; set; }

    public static string Handle(TwoPairAccessCheckCommand command)
    {
        return "executed";
    }
}

/// <summary>An operation that declares no scope pairs, so has nothing for a key to be judged against.</summary>
public partial class UnpermissionedAccessCheckCommand : ICommand<string>
{
    public static string Handle(UnpermissionedAccessCheckCommand command)
    {
        return "executed";
    }
}
