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
    public void When_permission_declared_then_descriptor_requires_authentication_and_preserves_claim_requirement()
    {
        var descriptor = ManageUsersAuthorizationQuery.Configure();

        descriptor.TryGetFeatureData<OperationAuthenticationFeatureData>(out var authData)
            .Should().BeTrue();
        authData!.Requirement.Should().Be(AuthenticationRequirement.Authenticated);

        var claimRequirement = descriptor.TypeAttributes.OfType<ClaimRequiredAttribute>().Should().ContainSingle().Subject;
        claimRequirement.ValueType.Should().Be("ManageUsers");
        claimRequirement.ValueTemplate.Should().Be("*");
    }

    [Fact]
    public void When_permission_for_declared_then_claim_requirement_expands_operation_properties()
    {
        var descriptor = EditTenantAuthorizationCommand.Configure();
        var claimRequirement = descriptor.TypeAttributes.OfType<ClaimRequiredAttribute>().Should().ContainSingle().Subject;

        var claim = claimRequirement.GetClaim(new EditTenantAuthorizationCommand { TenantId = "tenant-42" });

        claim.Type.Should().Be(Blueprint.Runtime.Authorization.ClaimTypes.Permission);
        claim.ValueType.Should().Be("Edit");
        claim.Value.Should().Be("Tenant/tenant-42");
    }

    [Fact]
    public void When_resource_declared_then_descriptor_has_resource_authorization_data()
    {
        var descriptor = UpdateTenantAuthorizationCommand.Configure();

        descriptor.TryGetFeatureData<OperationAuthorizationFeatureData>(out var authorizationData)
            .Should().BeTrue();

        authorizationData!.ScopePairs.Should().ContainSingle().Which.Should().Be(new ScopePair("tenant", "update", "/tenants/{TenantId}"));
    }

    [Fact]
    public async Task When_claims_authorization_has_valid_claims_then_pipeline_allows_execution()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(ManageUsersAuthorizationQuery));
        var serviceProvider = CreateClaimsAuthorizationServiceProvider();
        var context = CreateHttpContext(
            app,
            ManageUsersAuthorizationQuery.Configure(),
            new ManageUsersAuthorizationQuery(),
            CreateAuthenticatedPrincipal(new System.Security.Claims.Claim(Blueprint.Runtime.Authorization.ClaimTypes.Permission, "*", "ManageUsers")),
            serviceProvider);

        ManageUsersAuthorizationQuery.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<OkResult<string>>();
        ((OkResult<string>)result).Content.Should().Be("authorized");
        ManageUsersAuthorizationQuery.HandlerCallCount.Should().Be(1);
    }

    [Fact]
    public async Task When_claims_authorization_has_missing_claims_then_pipeline_returns_forbidden()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(EditTenantAuthorizationCommand));
        var serviceProvider = CreateClaimsAuthorizationServiceProvider();
        var context = CreateHttpContext(
            app,
            EditTenantAuthorizationCommand.Configure(),
            new EditTenantAuthorizationCommand { TenantId = "tenant-42" },
            CreateAuthenticatedPrincipal(),
            serviceProvider);

        EditTenantAuthorizationCommand.Reset();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ForbiddenResult>();
        EditTenantAuthorizationCommand.HandlerCallCount.Should().Be(0);
    }

    [Fact]
    public async Task When_resource_authorization_has_matching_claim_then_pipeline_allows_execution()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
        var pipeline = executor.GetPipeline(typeof(UpdateTenantAuthorizationCommand));
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IResolvedResourceAuthorizationProvider, MatchingClaimResolvedResourceAuthorizationProvider>()
            .BuildServiceProvider();
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
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app);
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
            UserAuthorisationContext = new TestClaimsUserContext(principal.Claims.Select(static claim => new Blueprint.Authorization.Claim(claim.Type, claim.Value, claim.ValueType)).ToArray()),
        };
        return context;
    }

    private static IServiceProvider CreateClaimsAuthorizationServiceProvider()
    {
        return new ServiceCollection()
            .AddSingleton<IClaimInspector>(_ => new Blueprint.Authorization.ClaimInspector([]))
            .BuildServiceProvider();
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAuthenticatedPrincipal(params System.Security.Claims.Claim[] claims)
    {
        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "IntegrationTestAuth"));
    }

    private static System.Security.Claims.ClaimsPrincipal CreateAnonymousPrincipal()
    {
        return new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
    }

    private sealed class TestClaimsUserContext(IReadOnlyList<Blueprint.Authorization.Claim> claims) : IUserAuthorisationContext, IClaimsHolder
    {
        public bool IsActive => true;

        public bool IsAnonymous => claims.Count == 0;

        public string Id => "user-1";

        public string IdentityId => "identity-1";

        public string AccountId => "account-1";

        public string Email => "user@example.com";

        public string Name => "Integration User";

        public IEnumerable<Blueprint.Authorization.Claim> GetClaimsByValueType(string valueType)
        {
            return claims.Where(claim => string.Equals(claim.ValueType, valueType, StringComparison.Ordinal));
        }

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

[Permission("ManageUsers")]
[RootLink("/authorization/manage-users")]
public partial class ManageUsersAuthorizationQuery : IQuery<string>
{
    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(ManageUsersAuthorizationQuery operation)
    {
        HandlerCallCount++;
        return Task.FromResult("authorized");
    }
}

[PermissionFor("Edit", "Tenant/{TenantId}")]
[RootLink("/authorization/tenants/{TenantId}")]
public partial class EditTenantAuthorizationCommand : ICommand<string>
{
    public string TenantId { get; set; } = string.Empty;

    public static int HandlerCallCount { get; private set; }

    public static void Reset()
    {
        HandlerCallCount = 0;
    }

    public static Task<string> HandleAsync(EditTenantAuthorizationCommand operation)
    {
        HandlerCallCount++;
        return Task.FromResult(operation.TenantId);
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
