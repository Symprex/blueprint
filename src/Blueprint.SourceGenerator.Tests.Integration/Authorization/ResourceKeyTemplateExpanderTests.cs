using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Integration tests for <see cref="ResourceKeyTemplateExpander"/> proving that resource key
/// templates with <c>{PropertyName}</c> and <c>{Auth.ClaimType}</c> tokens are correctly expanded.
/// </summary>
public class ResourceKeyTemplateExpanderTests
{
    [Fact]
    public void Expand_substitutes_property_tokens_from_operation()
    {
        var template = "/Tenants/{TenantId}/Campaigns/{CampaignId}";
        var operation = new SampleTenantOperation { TenantId = "T-100", CampaignId = "C-200" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Tenants/T-100/Campaigns/C-200");
    }

    [Fact]
    public void Expand_substitutes_auth_tokens_from_claims()
    {
        var template = "/Tenants/{Auth.AccountId}/Items/{Id}";
        var operation = new SampleTenantOperation { Id = "I-300" };
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("AccountId", "A-999")], "Test");
        var principal = new ClaimsPrincipal(identity);

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Tenants/A-999/Items/I-300");
    }

    [Fact]
    public void Expand_leaves_unresolvable_tokens_as_is()
    {
        var template = "/Tenants/{Auth.Missing}/Items/{NonExistent}";
        var operation = new SampleTenantOperation { TenantId = "T-100" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Tenants/{Auth.Missing}/Items/{NonExistent}");
    }

    [Fact]
    public void Expand_handles_mixed_auth_and_property_tokens()
    {
        var template = "/Accounts/{Auth.AccountId}/Tenants/{TenantId}/Campaigns/{CampaignId}";
        var operation = new SampleTenantOperation { TenantId = "T-1", CampaignId = "C-2" };
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("AccountId", "ACC-42")], "Test");
        var principal = new ClaimsPrincipal(identity);

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Accounts/ACC-42/Tenants/T-1/Campaigns/C-2");
    }

    [Fact]
    public void ExpandAll_returns_expanded_keys_only_for_pairs_with_templates()
    {
        var scopePairs = new ScopePair[]
        {
            new("campaign", "read"),
            new("campaign", "write", "/Tenants/{Auth.AccountId}/Campaigns/{CampaignId}"),
            new("admin", "manage"),
        };

        var operation = new SampleTenantOperation { TenantId = "T-1", CampaignId = "C-99" };
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("AccountId", "A-1")], "Test");
        var principal = new ClaimsPrincipal(identity);

        var services = new ServiceCollection().BuildServiceProvider();
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(SampleTenantOperation), "SampleTenantOperation", "test");
        app.Register(descriptor);
        var context = new Blueprint.Runtime.Exec.ApiOperationContext(services, app, descriptor, CancellationToken.None)
        {
            UserPrincipal = principal,
            Operation = operation,
        };

        var results = ResourceKeyTemplateExpander.ExpandAll(context, principal, scopePairs);

        results.Should().HaveCount(1);
        results[0].Should().Be("/Tenants/A-1/Campaigns/C-99");
    }

    [Fact]
    public void ScopePair_with_template_round_trips_correctly()
    {
        var pair = new ScopePair("document", "edit", "/Tenants/{TenantId}/Documents/{Id}");

        pair.Noun.Should().Be("document");
        pair.Verb.Should().Be("edit");
        pair.ResourceKeyTemplate.Should().Be("/Tenants/{TenantId}/Documents/{Id}");
    }

    [Fact]
    public void ScopePair_without_template_has_null_template()
    {
        var pair = new ScopePair("document", "read");

        pair.Noun.Should().Be("document");
        pair.Verb.Should().Be("read");
        pair.ResourceKeyTemplate.Should().BeNull();
    }

    [Fact]
    public void Expand_resolves_auth_AccountId_from_user_context_not_claims()
    {
        // S365 pattern: {Auth.AccountId} resolves from IUserAuthorisationContext.AccountId,
        // which is a computed property (TenantId ?? PartnerId ?? "Symprex") — not a JWT claim.
        var template = "/Tenants/{Auth.AccountId}/Items/{Id}";
        var operation = new SampleTenantOperation { Id = "I-42" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Test"));
        var userContext = new TestUserAuthorisationContext
        {
            AccountId = "computed-tenant-123",
            Id = "user-456",
        };

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal, userContext);

        result.Should().Be("/Tenants/computed-tenant-123/Items/I-42");
    }

    [Fact]
    public void Expand_resolves_auth_Id_from_user_context()
    {
        var template = "/Users/{Auth.Id}/Documents/{Id}";
        var operation = new SampleTenantOperation { Id = "D-100" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Test"));
        var userContext = new TestUserAuthorisationContext { Id = "user-789" };

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal, userContext);

        result.Should().Be("/Users/user-789/Documents/D-100");
    }

    [Fact]
    public void Expand_user_context_takes_precedence_over_claims()
    {
        // When both a context property and a claim exist, the context property wins
        var template = "/Tenants/{Auth.AccountId}";
        var operation = new SampleTenantOperation();
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("AccountId", "from-claim")], "Test");
        var principal = new ClaimsPrincipal(identity);
        var userContext = new TestUserAuthorisationContext { AccountId = "from-context" };

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal, userContext);

        result.Should().Be("/Tenants/from-context");
    }

    [Fact]
    public void Expand_falls_back_to_claims_for_non_context_auth_tokens()
    {
        // {Auth.s365_tenant_id} is not a well-known context property, so falls back to claims
        var template = "/Raw/{Auth.s365_tenant_id}";
        var operation = new SampleTenantOperation();
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("s365_tenant_id", "T-CLAIM")], "Test");
        var principal = new ClaimsPrincipal(identity);
        var userContext = new TestUserAuthorisationContext { AccountId = "ignored" };

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal, userContext);

        result.Should().Be("/Raw/T-CLAIM");
    }

    [Fact]
    public void ExpandAll_passes_user_context_through_to_Expand()
    {
        var scopePairs = new ScopePair[]
        {
            new("template", "read", "/Tenants/{Auth.AccountId}/Templates/{Id}"),
        };

        var operation = new SampleTenantOperation { Id = "TPL-1" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "Test"));

        var services = new ServiceCollection().BuildServiceProvider();
        var app = new BlueprintApp();
        var descriptor = new ApiOperationDescriptor(typeof(SampleTenantOperation), "SampleTenantOperation", "test");
        app.Register(descriptor);
        var context = new Blueprint.Runtime.Exec.ApiOperationContext(services, app, descriptor, CancellationToken.None)
        {
            UserPrincipal = principal,
            Operation = operation,
            UserAuthorisationContext = new TestUserAuthorisationContext { AccountId = "ctx-tenant" },
        };

        var results = ResourceKeyTemplateExpander.ExpandAll(context, principal, scopePairs);

        results.Should().HaveCount(1);
        results[0].Should().Be("/Tenants/ctx-tenant/Templates/TPL-1");
    }

    private class SampleTenantOperation
    {
        public string TenantId { get; set; } = string.Empty;

        public string CampaignId { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;
    }

    private class TestUserAuthorisationContext : IUserAuthorisationContext
    {
        public bool IsActive { get; set; } = true;

        public bool IsAnonymous { get; set; }

        public string Id { get; set; } = "test-user";

        public string IdentityId { get; set; } = "test-identity";

        public string AccountId { get; set; } = "test-account";

        public string Email { get; set; } = "test@example.com";

        public string Name { get; set; } = "Test User";

        public void PopulateMetadata(Action<string, object> add)
        {
            add("user_id", this.Id);
            add("account_id", this.AccountId);
        }
    }
}
