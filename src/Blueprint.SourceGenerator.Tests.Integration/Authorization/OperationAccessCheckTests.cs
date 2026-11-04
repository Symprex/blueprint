using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Middleware;
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

    [Theory]
    [InlineData("widget::read", "/accounts/account-9/*;widget::read")]
    [InlineData("widget::read", "/*;widget::read")]
    [InlineData("widget::read", null)]
    public async Task A_pair_with_no_template_is_given_no_key_as_at_execution(string scope, string? grant)
    {
        // Execution resolves a template-less pair to no key, which no grant covers; this check once gave it the key asked about
        var user = User(scope, grant);

        var executed = await ExecuteAsync(new PermissionedUnlinkedCommand(), RealProviders, user);
        var checkedAccess = await CheckAsync<PermissionedUnlinkedCommand>(new ResourceKey("/accounts/account-9/widgets/w1"), RealProviders, user);

        checkedAccess.IsAllowed.Should().Be(executed is OkResult<string>, $"the execution returned {executed.GetType().Name}");
    }

    [Fact]
    public async Task Providers_are_given_a_nested_context_for_the_operation_asked_about()
    {
        var provider = new ContextRecordingProvider();
        ApiOperationContext? asking = null;

        await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User(),
            context => asking = context);

        var seen = provider.Contexts.Should().ContainSingle().Subject;
        seen.Descriptor.Should().BeSameAs(provider.Requests[0].Descriptor);
        seen.Descriptor.OperationType.Should().Be(typeof(PermissionedUnlinkedTemplateCommand));
        seen.Operation.Should().BeOfType<PermissionedUnlinkedTemplateCommand>();
        seen.Parent.Should().BeSameAs(asking);
        seen.UserPrincipal.Should().BeSameAs(asking!.UserPrincipal);
        seen.Data.Should().BeSameAs(asking.Data);
    }

    [Fact]
    public async Task A_provider_that_reads_the_operation_as_the_target_type_can_judge_it()
    {
        // As a provider may at execution, where the context's operation is the one being judged
        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(new CastingProvider()),
            User());

        result.IsAllowed.Should().BeTrue();
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

    [Theory]
    [InlineData("w1", "widget::read", "/accounts/account-9/widgets/w1;widget::read")]
    [InlineData("w1", "widget::read", "/accounts/account-9/*;widget::read")]
    [InlineData("w2", "widget::read", "/accounts/account-9/widgets/w1;widget::read")]
    [InlineData("w1", "widget::read", "/accounts/account-1/*;widget::read")]
    [InlineData("w1", "widget::write", "/accounts/account-9/*;widget::read")]
    [InlineData("w1", "widget::read", null)]
    [InlineData(null, "widget::read", "/accounts/account-9/widgets/;widget::read")]
    public async Task An_instance_is_decided_as_its_own_execution_decides_it(string? id, string scope, string? grant)
    {
        var user = User(scope, grant);

        var executed = await ExecuteAsync(new PermissionedUnlinkedTemplateCommand { Id = id }, RealProviders, user);
        var checkedAccess = await CheckInstanceAsync(new PermissionedUnlinkedTemplateCommand { Id = id }, RealProviders, user);

        checkedAccess.IsAllowed.Should().Be(executed is OkResult<string>, $"the execution returned {executed.GetType().Name}");
    }

    [Fact]
    public async Task An_instances_keys_are_expanded_from_it_and_the_caller()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        await CheckInstanceAsync(
            new TwoPairAccessCheckCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User());

        var request = provider.Requests.Should().ContainSingle().Subject;
        request.Mode.Should().Be(ResourceAuthorizationMode.Execute);
        request.Resource.Should().BeNull();
        request.Descriptor.OperationType.Should().Be(typeof(TwoPairAccessCheckCommand));
        request.ScopePairs.Should().Equal(
            new ResolvedScopePair("widget", "read", "/accounts/{Auth.AccountId}/widgets/{Id}", "/accounts/account-9/widgets/w1"),
            new ResolvedScopePair("widget", "manage", "/accounts/{Auth.AccountId}", "/accounts/account-9"));
    }

    [Theory]
    [InlineData("tenant-1", 7, "owner-1", "/accounts/tenant-1/widgets/7/owners/owner-1/eu")]
    [InlineData(null, 7, "owner-1", "/accounts/account-9/widgets/7/owners/owner-1/eu")]
    [InlineData("", 0, null, "/accounts//widgets/0/owners//eu")]
    public async Task An_instances_keys_are_those_its_execution_resolves(string? tenantId, int number, string? owner, string expected)
    {
        // A fallback, a value type, a nested path and a claim the generator does not know, each expanded by the same code
        static InstanceAccessCheckCommand Operation(string? tenantId, int number, string? owner) => new()
        {
            TenantId = tenantId,
            Number = number,
            Owner = owner is null ? null : new InstanceAccessCheckOwner { Name = owner },
        };

        var atExecution = new RecordingProvider(ExecutionAllowed.Yes);
        var atCheck = new RecordingProvider(ExecutionAllowed.Yes);
        var user = User(region: "eu");

        await ExecuteAsync(Operation(tenantId, number, owner), services => services.AddSingleton<IResourceAuthorizationProvider>(atExecution), user);
        await CheckInstanceAsync(Operation(tenantId, number, owner), services => services.AddSingleton<IResourceAuthorizationProvider>(atCheck), user);

        var executed = atExecution.Requests.Should().ContainSingle().Subject.ScopePairs.Should().ContainSingle().Subject;
        var checkedPair = atCheck.Requests.Should().ContainSingle().Subject.ScopePairs.Should().ContainSingle().Subject;

        executed.ExpandedResourceKey.Should().Be(expected);
        checkedPair.Should().Be(executed);
    }

    [Fact]
    public async Task An_instances_caller_values_come_from_the_user_context_before_the_claims_as_at_execution()
    {
        // The claim says account-9; the user's authorisation context, which execution reads first, says account-7
        var atExecution = new RecordingProvider(ExecutionAllowed.Yes);
        var atCheck = new RecordingProvider(ExecutionAllowed.Yes);
        var userContext = new TestUserContext("user-1", "account-7");

        await ExecuteAsync(
            new TwoPairAccessCheckCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(atExecution),
            User(),
            context => context.UserAuthorisationContext = userContext);
        await CheckInstanceAsync(
            new TwoPairAccessCheckCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(atCheck),
            User(),
            context => context.UserAuthorisationContext = userContext);

        var executed = atExecution.Requests.Should().ContainSingle().Subject.ScopePairs;
        executed.Select(static pair => pair.ExpandedResourceKey).Should().Equal("/accounts/account-7/widgets/w1", "/accounts/account-7");
        atCheck.Requests.Should().ContainSingle().Which.ScopePairs.Should().Equal(executed);
    }

    [Fact]
    public async Task An_instance_is_judged_as_given_without_the_population_execution_runs()
    {
        // Execution's populator fills Id before it authorises; the check does not, so an unset Id is asked about as
        // /accounts/account-9/widgets/, which a grant on w1 alone does not cover, and the instance is left as it was
        var user = User("widget::read", "/accounts/account-9/widgets/w1;widget::read");

        void Services(IServiceCollection services)
        {
            RealProviders(services);
            services.AddSingleton<IPropertyPopulator>(new WidgetIdPopulator("w1"));
        }

        var executed = await ExecuteAsync(new PermissionedUnlinkedTemplateCommand(), Services, user);

        var operation = new PermissionedUnlinkedTemplateCommand();
        var checkedAccess = await CheckInstanceAsync(operation, Services, user);

        executed.Should().BeOfType<OkResult<string>>("execution populates Id before it authorises");
        checkedAccess.IsAllowed.Should().BeFalse();
        operation.Id.Should().BeNull();
    }

    [Fact]
    public async Task Providers_are_given_a_nested_context_whose_operation_is_the_instance()
    {
        var provider = new ContextRecordingProvider();
        var operation = new PermissionedUnlinkedTemplateCommand { Id = "w1" };
        ApiOperationContext? asking = null;

        await CheckInstanceAsync(
            operation,
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            User(),
            context => asking = context);

        var seen = provider.Contexts.Should().ContainSingle().Subject;
        seen.Operation.Should().BeSameAs(operation);
        seen.Descriptor.OperationType.Should().Be(typeof(PermissionedUnlinkedTemplateCommand));
        seen.Parent.Should().BeSameAs(asking);
        seen.UserPrincipal.Should().BeSameAs(asking!.UserPrincipal);
        seen.Data.Should().BeSameAs(asking.Data);
    }

    [Fact]
    public async Task An_instance_with_a_caller_with_no_authenticated_principal_is_refused_as_anonymous()
    {
        var provider = new RecordingProvider(ExecutionAllowed.Yes);

        var result = await CheckInstanceAsync(
            new PermissionedUnlinkedTemplateCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(provider),
            null);

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authentication);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task An_instance_with_no_provider_registered_fails_closed()
    {
        var result = await CheckInstanceAsync(new PermissionedUnlinkedTemplateCommand { Id = "w1" }, static _ => { }, User());

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
    }

    [Fact]
    public async Task An_instance_that_declares_no_scope_pairs_is_refused_as_an_argument()
    {
        var check = () => CheckInstanceAsync(new UnpermissionedAccessCheckCommand(), static _ => { }, User());

        await check.Should().ThrowAsync<ArgumentException>().WithMessage("*UnpermissionedAccessCheckCommand*scope pairs*");
    }

    [Fact]
    public async Task A_null_instance_is_refused()
    {
        var check = () => CheckInstanceAsync(null!, static _ => { }, User());

        await check.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_key_only_check_refuses_when_a_provider_reads_the_operation_and_asks_no_provider()
    {
        // Given only a key, such a provider would judge a default instance and could allow what the real one would not
        var first = new RecordingProvider(ExecutionAllowed.Yes);
        var reading = new OperationReadingProvider(typeof(PermissionedUnlinkedTemplateCommand));

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(first).AddSingleton<IResourceAuthorizationProvider>(reading),
            User());

        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
        result.Reason.Should().Contain(nameof(OperationReadingProvider)).And.Contain("CanExecuteAsync(IOperation)");
        first.Requests.Should().BeEmpty();
        reading.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_key_only_check_by_type_refuses_when_a_provider_reads_the_operation()
    {
        var result = await WithContextAsync(
            services => services.AddSingleton<IResourceAuthorizationProvider>(new OperationReadingProvider(typeof(PermissionedUnlinkedTemplateCommand))),
            User(),
            null,
            static context => context.CanExecuteAsync(typeof(PermissionedUnlinkedTemplateCommand), new ResourceKey("/accounts/account-9/widgets/w1")));

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("CanExecuteAsync(IOperation)");
    }

    [Fact]
    public async Task A_provider_that_reads_only_other_operations_does_not_refuse_a_key_only_check()
    {
        var reading = new OperationReadingProvider(typeof(TwoPairAccessCheckCommand));

        var result = await CheckAsync<PermissionedUnlinkedTemplateCommand>(
            new ResourceKey("/accounts/account-9/widgets/w1"),
            services => services.AddSingleton<IResourceAuthorizationProvider>(reading),
            User());

        result.IsAllowed.Should().BeTrue();
        reading.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_instance_check_asks_a_provider_that_reads_the_operation()
    {
        var reading = new OperationReadingProvider(typeof(PermissionedUnlinkedTemplateCommand));

        var result = await CheckInstanceAsync(
            new PermissionedUnlinkedTemplateCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(reading),
            User());

        result.IsAllowed.Should().BeTrue();
        reading.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Execution_asks_a_provider_that_reads_the_operation()
    {
        var reading = new OperationReadingProvider(typeof(PermissionedUnlinkedTemplateCommand));

        var executed = await ExecuteAsync(
            new PermissionedUnlinkedTemplateCommand { Id = "w1" },
            services => services.AddSingleton<IResourceAuthorizationProvider>(reading),
            User());

        executed.Should().BeOfType<OkResult<string>>();
        reading.Requests.Should().ContainSingle();
    }

    private static void RealProviders(IServiceCollection services)
    {
        services.AddBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>(static b => b
            .AddResourceAuthorizationProvider(new ScopePairClaimAuthorizationProvider())
            .AddResourceKeyAuthorization()
            .AddResourceGrantSource(new ScopePairClaimResourceGrantSource("grant", "::")));
    }

    private static ClaimsPrincipal User(string? scope = "widget::read", string? grant = null, string? region = null)
    {
        var claims = new List<Claim> { new("sub", "user-1"), new("AccountId", AccountId) };

        if (region is not null)
        {
            claims.Add(new Claim("Region", region));
        }

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

    private static Task<ExecutionAllowed> CheckInstanceAsync(
        IOperation operation,
        Action<IServiceCollection> configureServices,
        ClaimsPrincipal? user,
        Action<ApiOperationContext>? configure = null)
    {
        return WithContextAsync(configureServices, user, configure, context => context.CanExecuteAsync(operation));
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

    private static async Task<OperationResult> ExecuteAsync(
        IOperation operation,
        Action<IServiceCollection> configureServices,
        ClaimsPrincipal user,
        Action<ApiOperationContext>? configure = null)
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

        configure?.Invoke(context);

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

    /// <summary>Allows every request, recording the context and request it was given.</summary>
    private sealed class ContextRecordingProvider : IResourceAuthorizationProvider
    {
        public List<ApiOperationContext> Contexts { get; } = [];

        public List<ResourceAuthorizationRequest> Requests { get; } = [];

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            this.Contexts.Add(operationContext);
            this.Requests.Add(request);

            return new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes);
        }
    }

    /// <summary>Casts the context's operation to the type it judges, as a provider written for execution may.</summary>
    private sealed class CastingProvider : IResourceAuthorizationProvider
    {
        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            var operation = (PermissionedUnlinkedTemplateCommand)operationContext.Operation;

            return new ValueTask<ExecutionAllowed>(operation is not null ? ExecutionAllowed.Yes : ExecutionAllowed.No("none", "Denied", ExecutionAllowedFailureType.Authorisation));
        }
    }

    /// <summary>
    /// Declares that it reads the operation instance when judging <paramref name="readsFor"/>, and allows every request,
    /// recording them.
    /// </summary>
    private sealed class OperationReadingProvider(Type readsFor) : IResourceAuthorizationProvider
    {
        public List<ResourceAuthorizationRequest> Requests { get; } = [];

        public bool ReadsOperation(ApiOperationDescriptor operation)
        {
            return operation.OperationType == readsFor;
        }

        public ValueTask<ExecutionAllowed> AuthorizeAsync(
            ApiOperationContext operationContext,
            ClaimsPrincipal principal,
            ResourceAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            this.Requests.Add(request);

            return new ValueTask<ExecutionAllowed>(ExecutionAllowed.Yes);
        }
    }

    /// <summary>Fills an unset widget <c>Id</c> during execution's population stage, as an app's populator might.</summary>
    private sealed class WidgetIdPopulator(string id) : IPropertyPopulator
    {
        public Task PopulateAsync(ApiOperationContext context, CancellationToken cancellationToken)
        {
            if (context.Operation is PermissionedUnlinkedTemplateCommand { Id: null } operation)
            {
                operation.Id = id;
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A user's authorisation context, whose values a key's well-known <c>{Auth.X}</c> tokens read first.</summary>
    private sealed class TestUserContext(string id, string accountId) : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => id;

        public string? IdentityId => id;

        public string AccountId => accountId;

        public string? Email => null;

        public string? Name => null;

        public void PopulateMetadata(Action<string, object> add)
        {
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

/// <summary>
/// A permissioned operation whose key has a fallback, a value type, a nested path and a claim the generator does not know,
/// so a check of an instance must expand it as the execution does.
/// </summary>
[Resource("widget", "read", "/accounts/{TenantId:Auth.AccountId}/widgets/{Number}/owners/{Owner.Name}/{Auth.Region}")]
public partial class InstanceAccessCheckCommand : ICommand<string>
{
    public string? TenantId { get; set; }

    public int Number { get; set; }

    public InstanceAccessCheckOwner? Owner { get; set; }

    public static string Handle(InstanceAccessCheckCommand command)
    {
        return "executed";
    }
}

/// <summary>The owner of an <see cref="InstanceAccessCheckCommand"/>'s widget.</summary>
public sealed class InstanceAccessCheckOwner
{
    public string? Name { get; set; }
}

/// <summary>An operation that declares no scope pairs, so has nothing for a key to be judged against.</summary>
public partial class UnpermissionedAccessCheckCommand : ICommand<string>
{
    public static string Handle(UnpermissionedAccessCheckCommand command)
    {
        return "executed";
    }
}
