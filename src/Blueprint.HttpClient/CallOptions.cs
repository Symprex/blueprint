using System.Diagnostics.CodeAnalysis;

namespace Blueprint.HttpClient;

/// <summary>
/// Options that modify an outgoing HTTP request before it is sent by
/// <see cref="BlueprintHttpClient{TApp}"/>. Gateway services in S365 pass
/// <see cref="CallOptions"/> through their generated typed client methods
/// to add authentication headers, idempotency keys, or other per-request customisations.
/// </summary>
[SuppressMessage("Design", "CA1052:Static holder types should be static", Justification = "Intentional mutable options bag.")]
public class CallOptions
{
    /// <summary>
    /// An optional action that will modify the <see cref="HttpRequestMessage"/> before it is sent.
    /// Use this to add custom headers, override the URI, set authentication tokens, or apply
    /// any other per-request customisation.
    /// </summary>
    /// <example>
    /// <code>
    /// var options = new CallOptions
    /// {
    ///     ModifyRequest = message =&gt;
    ///     {
    ///         message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
    ///     },
    /// };
    /// </code>
    /// </example>
    public Action<HttpRequestMessage>? ModifyRequest { get; set; }
}
