using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Blueprint.Http;
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
/// <typeparam name="TApp">The <see cref="BlueprintApp"/> that owns the operation descriptors.</typeparam>
public sealed class BlueprintHttpClient<TApp>
    where TApp : BlueprintApp
{
    /// <summary>
    /// Caches <see cref="PropertyInfo"/> arrays per operation type to avoid repeated
    /// <see cref="Type.GetProperties(BindingFlags)"/> allocations on each request.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertyCache = new();

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
    /// <returns>The deserialized response, or <c>default</c> for 204 No Content responses.</returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="ValidationException">Thrown for 422 Unprocessable Entity with validation errors.</exception>
    public async Task<TResponse?> SendAsync<TResponse>(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var request = this.CreateRequest(operation);
        options?.ModifyRequest?.Invoke(request);

        try
        {
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await this.HandleResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            // Timeout — use RequestTimeout (not GatewayTimeout, which reverse proxies like
            // Cloudflare intercept and replace with their own HTML error page).
            throw new BlueprintApiException(
                HttpStatusCode.RequestTimeout,
                "Timed out waiting for service response.",
                e.Message);
        }
        catch (HttpRequestException e)
        {
            // Connection failure — use InternalServerError (not BadGateway, for the same
            // Cloudflare interception reason).
            throw new BlueprintApiException(
                HttpStatusCode.InternalServerError,
                "The remote server did not respond.",
                e.Message);
        }
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

        using var request = this.CreateRequest(operation);
        options?.ModifyRequest?.Invoke(request);

        try
        {
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await this.EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw new BlueprintApiException(
                HttpStatusCode.RequestTimeout,
                "Timed out waiting for service response.",
                e.Message);
        }
        catch (HttpRequestException e)
        {
            throw new BlueprintApiException(
                HttpStatusCode.InternalServerError,
                "The remote server did not respond.",
                e.Message);
        }
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

        using var request = this.CreateRequest(operation);
        options?.ModifyRequest?.Invoke(request);

        HttpResponseMessage response;

        try
        {
            response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw new BlueprintApiException(
                HttpStatusCode.RequestTimeout,
                "Timed out waiting for service response.",
                e.Message);
        }
        catch (HttpRequestException e)
        {
            throw new BlueprintApiException(
                HttpStatusCode.InternalServerError,
                "The remote server did not respond.",
                e.Message);
        }

        using (response)
        {
            await this.EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            // Parse the event envelope and extract the resource payload.
            // The envelope uses "data" for the content (matching ResourceEventEnvelope).
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                return dataElement.Deserialize<TResource>(this._jsonOptions);
            }

            // Legacy fallback: some event envelopes use "content" instead of "data"
            if (doc.RootElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.Deserialize<TResource>(this._jsonOptions);
            }

            // Fallback: try deserializing the whole body as TResource
            return doc.RootElement.Deserialize<TResource>(this._jsonOptions);
        }
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

        using var request = this.CreateRequest(operation);
        options?.ModifyRequest?.Invoke(request);

        HttpResponseMessage response;

        try
        {
            response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw new BlueprintApiException(
                HttpStatusCode.RequestTimeout,
                "Timed out waiting for service response.",
                e.Message);
        }
        catch (HttpRequestException e)
        {
            throw new BlueprintApiException(
                HttpStatusCode.InternalServerError,
                "The remote server did not respond.",
                e.Message);
        }

        using (response)
        {
            await this.EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return DeserializeResourceEventEnvelope<TResource>(doc.RootElement, this._jsonOptions);
        }
    }

    /// <summary>
    /// Parses a resource event JSON envelope into a fully-typed <see cref="ResourceEvent{TResource}"/>.
    /// The envelope shape is <c>{"$object":"event","eventId":"...","changeType":"...","data":{...},...}</c>
    /// as produced by <c>HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync</c>.
    /// </summary>
    /// <typeparam name="TResource">The resource type carried inside the envelope's <c>data</c> property.</typeparam>
    /// <param name="root">The root JSON element of the envelope.</param>
    /// <param name="jsonOptions">The JSON serializer options for deserializing nested payloads.</param>
    /// <returns>A fully-typed <see cref="ResourceEvent{TResource}"/>, or <c>null</c> if the data payload is absent.</returns>
    internal static ResourceEvent<TResource>? DeserializeResourceEventEnvelope<TResource>(
        JsonElement root,
        JsonSerializerOptions jsonOptions)
        where TResource : ApiResource
    {
        // Extract the strongly-typed data payload
        TResource? data = default;
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind != JsonValueKind.Null)
        {
            data = dataElement.Deserialize<TResource>(jsonOptions);
        }
        else if (root.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null)
        {
            data = contentElement.Deserialize<TResource>(jsonOptions);
        }

        if (data is null)
        {
            return null;
        }

        // Parse the change type from string → enum
        var changeType = ResourceEventChangeType.Updated;
        if (root.TryGetProperty("changeType", out var changeTypeElement) && changeTypeElement.ValueKind == JsonValueKind.String)
        {
            var changeTypeStr = changeTypeElement.GetString();
            if (changeTypeStr is not null)
            {
                _ = Enum.TryParse(changeTypeStr, ignoreCase: true, out changeType);
            }
        }

        // Extract the event id
        var eventId = string.Empty;
        if (root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.ValueKind == JsonValueKind.String)
        {
            eventId = eventIdElement.GetString() ?? string.Empty;
        }

        var resourceEvent = new ResourceEvent<TResource>(changeType, eventId, data);

        // Populate optional envelope fields
        if (root.TryGetProperty("href", out var hrefElement) && hrefElement.ValueKind == JsonValueKind.String)
        {
            resourceEvent.Href = hrefElement.GetString();
        }

        if (root.TryGetProperty("correlationId", out var corrElement) && corrElement.ValueKind == JsonValueKind.String)
        {
            resourceEvent.CorrelationId = corrElement.GetString();
        }

        if (root.TryGetProperty("changedValues", out var changedElement) && changedElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.ChangedValues = changedElement.Deserialize<Dictionary<string, object>>(jsonOptions);
        }

        // Populate metadata through per-entry WithMetadata to respect internal setter
        if (root.TryGetProperty("metadata", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metaElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => (object?)prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText(),
                };

                if (value is not null)
                {
                    resourceEvent.WithMetadata(prop.Name, value);
                }
            }
        }

        // Populate secureData through per-entry WithSecureData to respect internal setter
        if (root.TryGetProperty("secureData", out var secElement) && secElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in secElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => (object?)prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.GetRawText(),
                };

                if (value is not null)
                {
                    resourceEvent.WithSecureData(prop.Name, value);
                }
            }
        }

        return resourceEvent;
    }

    // ──────────────────────────────────────────────
    //  S365 compatibility: CallAsync aliases
    // ──────────────────────────────────────────────

    /// <summary>
    /// S365 compatibility alias for <see cref="SendAsync{TResponse}(IOperation, CancellationToken)"/>. Gateways in Signature 365
    /// use <c>CallAsync&lt;TResponse&gt;</c>; this method delegates directly so migration requires
    /// no method-name changes in gateway code.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public Task<TResponse?> CallAsync<TResponse>(
        IReturn<TResponse> operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync<TResponse>((IOperation)operation, cancellationToken);
    }

    /// <summary>
    /// S365 compatibility alias for <see cref="SendAsync{TResponse}(IOperation, CallOptions?, CancellationToken)"/>
    /// with <see cref="CallOptions"/> support. This is the exact signature S365 generated typed clients produce.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request (e.g. add headers).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    public Task<TResponse?> CallAsync<TResponse>(
        IReturn<TResponse> operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync<TResponse>((IOperation)operation, options, cancellationToken);
    }

    /// <summary>
    /// S365 compatibility alias for <see cref="SendAsync(IOperation, CancellationToken)"/>.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is processed.</returns>
    public Task CallAsync(
        IOperation operation,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync(operation, cancellationToken);
    }

    /// <summary>
    /// S365 compatibility alias for <see cref="SendAsync(IOperation, CallOptions?, CancellationToken)"/>
    /// with <see cref="CallOptions"/> support.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is processed.</returns>
    public Task CallAsync(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken = default)
    {
        return this.SendAsync(operation, options, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(IOperation operation)
    {
        var operationType = operation.GetType();
        var descriptor = this._app.FindOperation(operationType)
            ?? throw new InvalidOperationException(
                $"No operation descriptor found for type '{operationType.FullName}'. " +
                $"Ensure the operation is registered in the BlueprintApp '{typeof(TApp).Name}'.");

        var httpMethod = ResolveHttpMethod(descriptor);

        // Prefer source-generated URL builder (zero reflection) when available
        string url;
        if (descriptor.TryGetFeatureData<CompiledHttpRequestFeatureData>(out var compiled))
        {
            url = compiled.BuildUrl(operation)
                ?? throw new InvalidOperationException(
                    $"Compiled URL builder returned null for '{operationType.FullName}'. " +
                    $"A required route parameter is likely null.");

            if ((httpMethod == HttpMethod.Get || httpMethod == HttpMethod.Delete) && compiled.AppendQueryString is not null)
            {
                url = compiled.AppendQueryString(operation, url);
            }
        }
        else
        {
            url = ResolveUrl(descriptor, operation);

            if (httpMethod == HttpMethod.Get || httpMethod == HttpMethod.Delete)
            {
                url = AppendQueryString(url, descriptor, operation);
            }
        }

        // Strip the leading '/' so the URL is treated as relative to the HttpClient's BaseAddress.
        // Without this, HttpClient treats "/templates" as an absolute path from the host root,
        // discarding the BaseAddress path component (e.g. "http://host/api/" → "http://host/templates").
        if (url.Length > 0 && url[0] == '/')
        {
            url = url[1..];
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

            // Strip format specifier
            var colon = placeholder.IndexOf(':');
            if (colon >= 0)
            {
                placeholder = placeholder[..colon];
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

            // Strip catch-all prefix and format specifier
            var isCatchAll = placeholder.StartsWith('*');
            var propertyName = placeholder.TrimStart('*');
            var colonIndex = propertyName.IndexOf(':');
            if (colonIndex >= 0)
            {
                propertyName = propertyName[..colonIndex];
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

    private async Task<TResponse?> HandleResponseAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // StatusCodeResult subtypes (e.g. StatusCodeResult.Accepted): the server returns no body,
        // just a status code. Find the singleton Instance field and match by status code. This is
        // the exact pattern S365 gateway code uses via CallAsync<StatusCodeResult.Accepted>(...).
        if (typeof(StatusCodeResult).IsAssignableFrom(typeof(TResponse)) && typeof(StatusCodeResult) != typeof(TResponse))
        {
            // Validate before consuming the response body — error responses should still throw
            await this.EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            var field = typeof(TResponse).GetField(
                nameof(StatusCodeResult.Accepted.Instance),
                BindingFlags.Public | BindingFlags.Static);

            if (field?.GetValue(null) is StatusCodeResult singleton && singleton.StatusCode == (int)response.StatusCode)
            {
                return (TResponse)(object)singleton;
            }

            // Status code didn't match the expected singleton — return default (caller decides if this is an error)
            return default;
        }

        await this.EnsureSuccessOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        // PlainTextResult: when TResponse is PlainTextResult, construct from the response text
        if (typeof(TResponse) == typeof(PlainTextResult) || typeof(PlainTextResult).IsAssignableTo(typeof(TResponse)))
        {
            if (!string.IsNullOrEmpty(contentType))
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return (TResponse)(object)new PlainTextResult(text, response.StatusCode)
                {
                    ContentType = contentType,
                };
            }
        }

        // Plain text responses: if TResponse is string, return the body as-is
        if (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
            && typeof(TResponse) == typeof(string))
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (TResponse)(object)text;
        }

        // JSON deserialization
        return await response.Content.ReadFromJsonAsync<TResponse>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
