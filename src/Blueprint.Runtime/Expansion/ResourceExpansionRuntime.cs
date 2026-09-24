using System.Collections;
using System.Collections.Concurrent;

using Blueprint.Http;
using Blueprint.Model;
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
    /// Caches which <see cref="IResourceExpander"/> handles each expandable resource type so
    /// subsequent requests avoid the O(n) scan of all registered expanders.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Type?> _expanderTypeCache = new();

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

        if (!TryGetExpansionResponse(result, out var response))
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

        await DoExpandAsync(context, response, propertyValue, expandableResourceType, isEnumerable).ConfigureAwait(false);
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

        if (!TryGetExpansionResponse(result, out var response))
        {
            return;
        }

        await DoExpandAsync(context, response, propertyValue, expandableResourceType, isEnumerable).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the object an expander receives as its <c>response</c>: the content of an <see cref="OkResult"/>, or a
    /// <see cref="ResourceEvent"/> itself, which legacy Blueprint wrapped in an <c>OkResult</c> and so passed whole.
    /// </summary>
    /// <remarks>
    /// A resource event whose data its self-query loaded is not expanded. Legacy expanded before resolving the
    /// self-query, when that data was still null, so it was only ever expanded by the self-query's own pipeline.
    /// </remarks>
    private static bool TryGetExpansionResponse(OperationResult result, out object response)
    {
        switch (result)
        {
            case OkResult okResult:
                response = okResult.Content!;
                return true;

            case ResourceEvent { IsDataFromSelfQuery: false } resourceEvent:
                response = resourceEvent;
                return true;

            default:
                response = null!;
                return false;
        }
    }

    private static async ValueTask DoExpandAsync(
        ApiOperationContext context,
        object response,
        object propertyValue,
        Type expandableResourceType,
        bool isEnumerable)
    {
        // Fast path: if we've already determined no expander handles this type, skip immediately
        if (_expanderTypeCache.TryGetValue(expandableResourceType, out var cachedExpanderType) && cachedExpanderType is null)
        {
            return;
        }

        var expanders = context.ServiceProvider.GetServices<IResourceExpander>();
        IResourceExpander? matchingExpander = null;

        if (cachedExpanderType is not null)
        {
            // We know which concrete type handles this resource — find it directly
            foreach (var expander in expanders)
            {
                if (expander.GetType() == cachedExpanderType)
                {
                    matchingExpander = expander;
                    break;
                }
            }
        }
        else
        {
            // First encounter: scan all expanders by capability and cache the winning type
            foreach (var expander in expanders)
            {
                if (expander.CanExpand(expandableResourceType))
                {
                    matchingExpander = expander;
                    _expanderTypeCache.TryAdd(expandableResourceType, expander.GetType());
                    break;
                }
            }

            if (matchingExpander is null)
            {
                _expanderTypeCache.TryAdd(expandableResourceType, null);
                return;
            }
        }

        if (matchingExpander is null)
        {
            return;
        }

        var enumerable = isEnumerable
            ? (IEnumerable)propertyValue
            : CreateSingleItemArray(propertyValue, expandableResourceType);

        await matchingExpander.ExpandAsync(
            context,
            response,
            enumerable,
            context.OperationCancelled).ConfigureAwait(false);
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
            var span = v.AsSpan();

            if (span.IndexOf(',') >= 0)
            {
                // Walk comma-separated segments without allocating Split array
                while (span.Length > 0)
                {
                    var commaIndex = span.IndexOf(',');
                    var segment = commaIndex >= 0 ? span[..commaIndex] : span;

                    if (segment.Trim().Equals(propertyPath.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    span = commaIndex >= 0 ? span[(commaIndex + 1)..] : ReadOnlySpan<char>.Empty;
                }
            }
            else if (span.Equals(propertyPath.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a single-element array typed as <c>Expandable&lt;T&gt;[]</c> so that expanders can safely cast
    /// the <see cref="IEnumerable"/> parameter to <c>IEnumerable&lt;Expandable&lt;T&gt;&gt;</c>.
    /// </summary>
    private static IEnumerable CreateSingleItemArray(object value, Type expandableResourceType)
    {
#pragma warning disable IL3050 // Blueprint does not target Native AOT
        var expandableType = typeof(Expandable<>).MakeGenericType(expandableResourceType);
        var array = Array.CreateInstance(expandableType, 1);
#pragma warning restore IL3050
        array.SetValue(value, 0);
        return array;
    }
}
