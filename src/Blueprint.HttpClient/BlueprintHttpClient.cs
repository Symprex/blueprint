using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Validation;

namespace Blueprint.HttpClient;

/// <summary>
/// A typed HTTP client that sends Blueprint operations to a remote Blueprint API host and
/// deserializes the response. Uses the operation's <see cref="ApiOperationDescriptor"/> metadata
/// to resolve the URL pattern and HTTP method so callers need only supply a populated operation instance.
/// <para>
/// Register via <see cref="BlueprintHttpClientServiceCollectionExtensions"/>
/// which configures the underlying <see cref="System.Net.Http.HttpClient"/> with a named client factory.
/// </para>
/// </summary>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
public sealed class BlueprintHttpClient<TApp>
    where TApp : BlueprintApp
{
    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly TApp _app;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpClient{TApp}"/> class.
    /// </summary>
    /// <param name="httpClient">The configured <see cref="System.Net.Http.HttpClient"/>.</param>
    /// <param name="app">The Blueprint app model for operation descriptor lookups.</param>
    /// <param name="jsonOptions">JSON serialization options, defaulting to Blueprint's standard camelCase config.</param>
    public BlueprintHttpClient(System.Net.Http.HttpClient httpClient, TApp app, JsonSerializerOptions? jsonOptions = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this._app = app ?? throw new ArgumentNullException(nameof(app));
        this._jsonOptions = jsonOptions ?? BlueprintJsonOptions.CreateDefaultOptions();
    }

    /// <summary>
    /// Sends an operation to the remote API and deserializes the response body as <typeparamref name="TResponse"/>.
    /// The operation type is resolved against the app's descriptors to determine the URL and HTTP method.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response, or <c>default</c> for 204 No Content responses.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity with validation errors.</exception>
    public async Task<TResponse?> SendAsync<TResponse>(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var request = this.CreateRequest(operation);
        using var response = await this._httpClient.SendAsync(request, cancellationToken);

        return await this.HandleResponseAsync<TResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Sends an operation that does not return a typed response body (fire-and-forget or status-only commands).
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request has been sent and the response validated.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    public async Task SendAsync(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var request = this.CreateRequest(operation);
        using var response = await this._httpClient.SendAsync(request, cancellationToken);

        await this.EnsureSuccessOrThrowAsync(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(IOperation operation)
    {
        var operationType = operation.GetType();
        var descriptor = this._app.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is registered in the BlueprintApp '{typeof(TApp).Name}'.");

        var httpMethod = ResolveHttpMethod(descriptor);
        var url = ResolveUrl(descriptor, operation);

        // For GET requests, append non-URL properties as query string parameters
        if (httpMethod == HttpMethod.Get)
        {
            url = AppendQueryString(url, descriptor, operation);
        }

        var request = new HttpRequestMessage(httpMethod, url);

        // For methods that carry a body, serialize the operation as JSON
        if (httpMethod != HttpMethod.Get && httpMethod != HttpMethod.Delete)
        {
            request.Content = JsonContent.Create(operation, operationType, options: this._jsonOptions);
        }

        return request;
    }

    private static string AppendQueryString(string url, ApiOperationDescriptor descriptor, IOperation operation)
    {
        // Collect URL placeholder property names to exclude from query string
        var urlPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var urlDef in descriptor.Urls)
        {
            ExtractPlaceholderNames(urlDef.UrlFormat, urlPlaceholders);
        }

        foreach (var link in descriptor.Links)
        {
            ExtractPlaceholderNames(link.UrlFormat, urlPlaceholders);
        }

        var queryParts = new List<string>();
        var operationType = operation.GetType();

        foreach (var property in operationType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (urlPlaceholders.Contains(property.Name))
            {
                continue;
            }

            var value = property.GetValue(operation);
            if (value is null)
            {
                continue;
            }

            var stringValue = value switch
            {
                bool b => b ? "true" : "false",
                _ => value.ToString(),
            };

            if (string.IsNullOrEmpty(stringValue))
            {
                continue;
            }

            queryParts.Add($"{Uri.EscapeDataString(property.Name)}={Uri.EscapeDataString(stringValue)}");
        }

        if (queryParts.Count == 0)
        {
            return url;
        }

        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + string.Join("&", queryParts);
    }

    private static void ExtractPlaceholderNames(string urlFormat, HashSet<string> names)
    {
        var startIndex = 0;
        while (true)
        {
            var open = urlFormat.IndexOf('{', startIndex);
            if (open < 0)
            {
                break;
            }

            var close = urlFormat.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }

            var placeholder = urlFormat.Substring(open + 1, close - open - 1).TrimStart('*');
            var colon = placeholder.IndexOf(':');
            if (colon >= 0)
            {
                placeholder = placeholder[..colon];
            }

            names.Add(placeholder);
            startIndex = close + 1;
        }
    }

    private static HttpMethod ResolveHttpMethod(ApiOperationDescriptor descriptor)
    {
        if (descriptor.TryGetFeatureData<OperationHttpMethodMetadataFeatureData>(out var httpMethodData)
            && httpMethodData.HttpMethods.Length > 0)
        {
            return httpMethodData.HttpMethods[0] switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                var method => new HttpMethod(method),
            };
        }

        // Fallback: commands are POST, queries are GET
        var operationType = descriptor.OperationType;
        if (operationType.GetInterfaces().Any(static i =>
                i.IsGenericType && i.GetGenericTypeDefinition().Name.StartsWith("IQuery", StringComparison.Ordinal)))
        {
            return HttpMethod.Get;
        }

        return HttpMethod.Post;
    }

    private static string ResolveUrl(ApiOperationDescriptor descriptor, IOperation operation)
    {
        // Try URL templates first — the client passes an operation instance, and
        // self-link CompiledUrlBuilders are typed for the resource, not the operation.
        foreach (var urlDef in descriptor.Urls)
        {
            var url = SubstitutePlaceholders(urlDef.UrlFormat, operation);
            if (url is not null)
            {
                return url.StartsWith('/') ? url : "/" + url;
            }
        }

        // Fallback: try links (may work when the link is an operation-typed link)
        foreach (var link in descriptor.Links)
        {
            try
            {
                var url = link.CreateRelativeUrl(operation);
                if (url is not null)
                {
                    return url.StartsWith('/') ? url : "/" + url;
                }
            }
            catch (InvalidCastException)
            {
                // CompiledUrlBuilder is typed for the resource, not the operation — skip
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve a URL for operation '{descriptor.OperationType.FullName}'. " +
            $"The operation has no registered URLs or links.");
    }

    private static string? SubstitutePlaceholders(string urlFormat, object values)
    {
        if (!urlFormat.Contains('{'))
        {
            return urlFormat;
        }

        var result = urlFormat;
        var valueType = values.GetType();
        var startIndex = 0;

        while (true)
        {
            var openBrace = result.IndexOf('{', startIndex);
            if (openBrace < 0)
            {
                break;
            }

            var closeBrace = result.IndexOf('}', openBrace);
            if (closeBrace < 0)
            {
                break;
            }

            var placeholder = result.Substring(openBrace + 1, closeBrace - openBrace - 1);

            // Strip catch-all prefix and format specifier
            var propertyName = placeholder.TrimStart('*');
            var colonIndex = propertyName.IndexOf(':');
            if (colonIndex >= 0)
            {
                propertyName = propertyName[..colonIndex];
            }

            var property = valueType.GetProperty(
                propertyName,
                System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            if (property is null)
            {
                return null;
            }

            var value = property.GetValue(values);
            if (value is null)
            {
                return null;
            }

            var rendered = Uri.EscapeDataString(value.ToString()!);
            result = string.Concat(result.AsSpan(0, openBrace), rendered, result.AsSpan(closeBrace + 1));
            startIndex = openBrace + rendered.Length;
        }

        return result;
    }

    private async Task<TResponse?> HandleResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await this.EnsureSuccessOrThrowAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        // Plain text responses: if TResponse is string, return the body as-is
        if (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
            && typeof(TResponse) == typeof(string))
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (TResponse)(object)text;
        }

        // JSON deserialization
        return await response.Content.ReadFromJsonAsync<TResponse>(this._jsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // 422 Unprocessable Entity → ValidationException with field errors
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            throw this.ParseValidationException(body);
        }

        throw new BlueprintApiException(
            response.StatusCode,
            $"The remote API returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            body);
    }

    private ValidationException ParseValidationException(string body)
    {
        try
        {
            var problemDetails = JsonSerializer.Deserialize<ValidationProblemDetailsDto>(body, this._jsonOptions);

            if (problemDetails?.Errors is { Count: > 0 })
            {
                return new ValidationException(problemDetails.Errors);
            }

            return new ValidationException(
                new Dictionary<string, string[]>
                {
                    [string.Empty] = [problemDetails?.Detail ?? "Validation failed."],
                });
        }
        catch (JsonException)
        {
            return new ValidationException(
                new Dictionary<string, string[]>
                {
                    [string.Empty] = [body],
                });
        }
    }

    /// <summary>
    /// Internal DTO for deserializing RFC 9457 / RFC 7807 validation problem details responses.
    /// </summary>
    private sealed class ValidationProblemDetailsDto
    {
        public string? Type { get; set; }

        public string? Title { get; set; }

        public string? Detail { get; set; }

        public int? Status { get; set; }

        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
