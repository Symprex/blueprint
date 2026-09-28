using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;
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
public sealed class BlueprintHttpClient<TApp> : BlueprintHttpClient
    where TApp : BlueprintApp
{
    private const string UpstreamServerErrorDetail = "The remote server returned an internal error response";

    /// <summary>
    /// The problem extension a Blueprint host exposing exception details writes a 5xx's stack trace as.
    /// </summary>
    private const string StackTraceExtensionName = "stackTrace";

    /// <summary>
    /// The problem member naming the occurrence, which can name the remote end's own host.
    /// </summary>
    private const string InstanceMemberName = "instance";

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
    /// <returns>
    /// The deserialized response. A 204 No Content is <see cref="StatusCodeResult.NoContent.Instance"/> when
    /// <typeparamref name="TResponse"/> can hold a <see cref="StatusCodeResult"/>, as legacy's client made one.
    /// </returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="InvalidOperationException">A 204 No Content for a <typeparamref name="TResponse"/> that cannot hold a <see cref="StatusCodeResult"/>, as legacy's client threw.</exception>
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
    /// The deserialized response. A 204 No Content is <see cref="StatusCodeResult.NoContent.Instance"/> when
    /// <typeparamref name="TResponse"/> can hold a <see cref="StatusCodeResult"/>, as legacy's client made one.
    /// </returns>
    /// <exception cref="BlueprintApiException">Thrown for non-success status codes (4xx/5xx).</exception>
    /// <exception cref="InvalidOperationException">A 204 No Content for a <typeparamref name="TResponse"/> that cannot hold a <see cref="StatusCodeResult"/>, as legacy's client threw.</exception>
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
            return await this.HandleResponseAsync<TResponse>(operation.GetType().Name, request, response, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw CreateTimeoutException(e);
        }
        catch (HttpRequestException e)
        {
            throw CreateUnreachableException(e);
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
            await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw CreateTimeoutException(e);
        }
        catch (HttpRequestException e)
        {
            throw CreateUnreachableException(e);
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
            throw CreateTimeoutException(e);
        }
        catch (HttpRequestException e)
        {
            throw CreateUnreachableException(e);
        }

        using (response)
        {
            await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);

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
            throw CreateTimeoutException(e);
        }
        catch (HttpRequestException e)
        {
            throw CreateUnreachableException(e);
        }

        using (response)
        {
            await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);

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

        // Each dictionary is read as SendAsync reads a resource event's (ResourceEventReader.ReadDictionary): a number as an
        // int, or a double when it is no int; a string as a string; a boolean as a bool; an object or array as a JsonElement;
        // a null value as null.
        if (root.TryGetProperty("changedValues", out var changedElement) && changedElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.ChangedValues = ResourceEventReader.ReadDictionary(changedElement, jsonOptions);
        }

        if (root.TryGetProperty("metadata", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.Metadata = ResourceEventReader.ReadDictionary(metaElement, jsonOptions)!;
        }

        if (root.TryGetProperty("secureData", out var secElement) && secElement.ValueKind == JsonValueKind.Object)
        {
            resourceEvent.SecureData = ResourceEventReader.ReadDictionary(secElement, jsonOptions)!;
        }

        return resourceEvent;
    }

    // ──────────────────────────────────────────────
    //  S365 compatibility: CallAsync aliases
    // ──────────────────────────────────────────────

    /// <summary>
    /// Implements the legacy non-generic <c>CallAsync</c> surface exposed by <see cref="BlueprintHttpClient"/>.
    /// The public compatibility overloads live on the base type so legacy callers and the generic client
    /// share the same signatures without duplicating gateway-facing behavior.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    protected override async Task<TResponse> CallAsyncCore<TResponse>(
        IReturn<TResponse> operation,
        CallOptions? options,
        CancellationToken cancellationToken)
    {
        return (await this.SendAsync<TResponse>((IOperation)operation, options, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Implements the legacy non-generic void send surface exposed by <see cref="BlueprintHttpClient"/>.
    /// </summary>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">Optional call options to modify the outgoing request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request is processed.</returns>
    protected override Task SendVoidAsyncCore(
        IOperation operation,
        CallOptions? options,
        CancellationToken cancellationToken)
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

        // A GET carries the operation's other properties as a query string and every other method, a DELETE included, as
        // its JSON body, as legacy's client sent them (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:79,
        // :88-91) and as the host reads them.
        var isGet = httpMethod == HttpMethod.Get;

        // Prefer source-generated URL builder (zero reflection) when available
        string url;
        if (descriptor.TryGetFeatureData<CompiledHttpRequestFeatureData>(out var compiled))
        {
            url = compiled.BuildUrl(operation)
                ?? throw new InvalidOperationException(
                    $"Compiled URL builder returned null for '{operationType.FullName}'. " +
                    $"A required route parameter is likely null.");

            if (isGet && compiled.AppendQueryString is not null)
            {
                url = compiled.AppendQueryString(operation, url);
            }
        }
        else
        {
            url = ResolveUrl(descriptor, operation);

            if (isGet)
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
        if (!isGet)
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

    /// <summary>
    /// The failure of a request that did not answer within the client's timeout.
    /// </summary>
    /// <remarks>
    /// RequestTimeout, not GatewayTimeout: reverse proxies such as Cloudflare replace a 504 with an HTML page of their own.
    /// </remarks>
    /// <param name="exception">The cancellation the timeout raised.</param>
    /// <returns>The exception to throw.</returns>
    private static BlueprintApiException CreateTimeoutException(TaskCanceledException exception)
    {
        return new BlueprintApiException(
            new BlueprintApiException.ApiProblem(
                "Timed out waiting for service response",
                "service_timeout",
                exception.Message,
                (int)HttpStatusCode.RequestTimeout),
            HttpStatusCode.RequestTimeout,
            responseBody: null,
            innerException: null);
    }

    /// <summary>
    /// The failure of a request that reached no server. Its message, which names the host, is kept to the inner exception.
    /// </summary>
    /// <remarks>
    /// InternalServerError, not BadGateway, for the same reverse-proxy reason as <see cref="CreateTimeoutException"/>.
    /// </remarks>
    /// <param name="exception">The failure the handler raised.</param>
    /// <returns>The exception to throw.</returns>
    private static BlueprintApiException CreateUnreachableException(HttpRequestException exception)
    {
        return new BlueprintApiException(
            new BlueprintApiException.ApiProblem(
                "The remote server did not respond",
                "service_unavailable",
                "The remote server did not respond",
                (int)HttpStatusCode.InternalServerError),
            HttpStatusCode.InternalServerError,
            responseBody: null,
            innerException: exception);
    }

    /// <summary>
    /// The failure of a request the remote API answered with an error status other than 422.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problem details are carried through, except that a 5xx response's detail is replaced, and kept to the inner
    /// exception, and the <c>stackTrace</c> extension (in any casing) of a 5xx response, or of a problem whose own status is
    /// 5xx, dropped, because they describe the remote end's
    /// internals; and the <c>instance</c> member is never carried, as legacy's client carried it at no status (written in
    /// another casing it is an extension, as it was to legacy, dropped from a 5xx with the stack trace). A body that is not JSON
    /// maps to <c>not_found</c> for a 404 (naming the path, never the host) and to <c>invalid_json</c> otherwise.
    /// </para>
    /// <para>
    /// A problem without a status has status 0, as legacy's had; the response's own status is still the exception's
    /// <see cref="BlueprintApiException.StatusCode"/>. Extensions are keyed case-insensitively and read as legacy read them.
    /// </para>
    /// </remarks>
    /// <param name="request">The request, whose URI the client has made absolute by now.</param>
    /// <param name="response">The response.</param>
    /// <param name="body">The response body.</param>
    /// <returns>The exception to throw.</returns>
    private BlueprintApiException CreateErrorResponseException(HttpRequestMessage request, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        ProblemDetailsDto? problem;

        try
        {
            problem = this.ReadProblemDetails(body);
        }
        catch (JsonException e)
        {
            var notJson = response.StatusCode == HttpStatusCode.NotFound
                ? new BlueprintApiException.ApiProblem(
                    "The remote server responded Not Found",
                    "not_found",
                    $"Response from {DescribeRequestedResource(request)} was 404",
                    status)
                : new BlueprintApiException.ApiProblem(
                    "The remote server did not produce a valid JSON response",
                    "invalid_json",
                    e.Message,
                    status);

            return new BlueprintApiException(notJson, response.StatusCode, body, innerException: null);
        }

        if (problem is null)
        {
            return new BlueprintApiException(
                new BlueprintApiException.ApiProblem(
                    "Response deserialisation failed",
                    "response_empty",
                    "The error response could not be deserialised",
                    (int)HttpStatusCode.BadGateway),
                response.StatusCode,
                body,
                innerException: null);
        }

        var detail = problem.Detail;
        Exception? innerException = null;

        if (status >= 500)
        {
            detail = UpstreamServerErrorDetail;

            if (!string.IsNullOrWhiteSpace(problem.Detail))
            {
                innerException = new InvalidOperationException(problem.Detail);
            }
        }

        // A host exposing exception details writes a 5xx's stack trace as an extension, which a gateway would write back
        // out with every other one; legacy's host wrote it as the detail, replaced above. It is dropped when either the
        // response's status or the problem's says server error: the exception takes the problem's status, so a 4xx whose
        // problem says 500 is re-emitted by a gateway as a 500 with every extension it carries.
        var isServerError = status >= 500 || problem.Status >= 500;
        Dictionary<string, object>? extensions = null;

        if (problem.Extensions is not null)
        {
            // Keyed case-insensitively, each value as legacy's ProblemDetailsJsonConverter read it (Signature365
            // 8b40ecc961, Blueprint.Http/ProblemDetailsJsonConverter.cs:75-91).
            extensions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in problem.Extensions)
            {
                // "instance" in another casing is an extension, as it was to legacy (ReadProblemDetails), and like the member
                // it can name the remote end's internals.
                if (!isServerError
                    || !(string.Equals(key, StackTraceExtensionName, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(key, InstanceMemberName, StringComparison.OrdinalIgnoreCase)))
                {
                    extensions[key] = ToLegacyExtensionValue(value)!;
                }
            }
        }

        // Legacy's status was an int its converter left at 0 when the problem had none (legacy ProblemDetails.cs:36-37,
        // ProblemDetailsJsonConverter.cs:63-74), and its client threw with it (legacy BlueprintHttpClient.cs:162, :171).
        return new BlueprintApiException(
            new BlueprintApiException.ApiProblem(problem.Title, problem.Type, detail, problem.Status ?? 0, extensions),
            response.StatusCode,
            body,
            innerException);
    }

    /// <summary>
    /// Reads an error body as legacy's <c>ProblemDetailsJsonConverter</c> read one (Signature365 <c>8b40ecc961</c>,
    /// <c>Blueprint.Http/ProblemDetailsJsonConverter.cs:45-92</c>): <c>type</c>, <c>title</c>, <c>detail</c>,
    /// <c>instance</c> and <c>status</c> by their exact names (<c>reader.ValueTextEquals</c>), and every other member, one of
    /// those five in another casing included, as an extension.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>The problem, or <see langword="null"/> for a JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">
    /// The body is not JSON, not an object, or has one of the five members as a value of the wrong kind, as legacy's reader
    /// threw for one.
    /// </exception>
    private ProblemDetailsDto? ReadProblemDetails(string body)
    {
        // The client's own reading options apply, except that a document can skip comments but not keep them.
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions
        {
            AllowTrailingCommas = this._jsonOptions.AllowTrailingCommas,
            CommentHandling = this._jsonOptions.ReadCommentHandling == JsonCommentHandling.Disallow ? JsonCommentHandling.Disallow : JsonCommentHandling.Skip,
            MaxDepth = this._jsonOptions.MaxDepth,
        });
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"The error response is a JSON {root.ValueKind}, not an object.");
        }

        var problem = new ProblemDetailsDto();

        foreach (var member in root.EnumerateObject())
        {
            switch (member.Name)
            {
                case "type":
                    problem.Type = ReadProblemString(member);
                    break;
                case "title":
                    problem.Title = ReadProblemString(member);
                    break;
                case "detail":
                    problem.Detail = ReadProblemString(member);
                    break;
                case InstanceMemberName:
                    problem.Instance = ReadProblemString(member);
                    break;
                case "status":
                    problem.Status = member.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.Number when member.Value.TryGetInt32(out var status) => status,
                        _ => throw new JsonException($"The problem's status is not an integer: {member.Value.GetRawText()}"),
                    };
                    break;
                default:
                    (problem.Extensions ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[member.Name] = member.Value.Clone();
                    break;
            }
        }

        return problem;
    }

    private static string? ReadProblemString(JsonProperty member)
    {
        return member.Value.ValueKind switch
        {
            JsonValueKind.String => member.Value.GetString(),
            JsonValueKind.Null => null,
            _ => throw new JsonException($"The problem's {member.Name} is not a string: {member.Value.GetRawText()}"),
        };
    }

    /// <summary>
    /// An extension value as legacy's <c>ProblemDetailsJsonConverter</c> read it: a string as a string, a number as a
    /// <see cref="double"/>, true and false as a <see cref="bool"/>, null as <see langword="null"/>, and anything else as the
    /// <see cref="JsonElement"/> <c>JsonSerializer.Deserialize(object)</c> gives.
    /// </summary>
    private static object? ToLegacyExtensionValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.Clone(),
        };
    }

    private static string DescribeRequestedResource(HttpRequestMessage request)
    {
        return request.RequestUri switch
        {
            { IsAbsoluteUri: true } uri => uri.PathAndQuery,
            { } uri => uri.OriginalString,
            null => "the requested resource",
        };
    }

    private async Task<TResponse?> HandleResponseAsync<TResponse>(
        string operationName,
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // StatusCodeResult subtypes (e.g. StatusCodeResult.Accepted): the server returns no body,
        // just a status code. Find the singleton Instance field and match by status code. This is
        // the exact pattern S365 gateway code uses via CallAsync<StatusCodeResult.Accepted>(...).
        if (typeof(StatusCodeResult).IsAssignableFrom(typeof(TResponse)) && typeof(StatusCodeResult) != typeof(TResponse))
        {
            // Validate before consuming the response body — error responses should still throw
            await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);

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

        await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);

        // A Results<T1, T2>: the host wrote one member, so read the response as that member, not as the union.
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Results<,>))
        {
            var union = await this.ReadUnionMemberAsync(typeof(TResponse), response, cancellationToken).ConfigureAwait(false);

            if (union is not null)
            {
                return (TResponse)union;
            }

            // A response no member claims, a 204 included, is a contract mismatch: a default union would hold no member at
            // all. Legacy's client threw for any response it could not understand (Signature365 8b40ecc961,
            // Common.Core/Api/BlueprintHttpClient.cs:228).
            throw new InvalidOperationException(
                $"The response ({(int)response.StatusCode}, {response.Content.Headers.ContentType?.MediaType ?? "no content"}) " +
                $"matches none of the results {typeof(TResponse)} declares.");
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            // A result the host writes as an empty 204 is read as that result, as a union's member is. Otherwise, as legacy's
            // client had nothing to read in a 204, it is a StatusCodeResult of the status when the response type can hold
            // one, and anything else throws (Signature365 8b40ecc961, Common.Core/Api/BlueprintHttpClient.cs:199-228).
            // StatusCodeResult is abstract here, so the 204's is its own nested result.
            if (CreateBodilessResult(typeof(TResponse), (int)HttpStatusCode.NoContent) is { } bodiless)
            {
                return (TResponse)bodiless;
            }

            if (typeof(StatusCodeResult).IsAssignableTo(typeof(TResponse)))
            {
                return (TResponse)(object)StatusCodeResult.NoContent.Instance;
            }

            throw new InvalidOperationException(
                $"Could not understand the response of calling {operationName}: {DescribeRequestedResource(request)}");
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

        // A resource event is read as legacy read one, not by constructor (ResourceEventReader).
        if (ResourceEventReader.CanRead(typeof(TResponse)))
        {
            return (TResponse)(object)await ResourceEventReader.ReadAsync(typeof(TResponse), response.Content, this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        // A ResourceEventResult<T> is read from the envelope the host writes for it, which no constructor of it binds.
        if (IsResourceEventResultType(typeof(TResponse)))
        {
            return (TResponse)await this.ReadResourceEventResultAsync(typeof(TResponse), response.Content, cancellationToken).ConfigureAwait(false);
        }

        // JSON deserialization
        return await response.Content.ReadFromJsonAsync<TResponse>(this._jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a successful response as the member of the <see cref="Results{T1, T2}"/> the host wrote. With no body, the
    /// first <see cref="StatusCodeResult"/> member whose status it is claims it: a nested status result by its own status,
    /// the base type by the nested result of the response's status; as do an <see cref="HttpNoContentResult"/> a 204, a
    /// <see cref="NoResultOperationResult"/> a 200, an <see cref="HttpAcceptedResult"/> a 202 and a
    /// <see cref="KeyOnlyResourceEventResult"/> the 201, 200 or 204 of its change type, which the host writes without a body.
    /// With a body that is not JSON, the first
    /// <see cref="PlainTextResult"/> member claims it. With a JSON body, the one member the host writes as a JSON body of
    /// that status claims it: a plain type or a resource event at a 200, an <see cref="OkResult{T}"/>,
    /// <see cref="CreatedResult{T}"/> or <see cref="AcceptedResult{T}"/> at its own status, read as its content, a
    /// <see cref="ResourceEventResult{T}"/> at a 201 or 200, read from its envelope.
    /// </summary>
    /// <remarks>
    /// When more than one member could be that JSON body, the body cannot say which one the host wrote, so rather than read
    /// it as whichever is declared first this throws, naming them: a runtime guard, as the union is the caller's choice of
    /// response type and so is only known at the call. Resource events of distinct resources, legacy ones or
    /// <see cref="ResourceEventResult{T}"/>s, are the exception: the body's <c>resourceObject</c> says which.
    /// </remarks>
    /// <param name="unionType">The closed <see cref="Results{T1, T2}"/> type.</param>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The union holding the member read, or <see langword="null"/> when the response is none of them.</returns>
    /// <exception cref="InvalidOperationException">More than one member could be the JSON body the host wrote.</exception>
    private async Task<object?> ReadUnionMemberAsync(Type unionType, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var isJson = mediaType is not null
                     && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                         || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
        var status = (int)response.StatusCode;
        var memberTypes = unionType.GetGenericArguments();

        if (isJson)
        {
            var candidates = memberTypes.Where(memberType => IsJsonMemberOfStatus(memberType, status)).ToArray();

            if (candidates.Length > 1 && candidates.All(static c => GetEventResourceType(c) is not null))
            {
                candidates = await SelectEventsOfResourceObjectAsync(candidates, response, cancellationToken).ConfigureAwait(false);
            }

            if (candidates.Length > 1)
            {
                throw new InvalidOperationException(
                    $"The response ({status}, {mediaType}) could be more than one of the results {unionType} declares " +
                    $"({string.Join(", ", candidates.Select(static c => c.ToString()))}), so which the host wrote cannot be told.");
            }

            return candidates.Length == 1
                ? ToUnion(unionType, candidates[0], await this.ReadJsonMemberAsync(candidates[0], response, cancellationToken).ConfigureAwait(false))
                : null;
        }

        foreach (var memberType in memberTypes)
        {
            var member = mediaType is null ? CreateBodilessResult(memberType, status) : null;

            if (memberType == typeof(PlainTextResult) && mediaType is not null)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                member = new PlainTextResult(text, response.StatusCode) { ContentType = mediaType };
            }

            if (member is not null)
            {
                return ToUnion(unionType, memberType, member);
            }
        }

        return null;
    }

    /// <summary>
    /// Narrows resource events that could each be the body to those of the resource the body names. The host writes an
    /// event's <c>resourceObject</c> as its resource's type name (<see cref="ApiResource.GetTypeName"/>), fixed by the
    /// event's type, where a legacy event's <c>eventId</c> carries a sub-id its handler chose at run time and a
    /// <see cref="ResourceEventResult{T}"/>'s is its handler's too. So two events of distinct resources, which the generator
    /// accepts in one union, are told apart by it, as the OpenAPI document's discriminator says for legacy events.
    /// </summary>
    /// <param name="candidates">The event members that could be the body: legacy events or <see cref="ResourceEventResult{T}"/>s.</param>
    /// <param name="response">The response, whose content is buffered so it can be read again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The candidates of the resource the body names, or all of them when it names none.</returns>
    private static async Task<Type[]> SelectEventsOfResourceObjectAsync(Type[] candidates, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Read as bytes, which buffers the content without consuming the stream the member is read from afterwards.
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("resourceObject", out var resourceObject)
            || resourceObject.ValueKind != JsonValueKind.String)
        {
            return candidates;
        }

        var name = resourceObject.GetString();
        var matching = candidates
            .Where(candidate => GetEventResourceType(candidate) is { } resourceType
                                && string.Equals(ApiResource.GetTypeName(resourceType), name, StringComparison.Ordinal))
            .ToArray();

        return matching.Length == 0 ? candidates : matching;
    }

    /// <summary>
    /// The resource type of a legacy <see cref="ResourceEvent{TResource}"/> member or of a
    /// <see cref="ResourceEventResult{T}"/> member, or <see langword="null"/> for any other member, the non-generic
    /// <see cref="ResourceEvent"/> included.
    /// </summary>
    private static Type? GetEventResourceType(Type eventType)
    {
        if (IsResourceEventResultType(eventType))
        {
            return eventType.GetGenericArguments()[0];
        }

        for (var current = eventType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ResourceEvent<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a closed <see cref="ResourceEventResult{T}"/>, which the host writes as its
    /// envelope.
    /// </summary>
    private static bool IsResourceEventResultType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResourceEventResult<>);
    }

    /// <summary>
    /// Reads the envelope the host writes for a <see cref="ResourceEventResult{T}"/>
    /// (<c>HttpOperationResultExecutor.WriteResourceEventEnvelopeAsync</c>) as <paramref name="resultType"/>: its
    /// <c>changeType</c>, <c>eventId</c>, <c>data</c> as the content, the <c>metadata</c>, <c>secureData</c> and
    /// <c>changedValues</c> the host writes when set, each read as a resource event's are
    /// (<see cref="ResourceEventReader.ReadDictionary"/>), and the <c>correlationId</c>. The envelope carries no resource
    /// id, so the result's is empty.
    /// </summary>
    /// <param name="resultType">A closed <see cref="ResourceEventResult{T}"/>.</param>
    /// <param name="content">The response content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidOperationException">When the body is not an envelope with a change type and data.</exception>
    private async Task<object> ReadResourceEventResultAsync(Type resultType, HttpContent content, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("changeType", out var changeTypeElement)
            || changeTypeElement.ValueKind != JsonValueKind.String
            || !Enum.TryParse<ChangeType>(changeTypeElement.GetString(), out var changeType)
            || !root.TryGetProperty("data", out var dataElement)
            || dataElement.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException($"The response is not the resource event envelope a {resultType} is written as.");
        }

        var eventId = root.TryGetProperty("eventId", out var eventIdElement) && eventIdElement.ValueKind == JsonValueKind.String
            ? eventIdElement.GetString()!
            : string.Empty;
        var data = dataElement.Deserialize(resultType.GetGenericArguments()[0], this._jsonOptions)!;
        var result = (ResourceEventResult)Activator.CreateInstance(
            resultType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [changeType, eventId, data],
            culture: null)!;

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "metadata", this._jsonOptions))
        {
            result.WithMetadata(key, value);
        }

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "secureData", this._jsonOptions))
        {
            result.WithSecureData(key, value);
        }

        foreach (var (key, value) in ReadEnvelopeDictionary(root, "changedValues", this._jsonOptions))
        {
            result.WithChangedValue(key, value);
        }

        if (root.TryGetProperty("correlationId", out var correlationIdElement) && correlationIdElement.ValueKind == JsonValueKind.String)
        {
            result.CorrelationId = correlationIdElement.GetString();
        }

        return result;
    }

    private static Dictionary<string, object> ReadEnvelopeDictionary(JsonElement root, string propertyName, JsonSerializerOptions options)
    {
        return root.TryGetProperty(propertyName, out var element) && ResourceEventReader.ReadDictionary(element, options) is { } dictionary
            ? dictionary
            : [];
    }

    /// <summary>
    /// The <paramref name="resultType"/> a response with no body and <paramref name="status"/> is, when the host writes
    /// one that way: a <see cref="StatusCodeResult"/> by its status, an <see cref="HttpNoContentResult"/> as a 204, a
    /// <see cref="NoResultOperationResult"/> as a 200, an <see cref="HttpAcceptedResult"/> as a 202, and a
    /// <see cref="KeyOnlyResourceEventResult"/> as the 201, 200 or 204 of its change type.
    /// </summary>
    /// <returns>The result, or <see langword="null"/> when the host writes no <paramref name="resultType"/> so.</returns>
    private static object? CreateBodilessResult(Type resultType, int status)
    {
        if (typeof(StatusCodeResult).IsAssignableFrom(resultType))
        {
            return FindStatusCodeResult(resultType, status);
        }

        if (resultType == typeof(HttpNoContentResult))
        {
            return status == (int)HttpStatusCode.NoContent ? new HttpNoContentResult() : null;
        }

        if (resultType == typeof(NoResultOperationResult))
        {
            return status == (int)HttpStatusCode.OK ? NoResultOperationResult.Instance : null;
        }

        if (resultType == typeof(HttpAcceptedResult))
        {
            return status == (int)HttpStatusCode.Accepted ? new HttpAcceptedResult() : null;
        }

        // The host writes a key-only event's change type as its status, and nothing else of it.
        if (resultType == typeof(KeyOnlyResourceEventResult))
        {
            return TryGetKeyOnlyChangeType(status, out var changeType) ? new KeyOnlyResourceEventResult(changeType) : null;
        }

        return null;
    }

    /// <summary>
    /// The change type a <see cref="KeyOnlyResourceEventResult"/> the host wrote with <paramref name="status"/> had: the host
    /// sends a created one as a 201, an updated one as a 200 and a deleted one as a 204
    /// (<c>HttpOperationResultExecutor.MapChangeTypeToStatusCode</c>).
    /// </summary>
    private static bool TryGetKeyOnlyChangeType(int status, out ChangeType changeType)
    {
        (var found, changeType) = status switch
        {
            (int)HttpStatusCode.Created => (true, ChangeType.Created),
            (int)HttpStatusCode.OK => (true, ChangeType.Updated),
            (int)HttpStatusCode.NoContent => (true, ChangeType.Deleted),
            _ => (false, default(ChangeType)),
        };

        return found;
    }

    /// <summary>
    /// Makes the union holding <paramref name="member"/>: the union's constructor is private, so its implicit conversion
    /// from the member is how a value is made.
    /// </summary>
    private static object? ToUnion(Type unionType, Type memberType, object? member)
    {
        return member is null
            ? null
            : unionType.GetMethod("op_Implicit", BindingFlags.Public | BindingFlags.Static, [memberType])!.Invoke(null, [member]);
    }

    /// <summary>
    /// The <see cref="StatusCodeResult"/> a body-less response of <paramref name="status"/> is, as <paramref name="memberType"/>:
    /// a nested status result's own singleton when the status is its own, or, for the base type, the singleton of whichever
    /// nested result's status it is, as legacy's client made a <c>StatusCodeResult</c> of any status (Signature365
    /// <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:223-226</c>).
    /// </summary>
    private static StatusCodeResult? FindStatusCodeResult(Type memberType, int status)
    {
        var candidates = memberType == typeof(StatusCodeResult)
            ? typeof(StatusCodeResult).GetNestedTypes(BindingFlags.Public).Where(static t => typeof(StatusCodeResult).IsAssignableFrom(t))
            : [memberType];

        return candidates
            .Select(static t => t.GetField(nameof(StatusCodeResult.Accepted.Instance), BindingFlags.Public | BindingFlags.Static)?.GetValue(null))
            .OfType<StatusCodeResult>()
            .FirstOrDefault(singleton => singleton.StatusCode == status);
    }

    /// <summary>
    /// Whether the host writes a <paramref name="memberType"/> as a JSON body with <paramref name="status"/>: a type that is
    /// not an operation result, or a resource event, as a 200 only; an <see cref="OkResult{T}"/>,
    /// <see cref="CreatedResult{T}"/> or <see cref="AcceptedResult{T}"/> as its content with its own status; a
    /// <see cref="ResourceEventResult{T}"/> as its envelope with the 201 or 200 of a created or updated one (a deleted one is
    /// an empty 204, which carries no content to read). No other operation result is read back from a body.
    /// </summary>
    /// <remarks>
    /// The host writes nothing else: a handler's plain value is sent as an <see cref="OkResult{T}"/>, and a resource event
    /// as a 200 whatever its change type, as legacy's <c>OkResult</c> sent one. Claiming either at another status would
    /// make, for example, <c>Results&lt;ResourceCreated&lt;T&gt;, CreatedResult&lt;T&gt;&gt;</c>, a union the generator
    /// accepts, refuse the <see cref="CreatedResult{T}"/>'s 201 as a body two members could be.
    /// </remarks>
    private static bool IsJsonMemberOfStatus(Type memberType, int status)
    {
        if (typeof(ResourceEvent).IsAssignableFrom(memberType) || !typeof(OperationResult).IsAssignableFrom(memberType))
        {
            return status == (int)HttpStatusCode.OK;
        }

        if (!memberType.IsGenericType)
        {
            return false;
        }

        var definition = memberType.GetGenericTypeDefinition();

        return (definition == typeof(OkResult<>) && status == (int)HttpStatusCode.OK)
               || (definition == typeof(CreatedResult<>) && status == (int)HttpStatusCode.Created)
               || (definition == typeof(AcceptedResult<>) && status == (int)HttpStatusCode.Accepted)
               || (definition == typeof(ResourceEventResult<>) && status is (int)HttpStatusCode.OK or (int)HttpStatusCode.Created);
    }

    /// <summary>
    /// Reads the JSON body as <paramref name="memberType"/>: a resource event or <see cref="ResourceEventResult{T}"/> from
    /// its envelope, another semantic result as its content wrapped in it.
    /// </summary>
    private async Task<object?> ReadJsonMemberAsync(Type memberType, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (ResourceEventReader.CanRead(memberType))
        {
            return await ResourceEventReader.ReadAsync(memberType, response.Content, this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (IsResourceEventResultType(memberType))
        {
            return await this.ReadResourceEventResultAsync(memberType, response.Content, cancellationToken).ConfigureAwait(false);
        }

        if (!typeof(OperationResult).IsAssignableFrom(memberType) || typeof(ResourceEvent).IsAssignableFrom(memberType))
        {
            return await response.Content.ReadFromJsonAsync(memberType, this._jsonOptions, cancellationToken).ConfigureAwait(false);
        }

        var contentType = memberType.GetGenericArguments()[0];
        var content = await response.Content.ReadFromJsonAsync(contentType, this._jsonOptions, cancellationToken).ConfigureAwait(false);

        return content is null ? null : Activator.CreateInstance(memberType, content);
    }

    private async Task EnsureSuccessOrThrowAsync(
        HttpRequestMessage request,
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
            throw this.ParseValidationException(response, body);
        }

        throw this.CreateErrorResponseException(request, response, body);
    }

    /// <summary>
    /// Reads a 422 as legacy's client read one, as a <c>ValidationProblemDetails</c> (Signature365 <c>8b40ecc961</c>,
    /// <c>Common.Core/Api/BlueprintHttpClient.cs:125-135</c>): the errors are the member named exactly <c>errors</c>, the
    /// name the host writes (<c>Blueprint.Http/ValidationProblemDetailsJsonConverter.cs:13</c>, <c>:30</c>), read into a
    /// dictionary that is empty without one (<c>:20-21</c>), and the exception is thrown with that dictionary whatever it
    /// holds (<c>:129-131</c>). A JSON <c>null</c> is a <c>response_empty</c> problem (<c>:134</c>).
    /// </summary>
    /// <param name="response">The 422 response.</param>
    /// <param name="body">The response body.</param>
    /// <returns>The exception to throw.</returns>
    private Exception ParseValidationException(HttpResponseMessage response, string body)
    {
        try
        {
            // The client's own reading options apply, as ReadProblemDetails applies them.
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowTrailingCommas = this._jsonOptions.AllowTrailingCommas,
                CommentHandling = this._jsonOptions.ReadCommentHandling == JsonCommentHandling.Disallow ? JsonCommentHandling.Disallow : JsonCommentHandling.Skip,
                MaxDepth = this._jsonOptions.MaxDepth,
            });
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Null)
            {
                return new BlueprintApiException(
                    new BlueprintApiException.ApiProblem(
                        "Response deserialisation failed",
                        "response_empty",
                        "The validation error response could not be deserialised",
                        (int)HttpStatusCode.BadGateway),
                    response.StatusCode,
                    body,
                    innerException: null);
            }

            var errors = root.ValueKind == JsonValueKind.Object
                         && root.TryGetProperty("errors", out var errorsElement)
                         && errorsElement.Deserialize<Dictionary<string, string[]>>(this._jsonOptions) is { } readErrors
                ? readErrors
                : [];

            return new ValidationException(errors);
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
    /// An RFC 9457 / RFC 7807 problem details response as <see cref="ReadProblemDetails"/> reads one, keeping any further
    /// members.
    /// </summary>
    private sealed class ProblemDetailsDto
    {
        public string? Type { get; set; }

        public string? Title { get; set; }

        public string? Detail { get; set; }

        public int? Status { get; set; }

        /// <summary>
        /// Gets or sets the reference to this occurrence, read so that it is not taken for an extension, and never passed
        /// on: legacy's converter read it into a member of its own, which its client never carried.
        /// </summary>
        public string? Instance { get; set; }

        public Dictionary<string, JsonElement>? Extensions { get; set; }
    }
}

