namespace Blueprint.Model.Attributes;

/// <summary>
/// Declares the media types an operation's streamed body is sent as, for its OpenAPI document: the content of the success
/// response of an operation returning a <c>FileStreamResult</c> or a <c>ProxyResult</c>.
/// </summary>
/// <remarks>
/// <para>
/// A streamed result's media type is chosen when it is made (a <c>FileStreamResult</c>'s content type, an upstream response's
/// <c>Content-Type</c>), so it cannot be read from the declared result type; this attribute states it. Each media type is
/// documented as a binary string.
/// </para>
/// <para>
/// Without it, a <c>FileStreamResult</c> is documented as <c>application/octet-stream</c> and a <c>ProxyResult</c> with no
/// content. It documents only; the response is still sent with whatever media type the result carries.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ProducesContentType("text/vcard")]
/// public partial class GenerateVCardQuery : IQuery&lt;FileStreamResult&gt;;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProducesContentTypeAttribute : Attribute
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ProducesContentTypeAttribute"/> class.
    /// </summary>
    /// <param name="contentType">The media type the body is sent as, such as <c>text/vcard</c>.</param>
    /// <param name="additionalContentTypes">Any further media types the body may be sent as.</param>
    public ProducesContentTypeAttribute(string contentType, params string[] additionalContentTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(additionalContentTypes);

        this.ContentTypes = [contentType, .. additionalContentTypes];
    }

    /// <summary>
    /// Gets the media types the body may be sent as, in the order declared.
    /// </summary>
    public IReadOnlyList<string> ContentTypes { get; }
}
