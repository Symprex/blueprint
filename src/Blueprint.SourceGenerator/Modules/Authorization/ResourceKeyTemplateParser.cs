using System.Text.RegularExpressions;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Represents a parsed token from a resource key template, distinguishing between
/// property tokens (<c>{PropertyName}</c>) and claim tokens (<c>{Auth:ClaimType}</c>).
/// </summary>
internal sealed record ResourceKeyToken
{
    /// <summary>The raw token text including braces (e.g., "{TenantId}" or "{Auth:AccountId}").</summary>
    public required string RawToken { get; init; }

    /// <summary>Whether this is a claim token (<c>{Auth:...}</c>).</summary>
    public required bool IsClaimToken { get; init; }

    /// <summary>
    /// For property tokens: the property name (e.g., "TenantId").
    /// For claim tokens: the claim type (e.g., "AccountId").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// For property path tokens like <c>{TenantId:Id}</c>, the sub-property name (e.g., "Id").
    /// Null for simple property tokens and claim tokens.
    /// </summary>
    public string? SubPropertyName { get; init; }
}

/// <summary>
/// Parses resource key templates at compile time to extract tokens for source-generated
/// property access instead of runtime reflection.
/// </summary>
internal static class ResourceKeyTemplateParser
{
    private static readonly Regex TokenRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Parses a resource key template and returns all tokens found.
    /// </summary>
    /// <param name="template">The resource key template string containing <c>{PropertyName}</c> and/or <c>{Auth:ClaimType}</c> tokens.</param>
    /// <returns>A list of parsed tokens extracted from the template.</returns>
    public static List<ResourceKeyToken> ParseTokens(string template)
    {
        var tokens = new List<ResourceKeyToken>();

        foreach (Match match in TokenRegex.Matches(template))
        {
            var tokenContent = match.Groups[1].Value;
            bool isClaimToken;
            string name;
            string? subPropertyName = null;

            if (tokenContent.StartsWith("Auth:", StringComparison.OrdinalIgnoreCase))
            {
                isClaimToken = true;
                name = tokenContent[5..];
            }
            else
            {
                isClaimToken = false;

                // Handle property path syntax: {PropertyName:SubProperty}
                var colonIndex = tokenContent.IndexOf(':');

                if (colonIndex >= 0)
                {
                    name = tokenContent[..colonIndex];
                    subPropertyName = tokenContent[(colonIndex + 1)..];
                }
                else
                {
                    name = tokenContent;
                }
            }

            tokens.Add(new ResourceKeyToken
            {
                RawToken = match.Value,
                IsClaimToken = isClaimToken,
                Name = name,
                SubPropertyName = subPropertyName,
            });
        }

        return tokens;
    }

    /// <summary>
    /// Generates a C# string interpolation expression that expands the resource key template
    /// inline, using direct property access for operation properties and <c>FindFirst</c> for claims.
    /// </summary>
    /// <param name="template">The resource key template string.</param>
    /// <param name="tokens">The parsed tokens from <see cref="ParseTokens"/>.</param>
    /// <param name="operationExpression">
    /// The expression that accesses the typed operation instance (e.g., <c>"operation"</c>).
    /// </param>
    /// <param name="principalExpression">
    /// The expression that accesses the <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// (e.g., <c>"principal"</c>).
    /// </param>
    /// <returns>A C# expression string that evaluates to the expanded resource key.</returns>
    public static string GenerateExpansionExpression(
        string template,
        List<ResourceKeyToken> tokens,
        string operationExpression,
        string principalExpression)
    {
        if (tokens.Count == 0)
        {
            return $"\"{template}\"";
        }

        // Build a string interpolation by replacing tokens with C# expressions
        var result = template;

        foreach (var token in tokens)
        {
            string replacement;

            if (token.IsClaimToken)
            {
                replacement = $"{{{principalExpression}.FindFirst(\"{token.Name}\")?.Value ?? \"\"}}";
            }
            else
            {
                if (token.SubPropertyName != null)
                {
                    // Property path: {TenantId:Id} → operation.TenantId?.Id?.ToString() ?? ""
                    replacement = $"{{{operationExpression}.{token.Name}?.{token.SubPropertyName}?.ToString() ?? \"\"}}";
                }
                else
                {
                    replacement = $"{{{operationExpression}.{token.Name}?.ToString() ?? \"\"}}";
                }
            }

            result = result.Replace(token.RawToken, replacement);
        }

        return $"$\"{result}\"";
    }
}
