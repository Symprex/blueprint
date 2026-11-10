namespace Blueprint.Model;

/// <summary>
/// The HTTP method and URL an operation is sent to a remote Blueprint API with, reachable from a generic type argument so
/// that a caller holding only the operation's type, such as <c>BlueprintHttpClient.ForwardAsync</c>, needs no app, descriptor
/// or lookup to send it.
/// </summary>
/// <remarks>
/// <para>
/// The source generator implements it, explicitly, on every operation it gives a nested <c>Http</c> builder class, by
/// calling those builders, so it sends exactly what a generated typed client of the operation sends: the method the
/// operation's descriptor publishes first, and its first URL, with a GET's query string appended. An operation that cannot
/// take the <c>Http</c> class (one that declares or inherits a member named <c>Http</c> of its own) does not implement it.
/// </para>
/// <para>
/// An operation derived from another implements it for itself as well as inheriting its base's, so the constraint picks the
/// operation's own route when it is the type argument.
/// </para>
/// </remarks>
/// <typeparam name="TSelf">The operation type.</typeparam>
public interface IHttpRequestBuilder<TSelf>
    where TSelf : IHttpRequestBuilder<TSelf>
{
    /// <summary>
    /// Gets the HTTP method the operation is sent with.
    /// </summary>
    /// <returns>The method.</returns>
    static abstract HttpMethod GetHttpMethod();

    /// <summary>
    /// Builds the URL the operation is sent to, relative to the remote API's base address, with a GET's query string appended.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <returns>The URL, or <see langword="null"/> when a route placeholder has no value.</returns>
    static abstract string? BuildRequestUrl(TSelf operation);
}
