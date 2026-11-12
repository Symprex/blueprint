namespace Blueprint.Model;

/// <summary>
/// Builds the body an operation is sent to a remote Blueprint API with when its host does not read that body as JSON (#71,
/// D19): the stream a <c>[FromBody]</c> <see cref="Stream"/> property holds, or a <c>multipart/form-data</c> form of an
/// operation with an <c>IFormFile</c> property.
/// </summary>
/// <remarks>
/// <para>
/// The source generator implements it, explicitly, on such an operation's generated partial, and <c>BlueprintHttpClient</c>
/// sends any operation that implements it with the content it builds instead of the operation as JSON. Every way of sending
/// goes through that one check, a typed client, <c>BlueprintHttpClient&lt;TApp&gt;</c> and a gateway's forward alike, so a
/// forward streams an inbound upload upstream: the inbound request body, or each inbound file, is the outbound content,
/// read as it is sent and never buffered.
/// </para>
/// <para>
/// The content takes the operation's streams over: sending the request reads them, and disposing it disposes them.
/// </para>
/// </remarks>
public interface IHttpRequestBodyBuilder
{
    /// <summary>
    /// Builds the request's content from the operation's current property values.
    /// </summary>
    /// <remarks>
    /// An operation derived from one that implements this inherits its implementation, so a derived operation whose body is
    /// JSON again, because it hides the base's stream or file with a property of its own, implements it once more to return
    /// <see langword="null"/>.
    /// </remarks>
    /// <returns>The content, which the request it is sent with owns; or <see langword="null"/> to send the operation as JSON.</returns>
    HttpContent? BuildRequestBody();
}
