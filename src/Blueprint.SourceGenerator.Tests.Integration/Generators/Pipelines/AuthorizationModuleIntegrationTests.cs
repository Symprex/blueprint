using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

public class AuthorizationModuleIntegrationTests
{
    [Fact]
    public void When_resource_declared_then_descriptor_has_resource_authorization_data()
    {
        var descriptor = UpdateTenantAuthorizationCommand.Configure();

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorizationData)
            .Should().BeTrue();

        authorizationData!.ScopePairs.Should().ContainSingle().Which.Should().Be(new ScopePair("tenant", "update", "/tenants/{TenantId}"));
    }

    [Fact]
    public async Task When_resource_authorization_has_matching_claim_then_pipeline_allows_execution()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResolvedResourceAuthorizationProvider, MatchingClaimResolvedResourceAuthorizationProvider>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(UpdateTenantAuthorizationCommand));
        var context = CreateHttpContext(
            app,
            UpdateTenantAuthorizationCommand.Configure(),
            new UpdateTenantAuthorizationCommand { TenantId = "tenant-42" },
            CreateAuthenticatedPrincipal(new System.Security.Claims.Claim("resource-access", "tenant:update:/tenants/tenant-42")),
            serviceProvider);

        UpdateTenantAuthorizationCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("tenant-42");
        UpdateTenantAuthorizationCommand.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_operation_allows_anonymous_then_authorization_checks_are_skipped()
    {
        var countingAuthoriser = new CountingDenyingApiAuthoriser();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IApiAuthoriser>(countingAuthoriser)
            .BuildServiceProvider();
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(AllowAnonymousAuthorizationQuery));
        var context = CreateHttpContext(
            app,
            AllowAnonymousAuthorizationQuery.Configure(),
            new AllowAnonymousAuthorizationQuery(),
            CreateAnonymousPrincipal(),
            serviceProvider);

        AllowAnonymousAuthorizationQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("anonymous");
        AllowAnonymousAuthorizationQuery.HandlerCallCount.Should().Be(1);
        countingAuthoriser.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_resource_with_claim_token_then_expanded_key_uses_principal_claim()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResolvedResourceAuthorizationProvider, MatchingClaimResolvedResourceAuthorizationProvider>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(GetMyAccountAuthorizationQuery));

        // The {Auth:AccountId} token resolves from IUserAuthorisationContext.AccountId
        // (which is "account-1" in TestUserContext), falling back to claims if no context.
        var context = CreateHttpContext(
            app,
            GetMyAccountAuthorizationQuery.Configure(),
            new GetMyAccountAuthorizationQuery(),
            CreateAuthenticatedPrincipal(
                new System.Security.Claims.Claim("AccountId", "account-1"),
                new System.Security.Claims.Claim("resource-access", "account:read:/accounts/account-1")),
            serviceProvider);

        GetMyAccountAuthorizationQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        GetMyAccountAuthorizationQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_resource_with_claim_token_and_wrong_claim_then_forbidden()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResolvedResourceAuthorizationProvider, MatchingClaimResolvedResourceAuthorizationProvider>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(GetMyAccountAuthorizationQuery));

        // AccountId resolves from context ("account-1") but resource-access claim expects wrong key
        var context = CreateHttpContext(
            app,
            GetMyAccountAuthorizationQuery.Configure(),
            new GetMyAccountAuthorizationQuery(),
            CreateAuthenticatedPrincipal(
                new System.Security.Claims.Claim("AccountId", "account-1"),
                new System.Security.Claims.Claim("resource-access", "account:read:/accounts/acct-WRONG")),
            serviceProvider);

        GetMyAccountAuthorizationQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ForbiddenResult>();
        GetMyAccountAuthorizationQuery.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_resource_with_property_path_token_then_sub_property_is_expanded()
    {
        var app = new IntegrationTestBlueprintApp();
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResolvedResourceAuthorizationProvider, MatchingClaimResolvedResourceAuthorizationProvider>()
            .BuildServiceProvider();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, serviceProvider);
        var pipeline = executor.GetPipeline(typeof(GetBillingAuthorizationQuery));

        // The {TenantId:Id} should expand to the .Id sub-property of TenantId
        var context = CreateHttpContext(
            app,
            GetBillingAuthorizationQuery.Configure(),
            new GetBillingAuthorizationQuery { TenantId = new TypedId { Id = "billing-77" } },
            CreateAuthenticatedPrincipal(
                new System.Security.Claims.Claim("resource-access", "billing:read:/tenants/billing-77")),
            serviceProvider);

        GetBillingAuthorizationQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("billing-billing-77");
        GetBillingAuthorizationQuery.HandlerCallCount.Should().Be(1);
    }

    private static ApiOperationContext CreateHttpContext(
        BlueprintApp app,
        ApiOperationDescriptor descriptor,
        object operation,
        System.Security.Claims.ClaimsPrincipal principal,
        IServiceProvider? serviceProvider = null)
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

        var context = new ApiOperationContext(serviceProvider ?? new ServiceCollection().BuildServiceProvider(), app, descriptor, CancellationToken.None)
        {
            Operation = operation,
            HttpContext = httpContext,
            UserPrincipal = principal,
            UserAuthorisationContext = principal.Identity?.IsAuthenticated == true ? new TestUserContext() : null,
        };
        return context;
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAuthenticatedPrincipal(params System.Security.Claims.Claim[] claims)
    {
        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "IntegrationTestAuth"));
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAnonymousPrincipal()
    {
        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
    }

    private sealed class TestUserContext : IUserAuthorisationContext
    {
        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => "user-1";

        public string IdentityId => "identity-1";

        public string AccountId => "account-1";

        public string Email => "user@example.com";

        public string Name => "Integration User";

        public void PopulateMetadata(Action<string, object> add)
        {
        }
    }

    private sealed class MatchingClaimResolvedResourceAuthorizationProvider : IResolvedResourceAuthorizationProvider
    {
        public Task<bool> IsAuthorizedAsync(
            ApiOperationContext operationContext,
            System.Security.Claims.ClaimsPrincipal principal,
            IReadOnlyList<ResolvedScopePair> resolvedScopePairs,
            CancellationToken cancellationToken)
        {
            var isAuthorized = resolvedScopePairs.Any(scopePair => principal.HasClaim("resource-access", $"{scopePair.Noun}:{scopePair.Verb}:{scopePair.ExpandedResourceKey}"));
            return Task.FromResult(isAuthorized);
        }
    }

    private sealed class CountingDenyingApiAuthoriser : IApiAuthoriser
    {
        public int CallCount { get; private set; }

        public bool AppliesTo(ApiOperationDescriptor descriptor)
        {
            return true;
        }

        public Task<ExecutionAllowed> CanExecuteOperationAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object operation)
        {
            this.CallCount++;
            return Task.FromResult(ExecutionAllowed.No("denied", "denied", ExecutionAllowedFailureType.Authorisation));
        }

        public Task<ExecutionAllowed> CanShowLinkAsync(ApiOperationContext operationContext, ApiOperationDescriptor descriptor, object resource)
        {
            return Task.FromResult(ExecutionAllowed.Yes);
        }
    }
}

[Resource("tenant", "update", "/tenants/{TenantId}")]
[RootLink("/authorization/tenants/{TenantId}/update")]
public partial class UpdateTenantAuthorizationCommand : ICommand<string>
{
    public string TenantId { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(UpdateTenantAuthorizationCommand operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.TenantId);
    }
}

[AllowAnonymous]
[RootLink("/authorization/public")]
public partial class AllowAnonymousAuthorizationQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(AllowAnonymousAuthorizationQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult("anonymous");
    }
}

/// <summary>
/// Tests resource authorization with Auth: claim token in the resource key template.
/// The {Auth:AccountId} token is expanded from the principal's claims, not from operation properties.
/// </summary>
[Resource("account", "read", "/accounts/{Auth:AccountId}")]
[RootLink("/authorization/accounts/me")]
public partial class GetMyAccountAuthorizationQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(GetMyAccountAuthorizationQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult("my-account");
    }
}

/// <summary>
/// Tests resource authorization with a property path token ({TenantId:Id})
/// where TenantId is a complex type and .Id accesses its sub-property.
/// </summary>
[Resource("billing", "read", "/tenants/{TenantId:Id}")]
[RootLink("/authorization/billing/{TenantId}")]
public partial class GetBillingAuthorizationQuery : IQuery<string>
{
    public TypedId TenantId { get; set; } = new();

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(GetBillingAuthorizationQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult($"billing-{operation.TenantId.Id}");
    }
}

/// <summary>
/// Simple complex-type wrapper to exercise property path tokens in resource key templates.
/// Implements <see cref="IParsable{T}"/> so Blueprint can bind it from route/query string values.
/// </summary>
public class TypedId : IParsable<TypedId>
{
    public string Id { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string ToString()
    {
        return this.Id;
    }

    /// <inheritdoc />
    public static TypedId Parse(string s, IFormatProvider? provider)
    {
        return new TypedId { Id = s };
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, out TypedId result)
    {
        result = new TypedId { Id = s ?? string.Empty };
        return true;
    }
}
