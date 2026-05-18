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

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves the S365-style tenant isolation flow works end-to-end through generated pipelines:
/// resource authorization via <see cref="IResourceAuthorizationProvider"/> combined with
/// business-level tenant isolation via <see cref="IOperationAuthoriser"/>, including
/// <c>{Auth.AccountId}</c> template expansion.
/// </summary>
public class TenantIsolationIntegrationTests
{
    [Fact]
    public async Task When_tenant_user_accesses_own_resource_then_200()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResourceAuthorizationProvider, S365StyleResourceAuthorizationProvider>()
            .AddSingleton<IOperationAuthoriser, S365TenantIsolationAuthoriser>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(TenantScopedCommand));
        var context = CreateHttpContext(
            app,
            TenantScopedCommand.Configure(),
            new TenantScopedCommand { TenantId = "tenant-42", Name = "Test" },
            CreateAuthenticatedPrincipal(
                "tenant-42",
                new System.Security.Claims.Claim("resource-access", "template:create:/tenants/tenant-42")),
            serviceProvider);

        TenantScopedCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("created-for-tenant-42");
        TenantScopedCommand.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_tenant_user_accesses_other_tenant_resource_then_403()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResourceAuthorizationProvider, S365StyleResourceAuthorizationProvider>()
            .AddSingleton<IOperationAuthoriser, S365TenantIsolationAuthoriser>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(TenantScopedCommand));

        // User is tenant-99 but trying to access tenant-42's resources
        var context = CreateHttpContext(
            app,
            TenantScopedCommand.Configure(),
            new TenantScopedCommand { TenantId = "tenant-42", Name = "Test" },
            CreateAuthenticatedPrincipal(
                "tenant-99",
                new System.Security.Claims.Claim("resource-access", "template:create:/tenants/tenant-99")),
            serviceProvider);

        TenantScopedCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ForbiddenResult>();
        TenantScopedCommand.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_unauthenticated_user_then_401()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResourceAuthorizationProvider, S365StyleResourceAuthorizationProvider>()
            .AddSingleton<IOperationAuthoriser, S365TenantIsolationAuthoriser>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(TenantScopedCommand));
        var context = CreateHttpContext(
            app,
            TenantScopedCommand.Configure(),
            new TenantScopedCommand { TenantId = "tenant-42", Name = "Test" },
            CreateAnonymousPrincipal(),
            serviceProvider);

        TenantScopedCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnauthenticatedResult>();
        TenantScopedCommand.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_auth_account_id_template_expands_correctly_then_200()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResourceAuthorizationProvider, S365StyleResourceAuthorizationProvider>()
            .AddSingleton<IOperationAuthoriser, S365TenantIsolationAuthoriser>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(TenantAccountQuery));

        // {Auth.AccountId} should expand to "acct-77" from the principal's AccountId claim
        var context = CreateHttpContext(
            app,
            TenantAccountQuery.Configure(),
            new TenantAccountQuery(),
            CreateAuthenticatedPrincipal(
                "acct-77",
                new System.Security.Claims.Claim("resource-access", "account:read:/accounts/acct-77")),
            serviceProvider);

        TenantAccountQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("account-acct-77");
        TenantAccountQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_auth_account_id_does_not_match_resource_key_then_403()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResourceAuthorizationProvider, S365StyleResourceAuthorizationProvider>()
            .AddSingleton<IOperationAuthoriser, S365TenantIsolationAuthoriser>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(TenantAccountQuery));

        // User's AccountId is "acct-77" but their resource-access claim is for a different account
        var context = CreateHttpContext(
            app,
            TenantAccountQuery.Configure(),
            new TenantAccountQuery(),
            CreateAuthenticatedPrincipal(
                "acct-77",
                new System.Security.Claims.Claim("resource-access", "account:read:/accounts/acct-OTHER")),
            serviceProvider);

        TenantAccountQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ForbiddenResult>();
        TenantAccountQuery.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_parsable_struct_TenantId_not_in_route_and_accessor_has_value_then_populated_from_accessor()
    {
        var tenantAccessor = new TestTenantAccessor();
        var tenantResolver = new TestTenantResolver();

        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(tenantAccessor)
            .AddSingleton<ITenantResolver>(tenantResolver)
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(ParsableTenantOverviewQuery));

        // Operation starts with default TenantId (empty struct)
        var operation = new ParsableTenantOverviewQuery();
        var context = CreateHttpContext(
            app,
            ParsableTenantOverviewQuery.Configure(),
            operation,
            CreateAuthenticatedPrincipal("tenant-42"),
            serviceProvider);

        ParsableTenantOverviewQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("overview-tenant-42");
        ParsableTenantOverviewQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_parsable_struct_TenantId_query_string_ignored_accessor_wins()
    {
        // TenantId is classified as AuthContext by PropertyOwnershipModule when tenancy is
        // enabled, so it is never bound from the query string. The accessor value (from JWT
        // claims via ITenantResolver) always wins — preventing tenant spoofing via URL.
        var tenantAccessor = new TestTenantAccessor();
        var tenantResolver = new TestTenantResolver();

        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<ITenantAccessor>(tenantAccessor)
            .AddSingleton<ITenantResolver>(tenantResolver)
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(ParsableTenantOverviewQuery));

        var operation = new ParsableTenantOverviewQuery();
        var principal = CreateAuthenticatedPrincipal("tenant-99");
        var context = CreateHttpContext(
            app,
            ParsableTenantOverviewQuery.Configure(),
            operation,
            principal,
            serviceProvider);

        // Set query string value — this should be ignored because TenantId is auth-owned
        ((DefaultHttpContext)context.HttpContext!).Request.QueryString = new QueryString("?tenantId=tenant-from-query");

        ParsableTenantOverviewQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();

        // Accessor value wins — query string is ignored for TenantId (security: prevents spoofing)
        ((OkResult<string>)result).Content.Should().Be("overview-tenant-99");
        ParsableTenantOverviewQuery.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateHttpContext(
        BlueprintApp app,
        ApiOperationDescriptor descriptor,
        object operation,
        System.Security.Claims.ClaimsPrincipal principal,
        IServiceProvider serviceProvider)
    {
        var httpContext = new DefaultHttpContext { User = principal };

        foreach (var property in operation.GetType().GetProperties().Where(static property => property.CanRead))
        {
            var value = property.GetValue(operation);
            if (value is not null)
            {
                httpContext.Request.RouteValues[property.Name] = value.ToString();
            }
        }

        var accountId = principal.FindFirst("AccountId")?.Value ?? string.Empty;
        var context = new ApiOperationContext(serviceProvider, app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = principal,
            UserAuthorisationContext = principal.Identity?.IsAuthenticated == true
                ? new S365UserContext(accountId)
                : null,
        };
        return context;
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAuthenticatedPrincipal(string accountId, params System.Security.Claims.Claim[] additionalClaims)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("AccountId", accountId),
            new(System.Security.Claims.ClaimTypes.Name, $"user-{accountId}"),
        };
        claims.AddRange(additionalClaims);

        return new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, "S365TestAuth"));
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAnonymousPrincipal()
    {
        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
    }

    /// <summary>
    /// Mirrors S365's ScopeApiAuthoriser: checks resource-access claims against expanded resource keys.
    /// </summary>
    private sealed class S365StyleResourceAuthorizationProvider : IResourceAuthorizationProvider
    {
        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
            CancellationToken cancellationToken)
        {
            var isAuthorized = resolvedScopePairs.Any(
                scopePair => principal.HasClaim(
                    "resource-access",
                    $"{scopePair.Noun}:{scopePair.Verb}:{scopePair.ExpandedResourceKey}"));
            return Task.FromResult(isAuthorized);
        }
    }

    /// <summary>
    /// Mirrors S365's tenant isolation check: the user's AccountId must match the operation's TenantId property.
    /// </summary>
    private sealed class S365TenantIsolationAuthoriser : IOperationAuthoriser
    {
        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return descriptor.OperationType.GetProperty("TenantId") is not null;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(
            ApiOperationContext operationContext,
            ApiOperationDescriptor descriptor,
            object operation)
        {
            var userContext = operationContext.UserAuthorisationContext;
            if (userContext is null)
            {
                return ExecutionAllowed.YesTask;
            }

            var tenantId = operation.GetType().GetProperty("TenantId")?.GetValue(operation)?.ToString();
            if (tenantId is null)
            {
                return ExecutionAllowed.YesTask;
            }

            if (string.Equals(userContext.AccountId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionAllowed.YesTask;
            }

            return Task.FromResult(
                ExecutionAllowed.No(
                    $"Tenant isolation: account {userContext.AccountId} cannot access tenant {tenantId}",
                    "tenant-isolation",
                    ExecutionAllowedFailureType.Authorisation));
        }
    }

    /// <summary>
    /// Minimal S365-style user context that carries AccountId from the JWT's sig365_tenant_id claim.
    /// </summary>
    private sealed class S365UserContext(string accountId) : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => $"user-{accountId}";

        public string IdentityId => $"identity-{accountId}";

        public string AccountId => accountId;

        public string Email => $"{accountId}@test.com";

        public string Name => $"Test User ({accountId})";

        public void PopulateMetadata(Action<string, object> add)
        {
        }
    }

    /// <summary>
    /// Test double for <see cref="ITenantAccessor"/> — mirrors S365's AsyncLocal-based accessor.
    /// </summary>
    private sealed class TestTenantAccessor : ITenantAccessor
    {
        public string? Current { get; set; }
    }

    /// <summary>
    /// Test double for <see cref="ITenantResolver"/> — extracts AccountId from the principal's claims.
    /// <see cref="PipelineHelpers.ResolveTenant"/> feeds this return value into <see cref="ITenantAccessor.Current"/>.
    /// </summary>
    private sealed class TestTenantResolver : ITenantResolver
    {
        public string? ResolveTenantId(System.Security.Claims.ClaimsPrincipal principal)
        {
            return principal.FindFirst("AccountId")?.Value;
        }
    }
}

/// <summary>
/// S365-style tenant-scoped command with Resource authorization.
/// The resource key template <c>/tenants/{TenantId}</c> is expanded at runtime.
/// </summary>
[MustBeAuthenticated]
[Resource("template", "create", "/tenants/{TenantId}")]
[RootLink("/tenants/{TenantId}/templates")]
public partial class TenantScopedCommand : ICommand<string>
{
    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(TenantScopedCommand operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"created-for-{operation.TenantId}");
    }
}

/// <summary>
/// S365-style account-scoped query using <c>{Auth.AccountId}</c> token expansion.
/// The token is resolved from the authenticated principal's claims, not from operation properties.
/// </summary>
[MustBeAuthenticated]
[Resource("account", "read", "/accounts/{Auth.AccountId}")]
[RootLink("/accounts/me")]
public partial class TenantAccountQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(TenantAccountQuery operation, IUserAuthorisationContext userContext)
    {
        HandlerCallCount++;
        return Task.FromResult($"account-{userContext.AccountId}");
    }
}

/// <summary>
/// Mirrors S365's <c>ApiId</c> — a strongly-typed tenant identifier that implements
/// <c>IParsable&lt;T&gt;</c> for route/query/body binding and compile-time TenantModule parsing.
/// </summary>
public readonly struct ParsableTenantId : IParsable<ParsableTenantId>, IEquatable<ParsableTenantId>
{
    public string Value { get; }

    public ParsableTenantId(string value)
    {
        this.Value = value;
    }

    public static ParsableTenantId Parse(string s, IFormatProvider? provider)
    {
        return new(s);
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out ParsableTenantId result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        result = new ParsableTenantId(s);
        return true;
    }

    public bool Equals(ParsableTenantId other)
    {
        return string.Equals(this.Value, other.Value, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ParsableTenantId other && this.Equals(other);
    }

    public override int GetHashCode()
    {
        return this.Value?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    public override string ToString()
    {
        return this.Value ?? string.Empty;
    }
}

/// <summary>
/// Proves TenantModule auto-populates a non-string <c>ParsableTenantId TenantId</c> from
/// <see cref="Blueprint.Tenancy.ITenantAccessor"/> when TenantId is NOT in the route template.
/// Mirrors the 165 S365 operations where <c>ApiId TenantId</c> comes from JWT claims.
/// </summary>
[MustBeAuthenticated]
[RootLink("/overview")]
public partial class ParsableTenantOverviewQuery : IQuery<string>
{
    public ParsableTenantId TenantId { get; set; }

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(ParsableTenantOverviewQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"overview-{operation.TenantId}");
    }
}
