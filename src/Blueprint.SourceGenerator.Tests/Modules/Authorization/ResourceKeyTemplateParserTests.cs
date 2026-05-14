using Blueprint.SourceGenerator.Modules.Authorization;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Modules.Authorization;

/// <summary>
/// Verifies compile-time template token parsing and C# expression generation for
/// resource key expansion without runtime reflection.
/// </summary>
public class ResourceKeyTemplateParserTests
{
    [Fact]
    public void ParseTokens_with_no_tokens_returns_empty()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/static/path");

        tokens.Should().BeEmpty();
    }

    [Fact]
    public void ParseTokens_with_single_property_token_extracts_name()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("TenantId");
        tokens[0].IsClaimToken.Should().BeFalse();
        tokens[0].RawToken.Should().Be("{TenantId}");
    }

    [Fact]
    public void ParseTokens_with_claim_token_extracts_claim_type()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth:AccountId}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("AccountId");
        tokens[0].IsClaimToken.Should().BeTrue();
        tokens[0].RawToken.Should().Be("{Auth:AccountId}");
    }

    [Fact]
    public void ParseTokens_with_mixed_tokens_preserves_order()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth:AccountId}/users/{UserId}");

        tokens.Should().HaveCount(2);
        tokens[0].IsClaimToken.Should().BeTrue();
        tokens[0].Name.Should().Be("AccountId");
        tokens[1].IsClaimToken.Should().BeFalse();
        tokens[1].Name.Should().Be("UserId");
    }

    [Fact]
    public void GenerateExpansionExpression_with_no_tokens_returns_literal()
    {
        var template = "/static/path";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Be("\"/static/path\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_property_token_uses_direct_access()
    {
        var template = "/tenants/{TenantId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("operation.TenantId");
        expression.Should().StartWith("$\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_claim_token_uses_FindFirst()
    {
        var template = "/tenants/{Auth:AccountId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("principal.FindFirst(\"AccountId\")");
        expression.Should().Contain("?.Value ?? \"\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_complex_template_produces_interpolation()
    {
        var template = "/tenants/{Auth:AccountId}/users/{UserId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        // Both tokens replaced with typed expressions
        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value ?? \"\"");
        expression.Should().Contain("operation.UserId?.ToString() ?? \"\"");
        expression.Should().StartWith("$\"");
        expression.Should().Contain("/tenants/");
        expression.Should().Contain("/users/");
    }

    // ── Property path token tests ─────────────────────────────────────

    [Fact]
    public void ParseTokens_with_property_path_token_extracts_property_and_sub_property()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId:Id}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("TenantId");
        tokens[0].IsClaimToken.Should().BeFalse();
        tokens[0].SubPropertyName.Should().Be("Id");
        tokens[0].RawToken.Should().Be("{TenantId:Id}");
    }

    [Fact]
    public void ParseTokens_simple_property_has_null_sub_property()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId}");

        tokens.Should().HaveCount(1);
        tokens[0].SubPropertyName.Should().BeNull();
    }

    [Fact]
    public void ParseTokens_auth_token_has_null_sub_property()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth:AccountId}");

        tokens.Should().HaveCount(1);
        tokens[0].IsClaimToken.Should().BeTrue();
        tokens[0].SubPropertyName.Should().BeNull();
    }

    [Fact]
    public void GenerateExpansionExpression_with_property_path_uses_sub_property_access()
    {
        var template = "/tenants/{TenantId:Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        // Should access operation.TenantId.Id (the sub-property), not operation.TenantId:Id
        expression.Should().Contain("operation.TenantId?.Id");
        expression.Should().NotContain("TenantId:Id");
        expression.Should().StartWith("$\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_mixed_path_and_claim_tokens()
    {
        var template = "/tenants/{Auth:AccountId}/products/{TenantId:Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value ?? \"\"");
        expression.Should().Contain("operation.TenantId?.Id");
    }

    // ── Context-aware claim resolution tests ─────────────────────────────

    [Fact]
    public void GenerateExpansionExpression_with_context_resolves_AccountId_from_context()
    {
        var template = "/tenants/{Auth:AccountId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        // Should resolve from context.UserAuthorisationContext first, then fall back to claims
        expression.Should().Contain("context.UserAuthorisationContext?.AccountId");
        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value");
    }

    [Fact]
    public void GenerateExpansionExpression_with_context_resolves_Id_from_context()
    {
        var template = "/users/{Auth:Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        expression.Should().Contain("context.UserAuthorisationContext?.Id");
    }

    [Fact]
    public void GenerateExpansionExpression_with_context_falls_back_for_unknown_claim()
    {
        var template = "/raw/{Auth:s365_tenant_id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        // s365_tenant_id is NOT a well-known context property, so no context resolution
        expression.Should().NotContain("UserAuthorisationContext");
        expression.Should().Contain("principal.FindFirst(\"s365_tenant_id\")?.Value");
    }

    [Fact]
    public void GenerateExpansionExpression_without_context_uses_claims_only()
    {
        var template = "/tenants/{Auth:AccountId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        // No context expression — backwards compatible
        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().NotContain("UserAuthorisationContext");
        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value");
    }

    // ── Value-type property expansion tests ─────────────────────────────

    [Fact]
    public void GenerateExpansionExpression_value_type_property_uses_direct_ToString()
    {
        var template = "/tenants/{TenantId}/items/{Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "TenantId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", valueTypePropertyNames: valueTypes);

        // Value type: .ToString() without null-conditional
        expression.Should().Contain("operation.TenantId.ToString()");
        expression.Should().NotContain("operation.TenantId?.");

        // Reference type: ?.ToString() ?? ""
        expression.Should().Contain("operation.Id?.ToString() ?? \"\"");
    }

    [Fact]
    public void GenerateExpansionExpression_value_type_sub_property_uses_direct_access()
    {
        var template = "/tenants/{TenantId:Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "TenantId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", valueTypePropertyNames: valueTypes);

        // Value type parent: operation.TenantId.Id (no ?. on TenantId, but sub-property still nullable)
        expression.Should().Contain("operation.TenantId.Id?.ToString()");
        expression.Should().NotContain("operation.TenantId?.");
    }

    [Fact]
    public void GenerateExpansionExpression_without_value_type_set_uses_nullable_access()
    {
        var template = "/tenants/{TenantId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        // No valueTypePropertyNames — defaults to nullable access for all properties
        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("operation.TenantId?.ToString() ?? \"\"");
    }

    [Fact]
    public void GenerateExpansionExpression_mixed_value_and_ref_types()
    {
        var template = "/tenants/{Auth:AccountId}/items/{ItemId}/sub/{SubId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "SubId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context", valueTypes);

        // Auth token: context-aware claim resolution (unaffected by value types)
        expression.Should().Contain("context.UserAuthorisationContext?.AccountId");

        // Ref type: nullable access
        expression.Should().Contain("operation.ItemId?.ToString() ?? \"\"");

        // Value type: direct ToString
        expression.Should().Contain("operation.SubId.ToString()");
        expression.Should().NotContain("operation.SubId?.");
    }
}
