using System.Reflection;
using System.Text;
using Blueprint.Model;

namespace Blueprint.Http;

/// <summary>
/// Default implementation of <see cref="IApiLinkGenerator"/> that resolves links from
/// <see cref="BlueprintApp"/> and constructs fully-qualified URLs using a configured base URI.
/// </summary>
public class ApiLinkGenerator : IApiLinkGenerator
{
    private readonly BlueprintApp _app;
    private readonly string _baseUri;

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
        return this.CreateSelfLink<T>(new { id }, queryString);
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(int id, object? queryString = null)
        where T : ApiResource
    {
        return this.CreateSelfLink<T>(new { id }, queryString);
    }

    /// <inheritdoc />
    public Link CreateSelfLink<T>(Guid id, object? queryString = null)
        where T : ApiResource
    {
        return this.CreateSelfLink<T>(new { id }, queryString);
    }

    /// <inheritdoc />
    public string? CreateUrl(ApiOperationLink link, object? result = null)
    {
        if (!link.HasPlaceholders())
        {
            return this._baseUri + link.UrlFormat;
        }

        var relativeUrl = link.CreateRelativeUrl(result);

        if (relativeUrl is null)
        {
            return null;
        }

        return this._baseUri + relativeUrl;
    }

    /// <inheritdoc />
    public string CreateUrl(object operation)
    {
        var operationType = operation.GetType();
        var links = this._app.GetLinksForOperation(operationType);
        var link = links.FirstOrDefault();

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

        return this._baseUri + relativeUrl;
    }

    private string BuildUrl(string relativeUrl, object? queryString)
    {
        if (queryString is null)
        {
            return this._baseUri + relativeUrl;
        }

        var sb = new StringBuilder(this._baseUri.Length + relativeUrl.Length + 64);
        sb.Append(this._baseUri);
        sb.Append(relativeUrl);

        var first = true;

        foreach (var property in queryString.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(queryString);

            if (value is null)
            {
                continue;
            }

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(property.Name));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value.ToString()!));
        }

        return sb.ToString();
    }
}
