namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Where each region of a service is reached, as the options a gateway's typed client is built over.
/// </summary>
public sealed class RegionEndpoints
{
    /// <summary>
    /// Gets the base address of each region, by the region's name.
    /// </summary>
    public Dictionary<string, Uri> Regions { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The header every step that shapes a request appends its name to, so the order the steps ran in is the order of its values.
/// </summary>
internal static class RequestSteps
{
    /// <summary>
    /// The name of the header.
    /// </summary>
    public const string HeaderName = "X-Step";
}

/// <summary>
/// A typed client with the extension points a gateway's has: a readonly field, which the generated constructor takes as a
/// parameter and every clone carries; a hand-written method over it that is copied onto the interface and builds on
/// <c>WithRequestModifier</c>; and an <c>OnConfigure</c> hook run on every request.
/// </summary>
[BlueprintClient]
[BlueprintClientInclude(typeof(FindTemplateCopiesQuery))]
[BlueprintClientInclude(typeof(SignupTenantCommand))]
internal partial class RegionalOperationsApi
{
    private readonly required RegionEndpoints _endpoints;

    /// <summary>
    /// Rewrites each request this client sends to the region's base address, keeping its path and query. A request a preceding
    /// modifier has already made absolute (an earlier region) has its host swapped, as an absolute URI ignores the base it is
    /// resolved against.
    /// </summary>
    /// <param name="region">The region's name.</param>
    /// <returns>A client that sends to the region.</returns>
    public IRegionalOperationsApi ForRegion(string region)
    {
        var regionalBase = this._endpoints.Regions[region];

        return this.WithRequestModifier(message =>
        {
            var uri = message.RequestUri!;

            message.RequestUri = uri.IsAbsoluteUri
                ? new UriBuilder(uri) { Scheme = regionalBase.Scheme, Host = regionalBase.Host, Port = regionalBase.Port }.Uri
                : new Uri(regionalBase, uri);
        });
    }

    /// <summary>
    /// Records a step on every request, in the order the steps were added.
    /// </summary>
    /// <param name="name">The step's name.</param>
    /// <returns>A client that records the step.</returns>
    public IRegionalOperationsApi WithStep(string name)
    {
        return this.WithRequestModifier(request => request.Headers.Add(RequestSteps.HeaderName, name));
    }

    /// <summary>
    /// Sets the Authorization header of every request.
    /// </summary>
    /// <param name="value">The header's value.</param>
    /// <returns>A client that sets it.</returns>
    public IRegionalOperationsApi WithAuthorizationHeader(string value)
    {
        return this.WithRequestModifier(request => request.Headers.TryAddWithoutValidation("Authorization", value));
    }

    /// <summary>
    /// Passes a modifier straight to <c>WithRequestModifier</c>.
    /// </summary>
    /// <param name="modify">The modifier.</param>
    /// <returns>A client that runs it.</returns>
    public IRegionalOperationsApi WithModifier(Action<HttpRequestMessage> modify)
    {
        return this.WithRequestModifier(modify);
    }

    // Reads the operation, as a gateway's sign-up hook reads the idempotency key it carries, and chains the modifier the
    // caller gave before adding its own header: the call's options are replaced, so that the hook's is the one that runs in
    // the caller's place.
    partial void OnConfigure(SignupTenantCommand operation, ref CallOptions? options)
    {
        var caller = options?.ModifyRequest;
        var key = operation.IdempotencyKey;

        options = new CallOptions
        {
            ModifyRequest = request =>
            {
                caller?.Invoke(request);
                request.Headers.Add(RequestSteps.HeaderName, "hook");
                request.Headers.Add("Idempotency-Key", key);
            },
        };
    }
}
