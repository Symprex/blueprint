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
        tokens[0].IsAuthPath.Should().BeFalse();
        tokens[0].RawToken.Should().Be("{TenantId}");
    }

    [Fact]
    public void ParseTokens_with_claim_token_extracts_claim_type()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth.AccountId}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("Auth.AccountId");
        tokens[0].IsAuthPath.Should().BeTrue();
        tokens[0].RawToken.Should().Be("{Auth.AccountId}");
    }

    [Fact]
    public void ParseTokens_with_mixed_tokens_preserves_order()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth.AccountId}/users/{UserId}");

        tokens.Should().HaveCount(2);
        tokens[0].IsAuthPath.Should().BeTrue();
        tokens[0].Name.Should().Be("Auth.AccountId");
        tokens[1].IsAuthPath.Should().BeFalse();
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
        var template = "/tenants/{Auth.AccountId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("principal.FindFirst(\"AccountId\")");
        expression.Should().Contain("?.Value ?? \"\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_complex_template_produces_interpolation()
    {
        var template = "/tenants/{Auth.AccountId}/users/{UserId}";
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

    // ── Fallback token tests (colon = fallback) ─────────────────────────────────

    [Fact]
    public void ParseTokens_with_fallback_token_extracts_primary_and_fallback()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId:PartnerId}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("TenantId");
        tokens[0].IsAuthPath.Should().BeFalse();
        tokens[0].FallbackPropertyPath.Should().Be("PartnerId");
        tokens[0].RawToken.Should().Be("{TenantId:PartnerId}");
    }

    [Fact]
    public void ParseTokens_simple_property_has_null_fallback()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId}");

        tokens.Should().HaveCount(1);
        tokens[0].FallbackPropertyPath.Should().BeNull();
    }

    [Fact]
    public void ParseTokens_auth_token_has_null_fallback()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{Auth.AccountId}");

        tokens.Should().HaveCount(1);
        tokens[0].IsAuthPath.Should().BeTrue();
        tokens[0].FallbackPropertyPath.Should().BeNull();
    }

    [Fact]
    public void GenerateExpansionExpression_with_fallback_uses_null_coalescing()
    {
        var template = "/tenants/{TenantId:PartnerId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        // Should generate fallback: try TenantId first, then PartnerId
        expression.Should().Contain("operation.TenantId?.ToString()");
        expression.Should().Contain("operation.PartnerId?.ToString()");
        expression.Should().Contain("??");
        expression.Should().StartWith("$\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_mixed_fallback_and_auth_tokens()
    {
        var template = "/tenants/{Auth.AccountId}/products/{TenantId:PartnerId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value ?? \"\"");
        expression.Should().Contain("operation.TenantId?.ToString()");
        expression.Should().Contain("operation.PartnerId?.ToString()");
    }

    // ── Dot-notation property path tests ─────────────────────────────────

    [Fact]
    public void ParseTokens_with_dotted_path_extracts_full_path_as_name()
    {
        var tokens = ResourceKeyTemplateParser.ParseTokens("/tenants/{TenantId.Value}");

        tokens.Should().HaveCount(1);
        tokens[0].Name.Should().Be("TenantId.Value");
        tokens[0].IsAuthPath.Should().BeFalse();
        tokens[0].FallbackPropertyPath.Should().BeNull();
    }

    [Fact]
    public void GenerateExpansionExpression_with_dotted_path_navigates_properties()
    {
        var template = "/tenants/{TenantId.Value}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        // Should navigate: operation.TenantId?.Value?.ToString()
        expression.Should().Contain("operation.TenantId?.Value?.ToString()");
        expression.Should().StartWith("$\"");
    }

    [Fact]
    public void GenerateExpansionExpression_with_dotted_fallback_paths()
    {
        var template = "/tenants/{TenantId.Value:PartnerId.Value}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal");

        // Should use both paths with fallback
        expression.Should().Contain("operation.TenantId?.Value?.ToString()");
        expression.Should().Contain("operation.PartnerId?.Value?.ToString()");
        expression.Should().Contain("??");
    }

    [Fact]
    public void GenerateExpansionExpression_value_type_dotted_path_skips_first_null_conditional()
    {
        var template = "/tenants/{TenantId.Value}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "TenantId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", valueTypePropertyNames: valueTypes);

        // Value-type first segment: no ?. on TenantId, but sub-property gets ?.
        expression.Should().Contain("operation.TenantId.Value?.ToString()");
        expression.Should().NotContain("operation.TenantId?.");
    }

    // ── Context-aware auth resolution tests ─────────────────────────────

    [Fact]
    public void GenerateExpansionExpression_with_context_resolves_AccountId_from_context()
    {
        var template = "/tenants/{Auth.AccountId}";
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
        var template = "/users/{Auth.Id}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        expression.Should().Contain("context.UserAuthorisationContext?.Id");
    }

    [Fact]
    public void GenerateExpansionExpression_with_context_falls_back_for_unknown_claim()
    {
        var template = "/raw/{Auth.s365_tenant_id}";
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
        var template = "/tenants/{Auth.AccountId}";
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
    public void GenerateExpansionExpression_value_type_with_fallback()
    {
        var template = "/tenants/{TenantId:PartnerId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "TenantId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", valueTypePropertyNames: valueTypes);

        // Value type primary: always has a value via .ToString()
        // Fallback still uses null coalescing
        expression.Should().Contain("operation.TenantId.ToString()");
        expression.Should().Contain("operation.PartnerId?.ToString()");
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
        var template = "/tenants/{Auth.AccountId}/items/{ItemId}/sub/{SubId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);
        var valueTypes = new HashSet<string> { "SubId" };

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context", valueTypes);

        // Auth token: context-aware resolution (unaffected by value types)
        expression.Should().Contain("context.UserAuthorisationContext?.AccountId");

        // Ref type: nullable access
        expression.Should().Contain("operation.ItemId?.ToString() ?? \"\"");

        // Value type: direct ToString
        expression.Should().Contain("operation.SubId.ToString()");
        expression.Should().NotContain("operation.SubId?.");
    }

    // ── Cross-domain fallback tests (Auth ↔ operation) ────────────────────

    [Fact]
    public void GenerateExpansionExpression_auth_with_operation_fallback()
    {
        var template = "/tenants/{Auth.AccountId:TenantId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        // Primary: auth context then claims
        expression.Should().Contain("context.UserAuthorisationContext?.AccountId");
        expression.Should().Contain("principal.FindFirst(\"AccountId\")?.Value");

        // Fallback: operation property
        expression.Should().Contain("operation.TenantId?.ToString()");
    }

    [Fact]
    public void GenerateExpansionExpression_operation_with_auth_fallback()
    {
        var template = "/tenants/{TenantId:Auth.AccountId}";
        var tokens = ResourceKeyTemplateParser.ParseTokens(template);

        var expression = ResourceKeyTemplateParser.GenerateExpansionExpression(
            template, tokens, "operation", "principal", "context");

        // Primary: operation property
        expression.Should().Contain("operation.TenantId?.ToString()");

        // Fallback: auth context
        expression.Should().Contain("context.UserAuthorisationContext?.AccountId");
    }
}
