using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that an operation with no <c>[RootLink]</c>, <c>[SelfLink]</c> or <c>[Link]</c> runs its applicable authorisers
/// when executed in process with a user, as legacy Blueprint's <c>AuthorisationMiddlewareBuilder</c> ran them for every
/// operation it executed, and that the executions legacy never authorised still are not: one that allows anonymous access
/// (an <c>[AllowAnonymous]</c> operation, a background task, a Rebus message), one that skips authorisation, and one with no
/// user.
/// </summary>
/// <remarks>
/// <para>
/// Legacy matched every operation whose descriptor did not allow anonymous access (Signature365 <c>8b40ecc961</c>,
/// <c>Blueprint/Authorisation/AuthorisationMiddlewareBuilder.cs:52-55</c>), whatever its links, and inside
/// <c>if (context.SkipAuthorisation == false)</c> ran each applicable authoriser (<c>:76-103</c>). A background task
/// (<c>Blueprint.Tasks/BackgroundTaskAllowAnonymousConvention.cs:13-20</c>) and a subscribed Rebus message
/// (<c>Symprex.Signature365.BlueprintRebus/RebusScannerConvention.cs:30-44</c>) allowed anonymous access, so none was
/// authorised.
/// </para>
/// <para>
/// Every execution here has no <c>HttpContext</c>, as a background task's, a Rebus message's and
/// <c>ExecuteWithNewScopeAsync</c>'s have none: the user is the context's <see cref="ApiOperationContext.UserPrincipal"/>.
/// </para>
/// </remarks>
public sealed class UnlinkedOperationAuthoriserIntegrationTests
{
    private const string UserId = "user-7";

    [Fact]
    public async Task An_unlinked_operation_executed_by_a_user_an_authoriser_denies_is_forbidden()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Not this tenant's", "Forbidden here", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IApiAuthoriser>(authoriser), AuthenticatedUser());

        result.Should().BeOfType<ForbiddenResult>().Which.Failure!.Value.Message.Should().Be("Forbidden here");
        authoriser.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task An_unlinked_operation_executed_by_a_user_an_authoriser_denies_as_unauthenticated_is_unauthenticated()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Session expired", "Please log in", ExecutionAllowedFailureType.Authentication));

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IApiAuthoriser>(authoriser), AuthenticatedUser());

        result.Should().BeOfType<UnauthenticatedResult>();
    }

    [Fact]
    public async Task An_unlinked_operation_executed_by_a_user_an_operation_authoriser_denies_is_forbidden()
    {
        var authoriser = new RecordingOperationAuthoriser(ExecutionAllowed.No("Not this tenant's", "Forbidden here", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IOperationAuthoriser>(authoriser), AuthenticatedUser());

        result.Should().BeOfType<ForbiddenResult>();
        authoriser.Invocations.Should().Be(1);
    }

    [Fact]
    public async Task An_unlinked_operation_executed_by_a_user_its_authorisers_allow_runs_each_once_with_the_users_context()
    {
        var apiAuthoriser = new RecordingApiAuthoriser(ExecutionAllowed.Yes);
        var operationAuthoriser = new RecordingOperationAuthoriser(ExecutionAllowed.Yes);

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services
                .AddSingleton<IUserAuthorisationContextFactory>(new SubjectUserContextFactory())
                .AddSingleton<IApiAuthoriser>(apiAuthoriser)
                .AddSingleton<IOperationAuthoriser>(operationAuthoriser),
            AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("executed");
        apiAuthoriser.Invocations.Should().Be(1);
        operationAuthoriser.Invocations.Should().Be(1);

        // The factory made the context from the principal's subject claim: the user came from UserPrincipal.
        apiAuthoriser.SeenUserId.Should().Be(UserId);
        operationAuthoriser.SeenUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task An_unlinked_operation_whose_first_authoriser_denies_runs_no_other()
    {
        var operationAuthoriser = new RecordingOperationAuthoriser(ExecutionAllowed.Yes);

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services
                .AddSingleton<IApiAuthoriser>(new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation)))
                .AddSingleton<IOperationAuthoriser>(operationAuthoriser),
            AuthenticatedUser());

        result.Should().BeOfType<ForbiddenResult>();
        operationAuthoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_operation_runs_no_authoriser_that_does_not_apply_to_it()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation), appliesTo: false);

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IApiAuthoriser>(authoriser), AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>();
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_operation_given_a_users_context_without_a_principal_runs_its_authorisers()
    {
        // A nested execution, or a host that sets the user's context itself, has a user without a principal.
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services.AddSingleton<IApiAuthoriser>(authoriser),
            configure: context => context.UserAuthorisationContext = new TestUserContext("context-user"));

        result.Should().BeOfType<ForbiddenResult>();
        authoriser.SeenUserId.Should().Be("context-user");
    }

    [Fact]
    public async Task An_unlinked_operation_executed_with_no_user_runs_without_its_authorisers()
    {
        // Not legacy's mechanism, which refused a non-anonymous operation with no user ("Access denied. Anonymous access is
        // not allowed.", AuthorisationMiddlewareBuilder.cs:78-82) — but every unlinked operation legacy executed with no user
        // allowed anonymous access, as a background task or a subscribed message. Here a message's descriptor is marked so
        // only when Rebus first delivers it, so one executed in process before that, as a test fixture executes one, would be
        // refused. An execution with no user is a system one, and runs as it did before authorisers ran for unlinked
        // operations.
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authentication));

        var result = await ExecuteAsync(new UnlinkedAuthorisedCommand(), services => services.AddSingleton<IApiAuthoriser>(authoriser));

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("executed");
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_operation_executed_by_an_unauthenticated_principal_runs_without_its_authorisers()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authentication));

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services.AddSingleton<IApiAuthoriser>(authoriser),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId)])));

        result.Should().BeOfType<OkResult<string>>();
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_operation_whose_descriptor_allows_anonymous_access_runs_without_its_authorisers()
    {
        // As BlueprintApiActivator marks a Rebus message's descriptor when it first delivers one.
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services.AddSingleton<IApiAuthoriser>(authoriser),
            AuthenticatedUser(),
            configureApp: app => app.FindOperation(typeof(UnlinkedAuthorisedCommand))!.AnonymousAccessAllowed = true);

        result.Should().BeOfType<OkResult<string>>();
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_operation_that_skips_authorisation_runs_without_its_authorisers()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new UnlinkedAuthorisedCommand(),
            services => services.AddSingleton<IApiAuthoriser>(authoriser),
            AuthenticatedUser(),
            configure: static context => context.SkipAuthorisation = true);

        result.Should().BeOfType<OkResult<string>>();
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task A_background_task_executed_by_a_user_runs_without_its_authorisers()
    {
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(new UnlinkedAuthorisedBackgroundTask(), services => services.AddSingleton<IApiAuthoriser>(authoriser), AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("task executed");
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task A_background_task_run_by_the_task_executor_runs_whatever_its_authorisers_would_decide()
    {
        // The path every scheduled job takes: TaskExecutor's own context, with no user and no HttpContext. A denial here
        // would not throw: the task would silently not run, so the handler's run is what is asserted.
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Please log in", ExecutionAllowedFailureType.Authentication));
        var recorder = new BackgroundTaskRunRecorder();

        var app = new IntegrationTestBlueprintApp();
        await using var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IApiAuthoriser>(authoriser)
            .AddSingleton(recorder)
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);

        await new Blueprint.Runtime.Tasks.TaskExecutor(executor, serviceProvider).ExecuteAsync(new BackgroundTaskEnvelope(new RecordedBackgroundTask()));

        recorder.Runs.Should().Be(1);
        authoriser.Invocations.Should().Be(0);
    }

    [Fact]
    public async Task An_unlinked_anonymous_operation_runs_without_its_authorisers_but_is_given_the_users_context()
    {
        // As an [AllowAnonymous] HTTP operation is: its handler's Identity is the caller's when there is one.
        var authoriser = new RecordingApiAuthoriser(ExecutionAllowed.No("Denied", "Denied", ExecutionAllowedFailureType.Authorisation));

        var result = await ExecuteAsync(
            new UnlinkedAnonymousIdentityQuery(),
            services => services
                .AddSingleton<IUserAuthorisationContextFactory>(new SubjectUserContextFactory())
                .AddSingleton<IApiAuthoriser>(authoriser),
            AuthenticatedUser());

        result.Should().BeOfType<OkResult<string>>().Which.Content.Should().Be("identity:" + UserId);
        authoriser.Invocations.Should().Be(0);
    }

    private static ClaimsPrincipal AuthenticatedUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId)], "TestAuth"));
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

    private sealed class RecordingApiAuthoriser(ExecutionAllowed result, bool appliesTo = true) : IApiAuthoriser
    {
        public int Invocations { get; private set; }

        public string? SeenUserId { get; private set; }

        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return appliesTo;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            this.Invocations++;
            this.SeenUserId = operationContext.UserAuthorisationContext?.Id;

            return Task.FromResult(result);
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return ExecutionAllowed.YesTask;
        }
    }

    private sealed class RecordingOperationAuthoriser(ExecutionAllowed result) : IOperationAuthoriser
    {
        public int Invocations { get; private set; }

        public string? SeenUserId { get; private set; }

        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            this.Invocations++;
            this.SeenUserId = operationContext.UserAuthorisationContext?.Id;

            return Task.FromResult(result);
        }
    }

    /// <summary>Makes a user's context whose id is the identity's <c>sub</c> claim.</summary>
    private sealed class SubjectUserContextFactory : IUserAuthorisationContextFactory
    {
        public Task<IUserAuthorisationContext?> CreateContextAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
        {
            return Task.FromResult<IUserAuthorisationContext?>(new TestUserContext(identity.FindFirst("sub")!.Value));
        }
    }

    private sealed class TestUserContext(string id) : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => id;

        public string IdentityId => id;

        public string AccountId => "account-" + id;

        public string Email => id + "@example.test";

        public string Name => id;

        public void PopulateMetadata(Action<string, object> add)
        {
        }
    }
}

/// <summary>An operation with no link, so executed only in process.</summary>
public partial class UnlinkedAuthorisedCommand : ICommand<string>
{
    public static string Handle(UnlinkedAuthorisedCommand command)
    {
        return "executed";
    }
}

/// <summary>An anonymous operation with no link, whose handler reports the user it was given.</summary>
[AllowAnonymous]
public partial class UnlinkedAnonymousIdentityQuery : IQuery<string>
{
    public static string Handle(UnlinkedAnonymousIdentityQuery query, IUserAuthorisationContext? identity)
    {
        return "identity:" + (identity?.Id ?? "none");
    }
}

/// <summary>A background task, which allows anonymous access by convention.</summary>
public partial class UnlinkedAuthorisedBackgroundTask : IBackgroundTask
{
    public OkResult<string> Handle()
    {
        return new OkResult<string>("task executed");
    }
}

/// <summary>A background task whose handler records each run.</summary>
public partial class RecordedBackgroundTask : IBackgroundTask
{
    public static OkResult<string> Handle(RecordedBackgroundTask task, BackgroundTaskRunRecorder recorder)
    {
        recorder.Runs++;

        return new OkResult<string>("recorded");
    }
}

/// <summary>Counts the runs of <see cref="RecordedBackgroundTask"/>.</summary>
public sealed class BackgroundTaskRunRecorder
{
    public int Runs { get; set; }
}
