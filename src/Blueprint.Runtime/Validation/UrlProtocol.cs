namespace Blueprint.Validation;

/// <summary>
/// Controls how <see cref="UrlAttribute"/> checks the protocol portion of a URL.
/// </summary>
public enum UrlProtocol
{
    /// <summary>
    /// The protocol prefix (e.g. <c>http://</c>, <c>https://</c>) is optional.
    /// A bare domain like <c>example.com</c> is accepted.
    /// </summary>
    NotRequired,

    /// <summary>
    /// The protocol prefix is required. The URL must start with <c>http://</c>,
    /// <c>https://</c>, <c>ftp://</c> or <c>ftps://</c>.
    /// </summary>
    Required,
}
