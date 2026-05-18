using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Utilities;

namespace Blueprint.Authorization;

/// <summary>
/// Expands resource key templates by substituting <c>{PropertyName}</c> tokens from the operation
/// instance and <c>{Auth.ClaimType}</c> tokens from the authenticated principal's claims.
/// </summary>
/// <remarks>
/// <para>
/// This enables the <c>[Resource("noun", "verb", "/Tenants/{Auth.AccountId}/Items/{Id}")]</c> pattern
/// where hierarchical resource keys are built from operation + auth context values.
/// </para>
/// <para>
/// Token resolution rules:
/// <list type="bullet">
///   <item><c>{Auth.PropertyName}</c> — resolved from user auth context or principal claims (dot = path)</item>
///   <item><c>{PropertyName}</c> — resolved from a public property on the operation instance</item>
///   <item><c>{Prop:Fallback}</c> — try primary, if null/empty try fallback (colon = fallback)</item>
/// </list>
/// Unresolvable tokens are left as-is (the authorization provider should reject the key).
/// </para>
/// </remarks>
public static class ResourceKeyTemplateExpander
{
    private static readonly Regex TokenRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Expands a resource key template by substituting tokens from the operation and principal.
    /// </summary>
    /// <param name="template">The resource key template containing placeholder tokens.</param>
    /// <param name="operation">The operation instance whose properties supply <c>{PropertyName}</c> values.</param>
    /// <param name="principal">The authenticated principal whose claims supply <c>{Auth.ClaimType}</c> values.</param>
    /// <param name="userContext">
    /// Optional user authorization context. When present, <c>{Auth.AccountId}</c>, <c>{Auth.Id}</c>, and
    /// <c>{Auth.IdentityId}</c> resolve from this context before falling back to claims.
    /// </param>
    /// <returns>The expanded resource key with all resolvable tokens substituted.</returns>
    [RequiresDynamicCode("Uses CompiledPropertyAccessor.GetGetter which relies on Expression.Compile() for property access delegation.")]
    public static string Expand(
        string template,
        object operation,
        ClaimsPrincipal principal,
        IUserAuthorisationContext? userContext = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(principal);

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;

            // Colon always means fallback: {Primary:Fallback}
            // Dot always means property path: {Auth.AccountId}, {TenantId.Value}
            // "Auth" as first segment routes to auth context resolution.
            var colonIndex = token.IndexOf(':');
            if (colonIndex >= 0)
            {
                var primaryPath = token[..colonIndex];
                var fallbackPath = token[(colonIndex + 1)..];

                var primaryValue = ResolvePath(operation, principal, userContext, primaryPath);
                if (!string.IsNullOrEmpty(primaryValue))
                {
                    return primaryValue;
                }

                var fallbackValue = ResolvePath(operation, principal, userContext, fallbackPath);
                return !string.IsNullOrEmpty(fallbackValue) ? fallbackValue : match.Value;
            }

            // Simple or dotted path (no fallback)
            var resolved = ResolvePath(operation, principal, userContext, token);
            return resolved ?? match.Value;
        });
    }

    /// <summary>
    /// Routes a path to either auth context resolution (if first segment is "Auth") or
    /// operation property resolution (everything else).
    /// </summary>
    [RequiresDynamicCode("Delegates to ResolvePropertyPath which uses CompiledPropertyAccessor.")]
    private static string? ResolvePath(
        object operation,
        ClaimsPrincipal principal,
        IUserAuthorisationContext? userContext,
        string path)
    {
        if (path.StartsWith("Auth.", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Auth", StringComparison.OrdinalIgnoreCase))
        {
            var authPropName = path.IndexOf('.') is var idx and >= 0
                ? path[(idx + 1)..]
                : string.Empty;

            // Try well-known IUserAuthorisationContext properties first
            if (userContext is not null)
            {
                var contextValue = ResolveFromUserContext(userContext, authPropName);
                if (contextValue is not null)
                {
                    return contextValue;
                }
            }

            var claim = principal.FindFirst(authPropName);
            return claim?.Value;
        }

        return ResolvePropertyPath(operation, path);
    }

    /// <summary>
    /// Resolves a property path (simple name or dot-separated) from an operation instance.
    /// Returns null if the property cannot be resolved.
    /// </summary>
    private static string? ResolvePropertyPath(object operation, string path)
    {
        var segments = path.Split('.');

        if (segments.Length == 1)
        {
            var getter = CompiledPropertyAccessor.GetGetter(operation.GetType(), segments[0]);
            return getter?.Invoke(operation)?.ToString();
        }

        // Navigate the dot-separated path
        var current = (object?)operation;
        for (var i = 0; i < segments.Length; i++)
        {
            if (current is null)
            {
                return null;
            }

            var getter = CompiledPropertyAccessor.GetGetter(current.GetType(), segments[i]);
            if (getter is null)
            {
                return null;
            }

            current = getter.Invoke(current);
        }

        return current?.ToString();
    }

    /// <summary>
    /// Resolves a token name against well-known <see cref="IUserAuthorisationContext"/> properties.
    /// Returns <see langword="null"/> if the token doesn't match a known property.
    /// </summary>
    private static string? ResolveFromUserContext(IUserAuthorisationContext context, string tokenName)
    {
        return tokenName switch
        {
            _ when tokenName.Equals("AccountId", StringComparison.OrdinalIgnoreCase) => context.AccountId,
            _ when tokenName.Equals("Id", StringComparison.OrdinalIgnoreCase) => context.Id,
            _ when tokenName.Equals("IdentityId", StringComparison.OrdinalIgnoreCase) => context.IdentityId,
            _ when tokenName.Equals("Email", StringComparison.OrdinalIgnoreCase) => context.Email,
            _ when tokenName.Equals("Name", StringComparison.OrdinalIgnoreCase) => context.Name,
            _ => null,
        };
    }

    /// <summary>
    /// Expands resource key templates for all scope pairs that have a template, returning the
    /// expanded keys. Scope pairs without templates are skipped.
    /// </summary>
    /// <param name="context">The operation context containing the operation instance.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="scopePairs">The scope pairs to expand.</param>
    /// <returns>A list of expanded resource keys for scope pairs that have templates.</returns>
    [RequiresDynamicCode("Delegates to Expand which uses CompiledPropertyAccessor.GetGetter (Expression.Compile).")]
    public static IReadOnlyList<string> ExpandAll(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        IReadOnlyList<Model.ScopePair> scopePairs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scopePairs);

        var results = new List<string>(scopePairs.Count);

        foreach (var pair in scopePairs)
        {
            if (pair.ResourceKeyTemplate is not null)
            {
                results.Add(Expand(pair.ResourceKeyTemplate, context.Operation, principal, context.UserAuthorisationContext));
            }
        }

        return results;
    }
}
