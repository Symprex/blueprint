using System.Net.Http.Headers;
using Blueprint.Model;

namespace Blueprint.HttpClient;

/// <summary>
/// The <see cref="HttpRequestMessage.Options"/> <see cref="BlueprintHttpClient"/> sets on each request it sends, so that a
/// handler of the named client, such as a test double of the upstream, can tell which operation a request carries without
/// parsing its route back.
/// </summary>
public static class BlueprintRequestOptions
{
    /// <summary>
    /// The operation the request was built from: a typed client's operation, or a forward's inner operation. It is the
    /// instance the caller passed, so a handler must not change it.
    /// </summary>
    public static readonly HttpRequestOptionsKey<IOperation> Operation = new("Blueprint.Operation");

    /// <summary>
    /// The <c>Authorization</c> header the request itself carried when it was handed to the
    /// <see cref="System.Net.Http.HttpClient"/>: the one a forward's credential or <see cref="CallOptions.ModifyRequest"/>
    /// set. It is absent when the request carried none. <see cref="System.Net.Http.HttpClient"/> merges its
    /// <see cref="System.Net.Http.HttpClient.DefaultRequestHeaders"/> into the request before any handler sees it, so the
    /// header a handler reads cannot say which of the two set it; this can.
    /// </summary>
    public static readonly HttpRequestOptionsKey<AuthenticationHeaderValue> RequestAuthorization = new("Blueprint.RequestAuthorization");
}
