namespace Blueprint.Model;

/// <summary>
/// Feature data attached to an <see cref="ApiOperationDescriptor"/> that provides source-generated,
/// zero-reflection URL building and query string building for <c>BlueprintHttpClient</c>.
/// <para>
/// When this feature data is present the HTTP client uses the compiled delegates directly instead
/// of falling back to property reflection for URL substitution and query string assembly.
/// </para>
/// </summary>
public sealed class CompiledHttpRequestFeatureData
{
    /// <summary>
    /// Initialises a new instance of the <see cref="CompiledHttpRequestFeatureData"/> class.
    /// </summary>
    /// <param name="buildUrl">
    /// A delegate that takes an operation instance (as <see cref="object"/>) and produces the fully-substituted
    /// relative URL (with leading <c>/</c>). Returns <c>null</c> if the operation's URL
    /// placeholders cannot be resolved (e.g. a required property is null).
    /// </param>
    /// <param name="appendQueryString">
    /// A delegate that takes an operation instance (as <see cref="object"/>) and the base URL, then returns
    /// the URL with query string parameters appended. Called only for GET operations.
    /// Pass <c>null</c> if the operation has no query-string-eligible properties.
    /// </param>
    public CompiledHttpRequestFeatureData(
        Func<object, string?> buildUrl,
        Func<object, string, string>? appendQueryString)
    {
        this.BuildUrl = buildUrl ?? throw new ArgumentNullException(nameof(buildUrl));
        this.AppendQueryString = appendQueryString;
    }

    /// <summary>
    /// Builds a fully-substituted relative URL from an operation instance, with all
    /// route placeholders replaced by direct property access (zero reflection).
    /// </summary>
    public Func<object, string?> BuildUrl { get; }

    /// <summary>
    /// Appends query string parameters to a URL from operation properties that are not
    /// part of the URL template. Returns the URL with <c>?key=value&amp;...</c> appended.
    /// <c>null</c> when the operation has no query-string-eligible properties.
    /// </summary>
    public Func<object, string, string>? AppendQueryString { get; }
}
