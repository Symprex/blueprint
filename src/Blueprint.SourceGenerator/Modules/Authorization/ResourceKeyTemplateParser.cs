using System.Text.RegularExpressions;

namespace Blueprint.SourceGenerator.Modules.Authorization;

/// <summary>
/// Represents a parsed token from a resource key template. All tokens use a unified syntax:
/// dot for property path navigation, colon for fallback. <c>Auth</c> is simply a well-known
/// first segment that routes resolution to the authorization context instead of the operation.
/// </summary>
/// <remarks>
/// <para>Token syntax (no special cases — two operators, one rule each):</para>
/// <list type="bullet">
///   <item><c>{Prop}</c> — simple property lookup on the operation</item>
///   <item><c>{Prop.Sub}</c> — dot-notation nested property path on the operation</item>
///   <item><c>{Prop:Fallback}</c> — try Prop, if null/empty try Fallback (colon = fallback)</item>
///   <item><c>{Auth.AccountId}</c> — resolve from auth context (Auth = virtual root)</item>
///   <item><c>{Auth.AccountId:TenantId}</c> — try auth AccountId, fallback to operation TenantId</item>
///   <item><c>{TenantId:Auth.AccountId}</c> — try operation TenantId, fallback to auth AccountId</item>
/// </list>
/// </remarks>
internal sealed record ResourceKeyToken
{
    /// <summary>The raw token text including braces (e.g., "{TenantId}" or "{Auth.AccountId}").</summary>
    public required string RawToken { get; init; }

    /// <summary>
    /// The primary property path (e.g., "TenantId", "TenantId.Value", or "Auth.AccountId").
    /// Dot-separated segments indicate nested property navigation. When the first segment is
    /// "Auth" (case-insensitive), the remaining path resolves from the authorization context.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// For fallback tokens like <c>{Prop:Fallback}</c>, the fallback property path.
    /// May contain dots for nested access (e.g., "PartnerId.Value" or "Auth.AccountId").
    /// Null when no fallback is specified.
    /// </summary>
    public string? FallbackPropertyPath { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> if the primary path resolves from the auth context
    /// (first segment is "Auth").
    /// </summary>
    public bool IsAuthPath => IsAuthPropertyPath(this.Name);

    /// <summary>
    /// Returns <see langword="true"/> if a property path starts with the "Auth." prefix,
    /// indicating it resolves from the authorization context.
    /// </summary>
    /// <param name="path">The property path to check.</param>
    /// <returns><see langword="true"/> when the first segment is "Auth".</returns>
    internal static bool IsAuthPropertyPath(string path)
    {
        return path.StartsWith("Auth.", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Auth", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the claim/property name from an auth path (everything after "Auth.").
    /// </summary>
    /// <param name="authPath">A path whose first segment is "Auth".</param>
    /// <returns>The portion after "Auth.", or empty if no dot is found.</returns>
    internal static string GetAuthPropertyName(string authPath)
    {
        return authPath.IndexOf('.') is var idx and >= 0
            ? authPath[(idx + 1)..]
            : string.Empty;
    }
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
    /// <param name="template">The resource key template string containing <c>{PropertyName}</c> and/or <c>{Auth.ClaimType}</c> tokens.</param>
    /// <returns>A list of parsed tokens extracted from the template.</returns>
    public static List<ResourceKeyToken> ParseTokens(string template)
    {
        var tokens = new List<ResourceKeyToken>();

        foreach (Match match in TokenRegex.Matches(template))
        {
            var tokenContent = match.Groups[1].Value;

            // Colon always means fallback: {Prop:Fallback} → try Prop, if null/empty try Fallback.
            // Dot always means property path navigation: {Auth.AccountId}, {TenantId.Value}
            // No special cases — "Auth" is just a well-known first path segment.
            var colonIndex = tokenContent.IndexOf(':');
            string name;
            string? fallbackPropertyPath = null;

            if (colonIndex >= 0)
            {
                name = tokenContent[..colonIndex];
                fallbackPropertyPath = tokenContent[(colonIndex + 1)..];
            }
            else
            {
                name = tokenContent;
            }

            tokens.Add(new ResourceKeyToken
            {
                RawToken = match.Value,
                Name = name,
                FallbackPropertyPath = fallbackPropertyPath,
            });
        }

        return tokens;
    }

    /// <summary>
    /// Generates a C# string interpolation expression that expands the resource key template
    /// inline. Property paths starting with "Auth." resolve from the authorization context;
    /// all other paths resolve from the operation instance.
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
    /// <param name="contextExpression">
    /// Optional expression that accesses the <c>ApiOperationContext</c> (e.g., <c>"context"</c>).
    /// When provided, well-known auth properties resolve from the user authorization context first.
    /// </param>
    /// <param name="valueTypePropertyNames">
    /// Optional set of property names whose types are value types (structs). Value-type properties
    /// use <c>.ToString()</c> without null-conditional access, avoiding CS0023 errors.
    /// </param>
    /// <returns>A C# expression string that evaluates to the expanded resource key.</returns>
    public static string GenerateExpansionExpression(
        string template,
        List<ResourceKeyToken> tokens,
        string operationExpression,
        string principalExpression,
        string? contextExpression = null,
        HashSet<string>? valueTypePropertyNames = null)
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

            var primaryExpr = GeneratePathExpression(
                token.Name, operationExpression, principalExpression, contextExpression, valueTypePropertyNames);

            if (token.FallbackPropertyPath != null)
            {
                var fallbackExpr = GeneratePathExpression(
                    token.FallbackPropertyPath, operationExpression, principalExpression, contextExpression, valueTypePropertyNames);

                replacement = $"{{{primaryExpr} ?? {fallbackExpr} ?? \"\"}}";
            }
            else
            {
                // Simple, dotted, or auth path — no fallback
                var isSimpleValueType = !token.Name.Contains('.') &&
                    !token.IsAuthPath &&
                    valueTypePropertyNames?.Contains(token.Name) == true;

                replacement = isSimpleValueType
                    ? $"{{{primaryExpr}}}"
                    : $"{{{primaryExpr} ?? \"\"}}";
            }

            result = result.Replace(token.RawToken, replacement);
        }

        return $"$\"{result}\"";
    }

    /// <summary>
    /// Generates a C# expression for a path that may be either an operation property path
    /// or an auth context path. Auth paths (first segment = "Auth") resolve from the
    /// authorization context / claims; all others resolve from the operation.
    /// Returns an expression that evaluates to <c>string?</c>.
    /// </summary>
    private static string GeneratePathExpression(
        string path,
        string operationExpression,
        string principalExpression,
        string? contextExpression,
        HashSet<string>? valueTypePropertyNames)
    {
        if (ResourceKeyToken.IsAuthPropertyPath(path))
        {
            var authPropName = ResourceKeyToken.GetAuthPropertyName(path);
            return GenerateAuthExpression(authPropName, principalExpression, contextExpression);
        }

        return GenerateOperationPropertyPathExpression(path, operationExpression, valueTypePropertyNames);
    }

    /// <summary>
    /// Generates a C# expression for an auth context / claims resolution.
    /// Well-known properties resolve from <c>IUserAuthorisationContext</c> first.
    /// </summary>
    private static string GenerateAuthExpression(
        string authPropertyName,
        string principalExpression,
        string? contextExpression)
    {
        if (contextExpression is not null && IsWellKnownContextProperty(authPropertyName))
        {
            return $"{contextExpression}.UserAuthorisationContext?.{authPropertyName} ?? {principalExpression}.FindFirst(\"{authPropertyName}\")?.Value";
        }

        return $"{principalExpression}.FindFirst(\"{authPropertyName}\")?.Value";
    }

    /// <summary>
    /// Generates a C# expression for a property path (simple or dot-separated) on the operation.
    /// Returns an expression that evaluates to <c>string?</c>.
    /// </summary>
    private static string GenerateOperationPropertyPathExpression(
        string propertyPath,
        string operationExpression,
        HashSet<string>? valueTypePropertyNames)
    {
        var segments = propertyPath.Split('.');

        if (segments.Length == 1)
        {
            // Simple property: operation.Prop?.ToString()
            var propName = segments[0];
            var isValueType = valueTypePropertyNames?.Contains(propName) == true;

            return isValueType
                ? $"{operationExpression}.{propName}.ToString()"
                : $"{operationExpression}.{propName}?.ToString()";
        }

        // Dotted path: operation.Prop?.Sub?.ToString()
        var firstSegment = segments[0];
        var firstIsValueType = valueTypePropertyNames?.Contains(firstSegment) == true;

        var sb = new System.Text.StringBuilder();
        sb.Append(operationExpression);
        sb.Append('.');
        sb.Append(firstSegment);

        if (!firstIsValueType)
        {
            sb.Append('?');
        }

        for (var i = 1; i < segments.Length; i++)
        {
            sb.Append('.');
            sb.Append(segments[i]);

            // All intermediate and final segments use null-conditional
            if (i < segments.Length - 1)
            {
                sb.Append('?');
            }
        }

        sb.Append("?.ToString()");
        return sb.ToString();
    }

    /// <summary>
    /// Returns <see langword="true"/> if the property name matches a property defined on
    /// <c>IUserAuthorisationContext</c> that should be resolved from the context rather than
    /// from JWT claims. This covers computed properties like <c>AccountId</c> which may not
    /// correspond to any single claim.
    /// </summary>
    private static bool IsWellKnownContextProperty(string name)
    {
        return name.Equals("AccountId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Id", StringComparison.OrdinalIgnoreCase)
            || name.Equals("IdentityId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Email", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Name", StringComparison.OrdinalIgnoreCase);
    }
}
