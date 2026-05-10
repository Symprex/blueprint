using System.Security.Claims;
using System.Text.RegularExpressions;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Utilities;

namespace Blueprint.Authorization;

/// <summary>
/// Expands resource key templates by substituting <c>{PropertyName}</c> tokens from the operation
/// instance and <c>{Auth:ClaimType}</c> tokens from the authenticated principal's claims.
/// </summary>
/// <remarks>
/// <para>
/// This enables the <c>[Resource("noun", "verb", "/Tenants/{Auth:AccountId}/Items/{Id}")]</c> pattern
/// from legacy Blueprint where hierarchical resource keys are built from operation + claim values.
/// </para>
/// <para>
/// Token resolution rules:
/// <list type="bullet">
///   <item><c>{Auth:ClaimType}</c> — resolved from the first matching claim on the principal</item>
///   <item><c>{PropertyName}</c> — resolved from a public property on the operation instance via compiled delegates</item>
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
    /// <param name="principal">The authenticated principal whose claims supply <c>{Auth:ClaimType}</c> values.</param>
    /// <returns>The expanded resource key with all resolvable tokens substituted.</returns>
    public static string Expand(string template, object operation, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(principal);

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;

            // {Auth:ClaimType} pattern — resolve from claims
            if (token.StartsWith("Auth:", StringComparison.OrdinalIgnoreCase))
            {
                var claimType = token[5..];
                var claim = principal.FindFirst(claimType);
                return claim?.Value ?? match.Value;
            }

            // {PropertyName} pattern — resolve from operation instance via compiled delegate
            var getter = CompiledPropertyAccessor.GetGetter(operation.GetType(), token);
            return getter?.Invoke(operation)?.ToString() ?? match.Value;
        });
    }

    /// <summary>
    /// Expands resource key templates for all scope pairs that have a template, returning the
    /// expanded keys. Scope pairs without templates are skipped.
    /// </summary>
    /// <param name="context">The operation context containing the operation instance.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="scopePairs">The scope pairs to expand.</param>
    /// <returns>A list of expanded resource keys for scope pairs that have templates.</returns>
    public static IReadOnlyList<string> ExpandAll(
        ApiOperationContext context,
        ClaimsPrincipal principal,
        IReadOnlyList<Model.ScopePair> scopePairs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(scopePairs);

        var results = new List<string>();

        foreach (var pair in scopePairs)
        {
            if (pair.ResourceKeyTemplate is not null)
            {
                results.Add(Expand(pair.ResourceKeyTemplate, context.Operation, principal));
            }
        }

        return results;
    }
}
