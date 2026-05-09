using System.Collections;

using Blueprint.Runtime.Exec;

using Microsoft.Extensions.Primitives;

namespace Blueprint.Runtime.Expansion;

/// <summary>
/// Runtime helpers invoked by the generated resource expansion pipeline stage. The source
/// generator emits typed calls to <see cref="TryExpandAsync"/> (for client-requested expansion)
/// and <see cref="ExpandAsync"/> (for <c>[AlwaysExpand]</c> properties) with compile-time-known
/// property access and expander type.
/// </summary>
public static class ResourceExpansionRuntime
{
    private const string ExpandQueryStringParameterName = "expand";
    private const string ExpandArrayQueryStringParameterName = "expand[]";

    /// <summary>
    /// Attempts to expand an <c>Expandable&lt;T&gt;</c> property if the client requested it
    /// via <c>?expand=propertyPath</c> or <c>?expand[]=propertyPath</c> query parameters.
    /// </summary>
    /// <param name="context">The current operation context.</param>
    /// <param name="result">The operation result to check for success.</param>
    /// <param name="propertyValue">The property value (an <c>Expandable&lt;T&gt;</c> or collection of them).</param>
    /// <param name="propertyPath">The dot-separated path to the property from the response root.</param>
    /// <param name="expandableResourceType">The resource type T from <c>Expandable&lt;T&gt;</c> to find a matching expander for.</param>
    /// <param name="isEnumerable">Whether the property is a collection of <c>Expandable&lt;T&gt;</c>.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when expansion is finished or skipped.</returns>
    public static async ValueTask TryExpandAsync(
        ApiOperationContext context,
        OperationResult result,
        object? propertyValue,
        string propertyPath,
        Type expandableResourceType,
        bool isEnumerable)
    {
        if (propertyValue == null)
        {
            return;
        }

        if (result is not OkResult okResult)
        {
            return;
        }

        if (!CheckForExpandableParameters(context, out var expandables))
        {
            return;
        }

        if (!AnyExpandableMatch(propertyPath, expandables))
        {
            return;
        }

        await DoExpandAsync(context, okResult, propertyValue, expandableResourceType, isEnumerable);
    }

    /// <summary>
    /// Unconditionally expands an <c>Expandable&lt;T&gt;</c> property (used for
    /// <c>[AlwaysExpand]</c>-marked properties).
    /// </summary>
    /// <param name="context">The current operation context.</param>
    /// <param name="result">The operation result to check for success.</param>
    /// <param name="propertyValue">The property value (an <c>Expandable&lt;T&gt;</c> or collection of them).</param>
    /// <param name="expandableResourceType">The resource type T from <c>Expandable&lt;T&gt;</c> to find a matching expander for.</param>
    /// <param name="isEnumerable">Whether the property is a collection of <c>Expandable&lt;T&gt;</c>.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when expansion is finished or skipped.</returns>
    public static async ValueTask ExpandAsync(
        ApiOperationContext context,
        OperationResult result,
        object? propertyValue,
        Type expandableResourceType,
        bool isEnumerable)
    {
        if (propertyValue == null)
        {
            return;
        }

        if (result is not OkResult okResult)
        {
            return;
        }

        await DoExpandAsync(context, okResult, propertyValue, expandableResourceType, isEnumerable);
    }

    private static async ValueTask DoExpandAsync(
        ApiOperationContext context,
        OkResult okResult,
        object propertyValue,
        Type expandableResourceType,
        bool isEnumerable)
    {
        var expanders = context.ServiceProvider.GetServices<IResourceExpander>();
        IResourceExpander? matchingExpander = null;

        foreach (var expander in expanders)
        {
            if (expander.CanExpand(expandableResourceType))
            {
                matchingExpander = expander;
                break;
            }
        }

        if (matchingExpander is null)
        {
            return;
        }

        var enumerable = isEnumerable
            ? (IEnumerable)propertyValue
            : new[] { propertyValue };

        await matchingExpander.ExpandAsync(
            context,
            okResult.Content!,
            enumerable,
            context.OperationCancelled);
    }

    private static bool CheckForExpandableParameters(ApiOperationContext context, out StringValues expandables)
    {
        var queryCollection = context.HttpContext?.Request.Query;

        if (queryCollection?.TryGetValue(ExpandQueryStringParameterName, out var singular) == true)
        {
            expandables = singular;
            return true;
        }

        if (queryCollection?.TryGetValue(ExpandArrayQueryStringParameterName, out var array) == true)
        {
            expandables = array;
            return true;
        }

        expandables = StringValues.Empty;
        return false;
    }

    private static bool AnyExpandableMatch(string propertyPath, StringValues expandables)
    {
        foreach (var v in expandables)
        {
            if (v is null)
            {
                continue;
            }

            // Support both repeated keys (?expand=A&expand=B) and comma-separated (?expand=A,B)
            if (v.Contains(','))
            {
                foreach (var part in v.Split(','))
                {
                    if (part.Trim().Equals(propertyPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            else if (v.Equals(propertyPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
