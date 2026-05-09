using Blueprint.Authorization;
using Blueprint.Runtime.Authorization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Integration tests for the claims-based authorization system including
/// <see cref="ClaimsRequiredApiAuthoriser"/>, <see cref="ClaimInspector"/>,
/// and the <see cref="PermissionAttribute"/> / <see cref="PermissionForAttribute"/> attributes.
/// </summary>
public class ClaimsAuthorisationIntegrationTests
{
    [Fact]
    public void PermissionAttribute_creates_wildcard_claim()
    {
        // Arrange
        var attr = new PermissionAttribute("ManageUsers");

        // Act
        var claim = attr.GetClaim(new object());

        // Assert
        claim.Type.Should().Be(ClaimTypes.Permission);
        claim.Value.Should().Be("*");
        claim.ValueType.Should().Be("ManageUsers");
    }

    [Fact]
    public void PermissionForAttribute_resolves_property_tokens()
    {
        // Arrange
        var attr = new PermissionForAttribute("Edit", "Tenant/{TenantId}/Document/{DocId}");
        var operation = new SampleResourceOperation { TenantId = "T123", DocId = "D456" };

        // Act
        var claim = attr.GetClaim(operation);

        // Assert
        claim.Type.Should().Be(ClaimTypes.Permission);
        claim.Value.Should().Be("Tenant/T123/Document/D456");
        claim.ValueType.Should().Be("Edit");
    }

    [Fact]
    public void PermissionForAttribute_uses_fallback_property_when_primary_is_null()
    {
        // Arrange
        var attr = new PermissionForAttribute("View", "Resource/{PrimaryId:FallbackId}");
        var operation = new FallbackOperation { PrimaryId = null!, FallbackId = "FB99" };

        // Act
        var claim = attr.GetClaim(operation);

        // Assert
        claim.Value.Should().Be("Resource/FB99");
    }

    [Fact]
    public void ClaimInspector_grants_when_user_holds_exact_match()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Tenant/123/Doc/456", "Edit"),
        });
        var demand = new Claim(ClaimTypes.Permission, "Tenant/123/Doc/456", "Edit");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeTrue();
    }

    [Fact]
    public void ClaimInspector_grants_when_user_holds_wildcard()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "*", "ManageUsers"),
        });
        var demand = new Claim(ClaimTypes.Permission, "Tenant/123/User/456", "ManageUsers");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeTrue();
    }

    [Fact]
    public void ClaimInspector_grants_when_demand_is_wildcard_and_user_holds_any()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Tenant/123", "ViewReport"),
        });
        var demand = new Claim(ClaimTypes.Permission, "*", "ViewReport");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeTrue();
    }

    [Fact]
    public void ClaimInspector_grants_hierarchical_prefix_match()
    {
        // Arrange — user has permission at Account level, demand is for a specific user within that account
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Account/6", "ManageUsers"),
        });
        var demand = new Claim(ClaimTypes.Permission, "Account/6/Team/5/User/1", "ManageUsers");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeTrue();
    }

    [Fact]
    public void ClaimInspector_denies_when_no_matching_claim()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Tenant/OTHER", "Edit"),
        });
        var demand = new Claim(ClaimTypes.Permission, "Tenant/123/Doc/456", "Edit");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeFalse();
    }

    [Fact]
    public void ClaimInspector_denies_when_wrong_value_type()
    {
        // Arrange — user has "View" but demand is "Edit"
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Tenant/123/Doc/456", "View"),
        });
        var demand = new Claim(ClaimTypes.Permission, "Tenant/123/Doc/456", "Edit");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeFalse();
    }

    [Fact]
    public void ClaimInspector_uses_resource_key_expander_for_hierarchical_resolution()
    {
        // Arrange — user has "Account/6" but demand is "User/1". Expander resolves "User/1" → "Account/6/Team/5/User/1"
        var expander = new TestResourceKeyExpander("User/1", "Account/6/Team/5/User/1");
        var inspector = new ClaimInspector(new[] { expander });
        var userClaims = new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Account/6", "ManageUsers"),
        });
        var demand = new Claim(ClaimTypes.Permission, "User/1", "ManageUsers");

        // Act & Assert
        inspector.IsDemandedClaimFulfilled(userClaims, demand).Should().BeTrue();
    }

    [Fact]
    public async Task ClaimsRequiredApiAuthoriser_denies_when_user_lacks_permission()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var authoriser = new ClaimsRequiredApiAuthoriser(inspector);

        var descriptor = CreateDescriptor<ProtectedOperation>();
        var context = CreateContext(new ClaimsHolder(Array.Empty<Claim>()));
        var operation = new ProtectedOperation { TenantId = "T1", ItemId = "I1" };

        // Act
        var result = await authoriser.CanExecuteOperationAsync(context, descriptor, operation);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
    }

    [Fact]
    public async Task ClaimsRequiredApiAuthoriser_allows_when_user_holds_required_permission()
    {
        // Arrange
        var inspector = new ClaimInspector(Array.Empty<IResourceKeyExpander>());
        var authoriser = new ClaimsRequiredApiAuthoriser(inspector);

        var descriptor = CreateDescriptor<ProtectedOperation>();
        var context = CreateContext(new ClaimsHolder(new[]
        {
            new Claim(ClaimTypes.Permission, "Tenant/T1/Item/I1", "Edit"),
        }));
        var operation = new ProtectedOperation { TenantId = "T1", ItemId = "I1" };

        // Act
        var result = await authoriser.CanExecuteOperationAsync(context, descriptor, operation);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    private static Blueprint.Model.ApiOperationDescriptor CreateDescriptor<T>()
    {
        return new Blueprint.Model.ApiOperationDescriptor(typeof(T), typeof(T).Name, "test");
    }

    private static Blueprint.Runtime.Exec.ApiOperationContext CreateContext(IClaimsHolder claimsHolder)
    {
        var descriptor = new Blueprint.Model.ApiOperationDescriptor(typeof(ProtectedOperation), nameof(ProtectedOperation), "test");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new Blueprint.Runtime.Exec.ApiOperationContext(
            services,
            new Blueprint.SourceGenerator.Sample.SampleBlueprintApp(),
            descriptor,
            CancellationToken.None)
        {
            UserAuthorisationContext = new TestClaimsUserContext(claimsHolder),
        };
        return context;
    }

    [PermissionFor("Edit", "Tenant/{TenantId}/Item/{ItemId}")]
    private sealed class ProtectedOperation
    {
        public string TenantId { get; set; } = string.Empty;

        public string ItemId { get; set; } = string.Empty;
    }

    private sealed class SampleResourceOperation
    {
        public string TenantId { get; set; } = string.Empty;

        public string DocId { get; set; } = string.Empty;
    }

    private sealed class FallbackOperation
    {
        public string? PrimaryId { get; set; }

        public string FallbackId { get; set; } = string.Empty;
    }

    private sealed class TestResourceKeyExpander : IResourceKeyExpander
    {
        private readonly string _input;
        private readonly string _output;

        public TestResourceKeyExpander(string input, string output)
        {
            this._input = input;
            this._output = output;
        }

        public string? Expand(string resourceKey)
        {
            return string.Equals(resourceKey, this._input, StringComparison.Ordinal) ? this._output : null;
        }
    }

    private sealed class TestClaimsUserContext : IUserAuthorisationContext, IClaimsHolder
    {
        private readonly IClaimsHolder _claims;

        public TestClaimsUserContext(IClaimsHolder claims)
        {
            this._claims = claims;
        }

        public bool IsActive => true;

        public bool IsAnonymous => false;

        public string Id => "test-user";

        public string IdentityId => "identity-1";

        public string AccountId => "account-1";

        public string Email => "test@example.com";

        public string Name => "Test User";

        public void PopulateMetadata(Action<string, object> add)
        {
        }

        public IEnumerable<Claim> GetClaimsByValueType(string valueType)
        {
            return this._claims.GetClaimsByValueType(valueType);
        }
    }
}
