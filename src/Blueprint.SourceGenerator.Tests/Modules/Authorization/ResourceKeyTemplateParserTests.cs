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
}
