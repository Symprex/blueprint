using System.Security.Claims;
using Blueprint.Authorization;
using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Integration tests for <see cref="ResourceKeyTemplateExpander"/> proving that resource key
/// templates with <c>{PropertyName}</c> and <c>{Auth:ClaimType}</c> tokens are correctly expanded.
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
        var template = "/Tenants/{Auth:AccountId}/Items/{Id}";
        var operation = new SampleTenantOperation { Id = "I-300" };
        var identity = new ClaimsIdentity([new System.Security.Claims.Claim("AccountId", "A-999")], "Test");
        var principal = new ClaimsPrincipal(identity);

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Tenants/A-999/Items/I-300");
    }

    [Fact]
    public void Expand_leaves_unresolvable_tokens_as_is()
    {
        var template = "/Tenants/{Auth:Missing}/Items/{NonExistent}";
        var operation = new SampleTenantOperation { TenantId = "T-100" };
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = ResourceKeyTemplateExpander.Expand(template, operation, principal);

        result.Should().Be("/Tenants/{Auth:Missing}/Items/{NonExistent}");
    }

    [Fact]
    public void Expand_handles_mixed_auth_and_property_tokens()
    {
        var template = "/Accounts/{Auth:AccountId}/Tenants/{TenantId}/Campaigns/{CampaignId}";
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
            new("campaign", "write", "/Tenants/{Auth:AccountId}/Campaigns/{CampaignId}"),
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

    private class SampleTenantOperation
    {
        public string TenantId { get; set; } = string.Empty;

        public string CampaignId { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;
    }
}
