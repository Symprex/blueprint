using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.HttpClient.Readers;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
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
/// <remarks>
/// Each call resolves the operation's HTTP method and URL from its descriptor and the reader of the response type the
/// caller asked for (made once per type), then sends through <see cref="BlueprintHttpClient.SendAsync{TOperation, TResult}"/>,
/// the one send primitive a generated typed client uses too.
/// </remarks>
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
public sealed class BlueprintHttpClient<TApp> : BlueprintHttpClient
    where TApp : BlueprintApp
{
    /// <summary>
    /// Caches <see cref="PropertyInfo"/> arrays per operation type to avoid repeated
    /// <see cref="Type.GetProperties(BindingFlags)"/> allocations on each request.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

    private readonly TApp _app;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpClient{TApp}"/> class.
    /// </summary>
    /// <param name="httpClient">The configured <see cref="System.Net.Http.HttpClient"/>.</param>
    /// <param name="app">The Blueprint app model for operation descriptor lookups.</param>
    /// <param name="jsonOptions">JSON serialization options, defaulting to Blueprint's standard camelCase config.</param>
    public BlueprintHttpClient(System.Net.Http.HttpClient httpClient, TApp app, JsonSerializerOptions? jsonOptions = null)
        : base(httpClient, jsonOptions)
    {
        this._app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <summary>
    /// Sends an operation to the remote API and deserializes the response body as <typeparamref name="TResponse"/>.
    /// The operation type is resolved against the app's descriptors to determine the URL and HTTP method.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The deserialized response. A bodiless response with no media type — a 204 No Content, or any other status with
    /// nothing to read — is a <see cref="StatusCodeResult"/> singleton of the response's status when
    /// <typeparamref name="TResponse"/> can hold a <see cref="StatusCodeResult"/>, <see cref="HttpResult"/>,
    /// <see cref="Blueprint.OperationResult"/> or <see cref="object"/> (a status-only <see cref="HttpResult"/>
    /// when only that base is asked for), as legacy's client made one.
    /// </returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="InvalidOperationException">A bodiless, media-type-less response for a <typeparamref name="TResponse"/> that cannot hold one of those shapes, as legacy's client threw.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity with validation errors.</exception>
    public Task<TResponse?> SendAsync<TResponse>(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync<TResponse>(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Sends an operation to the remote API and deserializes the response body as <typeparamref name="TResponse"/>,
    /// with optional per-request customisation via <see cref="CallOptions"/>.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request (e.g. add headers).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The deserialized response. A bodiless response with no media type — a 204 No Content, or any other status with
    /// nothing to read — is a <see cref="StatusCodeResult"/> singleton of the response's status when
    /// <typeparamref name="TResponse"/> can hold a <see cref="StatusCodeResult"/>, <see cref="HttpResult"/>,
    /// <see cref="Blueprint.OperationResult"/> or <see cref="object"/> (a status-only <see cref="HttpResult"/>
    /// when only that base is asked for), as legacy's client made one.
    /// </returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="InvalidOperationException">A bodiless, media-type-less response for a <typeparamref name="TResponse"/> that cannot hold one of those shapes, as legacy's client threw.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity with validation errors.</exception>
    public async Task<TResponse?> SendAsync<TResponse>(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var (method, url) = this.ResolveRequest(operation);

        return await this.SendAsync(method, url, operation, operationTypeInfo: null, AppResponseReaders.For<TResponse>(), operation.GetType().Name, options, requestModifier: null, credential: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an operation that does not return a typed response body (fire-and-forget or status-only commands).
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request has been sent and the response validated.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    public Task SendAsync(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Sends an operation that does not return a typed response body, with optional per-request customisation.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request has been sent and the response validated.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    public async Task SendAsync(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var (method, url) = this.ResolveRequest(operation);

        await this.SendAsync(method, url, operation, operationTypeInfo: null, NoResponseBodyReader.Instance, operation.GetType().Name, options, requestModifier: null, credential: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an operation whose remote handler returns a <see cref="Blueprint.Runtime.Exec.ResourceEventResult{T}"/>.
    /// The HTTP host serialises that result inside an envelope (<c>{"eventId":"...","resourceId":"...","content":{...}}</c>);
    /// this method unwraps the <c>content</c> property and deserialises it as <typeparamref name="TResource"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource type carried inside the event envelope's <c>content</c> property.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized resource from the event envelope.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response does not contain a <c>content</c> property.</exception>
    public Task<TResource?> SendResourceEventAsync<TResource>(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendResourceEventAsync<TResource>(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Sends an operation whose remote handler returns a resource event envelope,
    /// with optional per-request customisation via <see cref="CallOptions"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource type carried inside the event envelope's <c>content</c> property.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized resource from the event envelope.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the response does not contain a <c>content</c> property.</exception>
    public async Task<TResource?> SendResourceEventAsync<TResource>(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var (method, url) = this.ResolveRequest(operation);

        return await this.SendAsync(method, url, operation, operationTypeInfo: null, ResourceEventDataReader<TResource>.Instance, operation.GetType().Name, options, requestModifier: null, credential: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an operation whose remote handler returns a resource event and deserialises
    /// the full envelope as a <see cref="ResourceEvent{TResource}"/>. Unlike
    /// <see cref="SendResourceEventAsync{TResource}(IOperation, CancellationToken)"/> (which returns only the inner resource),
    /// this method returns the complete event envelope including <c>ChangeType</c>, <c>EventId</c>,
    /// <c>Href</c>, <c>Metadata</c> etc.
    /// <para>
    /// Gateway services in S365 call upstream commands that return <c>ResourceEvent&lt;T&gt;</c> and need
    /// access to the envelope metadata to construct their own response. This method provides that.
    /// </para>
    /// </summary>
    /// <typeparam name="TResource">The resource type carried inside the envelope's <c>data</c> property.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full <see cref="ResourceEvent{TResource}"/> envelope with properly typed <c>Data</c>.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    public Task<ResourceEvent<TResource>?> SendResourceEventEnvelopeAsync<TResource>(
        IOperation operation,
        CancellationToken cancellationToken = default)
        where TResource : ApiResource
    {
        return this.SendResourceEventEnvelopeAsync<TResource>(operation, options: null, cancellationToken);
    }

    /// <summary>
    /// Sends an operation whose remote handler returns a resource event envelope,
    /// with optional per-request customisation via <see cref="CallOptions"/>.
    /// Returns the full <see cref="ResourceEvent{TResource}"/> including envelope metadata.
    /// </summary>
    /// <typeparam name="TResource">The resource type carried inside the envelope's <c>data</c> property.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full <see cref="ResourceEvent{TResource}"/> envelope with properly typed <c>Data</c>.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes.</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity.</exception>
    public async Task<ResourceEvent<TResource>?> SendResourceEventEnvelopeAsync<TResource>(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
        where TResource : ApiResource
    {
        ArgumentNullException.ThrowIfNull(operation);

        var (method, url) = this.ResolveRequest(operation);

        return await this.SendAsync(method, url, operation, operationTypeInfo: null, ResourceEventEnvelopeReader<TResource>.Instance, operation.GetType().Name, options, requestModifier: null, credential: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string AppendQueryString(string url, ApiOperationDescriptor descriptor, IOperation operation)
    {
        // When property ownership metadata is available, use it as the source of truth
        // for binding names (camelCase, [FromQuery(Name=...)] overrides) and which properties
        // are query-bound (excluding header/cookie/auth/system properties).
        if (descriptor.TryGetFeatureData<OperationPropertyOwnershipFeatureData>(out var ownershipData))
        {
            return AppendQueryStringFromOwnership(url, ownershipData, operation);
        }

        // Fallback: reflect over all public properties excluding URL placeholders (pre-existing behavior)
        return AppendQueryStringFallback(url, descriptor, operation);
    }

    private static string AppendQueryStringFromOwnership(
        string url,
        OperationPropertyOwnershipFeatureData ownershipData,
        IOperation operation)
    {
        var operationType = operation.GetType();
        System.Text.StringBuilder? queryBuilder = null;

        foreach (var entry in ownershipData.Entries)
        {
            if (entry.Source != PropertyBindingSource.Query)
            {
                continue;
            }

            var property = operationType.GetProperty(entry.PropertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                continue;
            }

            var value = property.GetValue(operation);
            if (value is null)
            {
                continue;
            }

            var bindingName = entry.BindingName ?? entry.PropertyName;
            var escapedName = Uri.EscapeDataString(bindingName);

            // Handle collection types (arrays, List<T>, IEnumerable<T>) by emitting repeated keys
            if (value is not string && value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    var itemStr = item.ToString();
                    if (!string.IsNullOrEmpty(itemStr))
                    {
                        queryBuilder ??= new System.Text.StringBuilder();
                        queryBuilder.Append(queryBuilder.Length == 0 ? (url.Contains('?') ? '&' : '?') : '&');
                        queryBuilder.Append(escapedName).Append('=').Append(Uri.EscapeDataString(itemStr));
                    }
                }

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

            queryBuilder ??= new System.Text.StringBuilder();
            queryBuilder.Append(queryBuilder.Length == 0 ? (url.Contains('?') ? '&' : '?') : '&');
            queryBuilder.Append(escapedName).Append('=').Append(Uri.EscapeDataString(stringValue));
        }

        if (queryBuilder is null)
        {
            return url;
        }

        return string.Concat(url, queryBuilder.ToString());
    }

    private static string AppendQueryStringFallback(string url, ApiOperationDescriptor descriptor, IOperation operation)
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

        System.Text.StringBuilder? queryBuilder = null;
        var operationType = operation.GetType();
        var properties = _propertyCache.GetOrAdd(
            operationType,
            static t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public));

        foreach (var property in properties)
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

            var escapedName = Uri.EscapeDataString(property.Name);

            // Handle collection types (arrays, List<T>, IEnumerable<T>) by emitting repeated keys
            if (value is not string && value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is null)
                    {
                        continue;
                    }

                    var itemStr = item.ToString();
                    if (!string.IsNullOrEmpty(itemStr))
                    {
                        queryBuilder ??= new System.Text.StringBuilder();
                        queryBuilder.Append(queryBuilder.Length == 0 ? (url.Contains('?') ? '&' : '?') : '&');
                        queryBuilder.Append(escapedName).Append('=').Append(Uri.EscapeDataString(itemStr));
                    }
                }

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

            queryBuilder ??= new System.Text.StringBuilder();
            queryBuilder.Append(queryBuilder.Length == 0 ? (url.Contains('?') ? '&' : '?') : '&');
            queryBuilder.Append(escapedName).Append('=').Append(Uri.EscapeDataString(stringValue));
        }

        if (queryBuilder is null)
        {
            return url;
        }

        return string.Concat(url, queryBuilder.ToString());
    }

    private static void ExtractPlaceholderNames(string urlFormat, HashSet<string> names)
    {
        var span = urlFormat.AsSpan();
        var startIndex = 0;
        while (true)
        {
            var open = span[startIndex..].IndexOf('{');
            if (open < 0)
            {
                break;
            }

            open += startIndex;
            var close = span[(open + 1)..].IndexOf('}');
            if (close < 0)
            {
                break;
            }

            close += open + 1;

            var placeholder = span[(open + 1)..close];

            // Strip catch-all prefix
            while (placeholder.Length > 0 && placeholder[0] == '*')
            {
                placeholder = placeholder[1..];
            }

            // Strip a resource-property alternate name (":") or a legacy format specifier ("(") — route-owned,
            // RoutePlaceholderSyntax, R97/R111.
            var separator = placeholder.IndexOfAny(':', '(');
            if (separator >= 0)
            {
                placeholder = placeholder[..separator];
            }

            names.Add(placeholder.ToString());
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

            // Strip catch-all prefix and a resource-property alternate name (":") or legacy format specifier ("(") —
            // route-owned, RoutePlaceholderSyntax, R97/R111.
            var isCatchAll = placeholder.StartsWith('*');
            var propertyName = placeholder.TrimStart('*');
            var separatorIndex = propertyName.IndexOfAny(':', '(');
            if (separatorIndex >= 0)
            {
                propertyName = propertyName[..separatorIndex];
            }

            var properties = _propertyCache.GetOrAdd(
                valueType,
                static t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public));

            PropertyInfo? property = null;
            foreach (var p in properties)
            {
                if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = p;
                    break;
                }
            }

            if (property is null)
            {
                return null;
            }

            var value = property.GetValue(values);
            if (value is null)
            {
                return null;
            }

            // Catch-all parameters (e.g. {**FolderPath}) preserve slashes and must not be encoded
            var rendered = isCatchAll ? value.ToString()! : Uri.EscapeDataString(value.ToString()!);
            result = string.Concat(result.AsSpan(0, openBrace), rendered, result.AsSpan(closeBrace + 1));
            startIndex = openBrace + rendered.Length;
        }

        return result;
    }

    /// <summary>
    /// Resolves the HTTP method and URL the operation is sent with from its descriptor: the source-generated URL builder
    /// (zero reflection) when the descriptor has one, else its URL templates and links, with a GET's query string appended.
    /// </summary>
    /// <returns>
    /// The method and URL; the URL is <see langword="null"/> when the generated builder found a route placeholder with no
    /// value, which the transport refuses before anything is sent.
    /// </returns>
    /// <exception cref="InvalidOperationException">The app has no descriptor of the operation, or the descriptor no URL.</exception>
    private (HttpMethod Method, string? Url) ResolveRequest(IOperation operation)
    {
        var operationType = operation.GetType();
        var descriptor = this._app.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is registered in the BlueprintApp '{typeof(TApp).Name}'.");

        var httpMethod = ResolveHttpMethod(descriptor);

        // Only a GET carries the operation's other properties as a query string; the transport sends every other method's
        // operation as its JSON body.
        var isGet = httpMethod == HttpMethod.Get;

        if (descriptor.TryGetFeatureData<CompiledHttpRequestFeatureData>(out var compiled))
        {
            var compiledUrl = compiled.BuildUrl(operation);

            return (httpMethod, compiledUrl is not null && isGet && compiled.AppendQueryString is not null
                ? compiled.AppendQueryString(operation, compiledUrl)
                : compiledUrl);
        }

        var url = ResolveUrl(descriptor, operation);

        return (httpMethod, isGet ? AppendQueryString(url, descriptor, operation) : url);
    }
}
