using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Blueprint.Model;
using Blueprint.Runtime.Utilities;

namespace Blueprint.Http;

/// <summary>
/// Default implementation of <see cref="IApiLinkGenerator"/> that resolves links from
/// <see cref="BlueprintApp"/> and constructs fully-qualified URLs using a configured base URI.
/// </summary>
public sealed class ApiLinkGenerator : IApiLinkGenerator
{
    /// <summary>
    /// Cached property accessors per query-string type, avoiding repeated reflection on hot paths.
    /// Each entry is an array of (escapedName, getter) tuples. The escaped name is pre-computed
    /// once via <see cref="Uri.EscapeDataString(string)"/> so the per-request query-string builder never
    /// re-escapes the same property name.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, (string EscapedName, Func<object, object?> Getter)[]> _queryStringAccessors = new();

    private readonly BlueprintApp _app;
    private readonly string _baseUri;
    private readonly string _originalBaseUri;

    /// <summary>
    /// Gets the original (pre-normalization) base URI passed to the constructor, enabling
    /// callers to check reference equality for caching (the router reuses the same string instance).
    /// </summary>
    internal string OriginalBaseUri => this._originalBaseUri;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiLinkGenerator"/> class.
    /// </summary>
    /// <param name="app">The registered Blueprint app model.</param>
    /// <param name="baseUri">
    /// The base URI for all generated links (e.g. <c>https://api.example.com/</c>).
    /// Must end with a trailing slash.
    /// </param>
    public ApiLinkGenerator(BlueprintApp app, string baseUri)
    {
        this._app = app;
        this._originalBaseUri = baseUri;
        this._baseUri = baseUri.EndsWith('/') ? baseUri : baseUri + "/";
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(object idDefinition, object? queryString = null)
        where T : ApiResource
    {
        var selfLink = this._app.GetLinkFor(typeof(T), "self");

        if (selfLink is null)
        {
            throw new InvalidOperationException(
                $"Cannot generate a self link for resource type '{typeof(T).Name}' because no 'self' link " +
                "has been registered. Ensure an operation has a [SelfLink] attribute targeting this resource type.");
        }

        var relativeUrl = selfLink.CreateRelativeUrl(idDefinition);

        if (relativeUrl is null)
        {
            throw new InvalidOperationException(
                $"Cannot fill all placeholders for the self link of '{typeof(T).Name}' from the provided id definition.");
        }

        var fullUri = this.BuildUrl(relativeUrl, queryString);

        return new Link
        {
            Href = fullUri,
            Type = typeof(T).Name,
        };
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(string id, object? queryString = null)
        where T : ApiResource
    {
        var selfLink = this._app.GetLinkFor(typeof(T), "self");

        if (selfLink is null)
        {
            throw new InvalidOperationException(
                $"Cannot generate a self link for resource type '{typeof(T).Name}' because no 'self' link " +
                "has been registered. Ensure an operation has a [SelfLink] attribute targeting this resource type.");
        }

        // Fast path: try passing the raw string to the compiled builder — single-placeholder
        // builders generate a string case that avoids anonymous-object allocation and reflection.
        var relativeUrl = selfLink.CompiledUrlBuilder?.Invoke(id);

        // Slow path: wrap in anonymous object for reflection-based placeholder resolution
        relativeUrl ??= selfLink.CreateRelativeUrl(new { id });

        if (relativeUrl is null)
        {
            throw new InvalidOperationException(
                $"Cannot fill all placeholders for the self link of '{typeof(T).Name}' from the provided id definition.");
        }

        var fullUri = this.BuildUrl(relativeUrl, queryString);

        return new Link
        {
            Href = fullUri,
            Type = typeof(T).Name,
        };
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(int id, object? queryString = null)
        where T : ApiResource
    {
        return this.CreateSelfLink<T>(id.ToString(), queryString);
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(Guid id, object? queryString = null)
        where T : ApiResource
    {
        var selfLink = this._app.GetLinkFor(typeof(T), "self");

        if (selfLink is null)
        {
            throw new InvalidOperationException(
                $"Cannot generate a self link for resource type '{typeof(T).Name}' because no 'self' link " +
                "has been registered. Ensure an operation has a [SelfLink] attribute targeting this resource type.");
        }

        // Fast path: pass the Guid directly to the compiled builder — the generated switch
        // expression includes a System.Guid case that formats via ISpanFormattable.TryFormat
        // directly into the interpolated string, avoiding Guid.ToString() heap allocation.
        var relativeUrl = selfLink.CompiledUrlBuilder?.Invoke(id);

        // Slow path: wrap in anonymous object for reflection-based placeholder resolution.
        relativeUrl ??= selfLink.CreateRelativeUrl(new { id = id.ToString() });

        if (relativeUrl is null)
        {
            throw new InvalidOperationException(
                $"Cannot fill all placeholders for the self link of '{typeof(T).Name}' from the provided id definition.");
        }

        var fullUri = this.BuildUrl(relativeUrl, queryString);

        return new Link
        {
            Href = fullUri,
            Type = typeof(T).Name,
        };
    }

    /// <inheritdoc />
    public string? CreateUrl(ApiOperationLink link, object? result = null)
    {
        if (!link.HasPlaceholders())
        {
            return this._baseUri + link.UrlFormat.TrimStart('/');
        }

        var relativeUrl = link.CreateRelativeUrl(result);

        if (relativeUrl is null)
        {
            return null;
        }

        return this._baseUri + relativeUrl.TrimStart('/');
    }

    /// <inheritdoc />
    public string CreateUrl(object operation)
    {
        var operationType = operation.GetType();
        var links = this._app.GetLinksForOperation(operationType);

        // Use pattern match to avoid IEnumerator allocation from FirstOrDefault()
        var link = links is IReadOnlyList<ApiOperationLink> { Count: > 0 } list
            ? list[0]
            : links.FirstOrDefault();

        if (link is null)
        {
            throw new InvalidOperationException(
                $"No links have been registered for operation type '{operationType.FullName}'.");
        }

        var relativeUrl = link.CreateRelativeUrl(operation);

        if (relativeUrl is null)
        {
            throw new InvalidOperationException(
                $"Cannot construct URL for operation '{operationType.Name}' — required placeholder values are missing.");
        }

        return this._baseUri + relativeUrl.TrimStart('/');
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Query-string objects are reflected over their public properties to serialize caller-supplied link parameters into the generated URL.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "CompiledPropertyAccessor uses Expression.Compile() but this fallback path is only used for non-source-generated link construction. Source-generated CompiledUrlBuilder bypasses this for all known operation types.")]
    private string BuildUrl(string relativeUrl, object? queryString)
    {
        var trimmedUrl = relativeUrl.TrimStart('/');

        if (queryString is null)
        {
            return this._baseUri + trimmedUrl;
        }

        var queryStringType = queryString.GetType();
        if (!_queryStringAccessors.TryGetValue(queryStringType, out var accessors))
        {
            var props = queryStringType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var computedAccessors = new (string EscapedName, Func<object, object?> Getter)[props.Length];

            for (var i = 0; i < props.Length; i++)
            {
                computedAccessors[i] = (Uri.EscapeDataString(ToCamelCase(props[i].Name)), CompiledPropertyAccessor.GetGetter(props[i]));
            }

            accessors = _queryStringAccessors.GetOrAdd(queryStringType, computedAccessors);
        }

        var sb = new StringBuilder(this._baseUri.Length + trimmedUrl.Length + 64);
        sb.Append(this._baseUri);
        sb.Append(trimmedUrl);

        var first = true;

        foreach (var (escapedName, getter) in accessors)
        {
            var value = getter(queryString);

            if (value is null)
            {
                continue;
            }

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(escapedName);
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value is string s ? s : value.ToString()!));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a PascalCase property name to camelCase to match the JSON/query-string
    /// naming convention used by the generated pipeline bindings.
    /// </summary>
    private static string ToCamelCase(string name)
    {
        if (name.Length == 0 || char.IsLower(name[0]))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
    }
}
