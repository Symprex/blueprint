using System.Collections.Immutable;

namespace Blueprint.Model;

/// <summary>
/// Feature data populated during descriptor generation with the ordered HTTP methods currently exposed for the operation.
/// </summary>
/// <param name="HttpMethods">The distinct HTTP methods exposed for the operation in deterministic order.</param>
public sealed record OperationHttpMethodMetadataFeatureData(ImmutableArray<string> HttpMethods)
{
    private string[]? _cachedArray;

    /// <summary>
    /// Returns the HTTP methods as a <c>string[]</c>, caching the result to avoid repeated
    /// <c>ImmutableArray.ToArray</c> allocations during startup endpoint registration.
    /// </summary>
    public string[] HttpMethodsArray => this._cachedArray ??= this.HttpMethods.ToArray();
}
