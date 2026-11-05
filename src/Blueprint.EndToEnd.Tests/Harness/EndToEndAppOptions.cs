namespace Blueprint.EndToEnd.Tests.Harness;

/// <summary>
/// How one app is started by <see cref="MultiAppHost.StartAsync{TStartup}"/>. The defaults start the app as
/// its own <c>Program</c> would in Development, with nothing changed but the port and the JWT issuer.
/// </summary>
public sealed class EndToEndAppOptions
{
    /// <summary>The hosting environment. Defaults to <c>Development</c>, as a developer would run the sample.</summary>
    public string Environment { get; set; } = "Development";

    /// <summary>
    /// Configuration values layered over the app's own, such as the address of an upstream app started
    /// earlier. This is how apps find each other: by configuration, as they would in a deployment.
    /// </summary>
    public IDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <see langword="true"/>, every outbound <see cref="System.Net.Http.HttpClient"/> the app makes
    /// through <see cref="System.Net.Http.IHttpClientFactory"/> carries the inbound caller's
    /// <c>Authorization</c> header, unless the request already sets one.
    /// </summary>
    /// <remarks>
    /// This stands in for the caller-credential pass-through a gateway in Signature365 does by hand today.
    /// The sample gateway does no credential handling of its own yet, and without this the inner app, which
    /// requires a scoped caller, refuses every hop. Leave it off for an app whose own credential strategy is
    /// what a test is proving, or the harness would supply the header the app was meant to.
    /// </remarks>
    public bool ForwardCallerCredential { get; set; }

    /// <summary>
    /// Service registrations applied after the app's own <c>Startup.ConfigureServices</c>, for the rare test
    /// that must replace a dependency. Prefer <see cref="Settings"/>: every override here is a difference
    /// from production.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }
}
