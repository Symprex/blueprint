using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.Validation;

namespace Blueprint.HttpClient;

/// <summary>
/// The transport every Blueprint HTTP call is sent through: one send primitive that builds the request, applies the
/// caller's <see cref="CallOptions.ModifyRequest"/>, sends it, maps a failure to the exception legacy's client threw, and
/// reads a success with a response reader chosen for the call's result type.
/// </summary>
/// <remarks>
/// <see cref="BlueprintHttpClient{TApp}"/> builds each request from the operation's descriptor and chooses its reader from
/// the result type it is asked for; both go through the same primitive and readers, so there is one wire implementation.
/// </remarks>
public partial class BlueprintHttpClient
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
    /// The options a serializer context is inserted into, by the options they were copied from and the context.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConcurrentDictionary<IJsonTypeInfoResolver, JsonSerializerOptions>> _derivedOptions = new();

    private readonly System.Net.Http.HttpClient? _httpClient;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured <see cref="System.Net.Http.HttpClient"/>, whose base address the calls' relative URLs resolve against.</param>
    /// <param name="jsonOptions">JSON serialization options, defaulting to Blueprint's standard camelCase config.</param>
    public BlueprintHttpClient(System.Net.Http.HttpClient httpClient, JsonSerializerOptions? jsonOptions = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.JsonOptions = jsonOptions ?? BlueprintJsonOptions.CreateDefaultOptions();
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpClient"/> class for a subclass that supplies its own
    /// <see cref="HttpClient"/>, as a subclass of the abstract compatibility base did before this type sent calls itself.
    /// </summary>
    protected BlueprintHttpClient()
    {
        this.JsonOptions = BlueprintJsonOptions.CreateDefaultOptions();
    }

    /// <summary>
    /// Gets the inner HTTP client the calls are sent with, as legacy's client exposed it (Signature365
    /// <c>Common.Core/Api/BlueprintHttpClient.cs:47-50</c>), so a caller can read or set its default headers.
    /// </summary>
    public virtual System.Net.Http.HttpClient HttpClient =>
        this._httpClient ?? throw new InvalidOperationException($"A {nameof(BlueprintHttpClient)} made without an HttpClient must override {nameof(this.HttpClient)}.");

    /// <summary>
    /// Gets the options requests are written and responses read with.
    /// </summary>
    internal JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// Sends <paramref name="operation"/> as <paramref name="plan"/> says, the call a generated typed client makes: the URL
    /// from the plan's builders, the operation as the JSON body of any method but GET, then the caller's
    /// <see cref="CallOptions.ModifyRequest"/>, the client's request modifiers and its credential applied in that order, and
    /// the response read with the plan's reader.
    /// </summary>
    /// <typeparam name="TOperation">The operation type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="plan">The operation's call plan.</param>
    /// <param name="operation">The operation to send.</param>
    /// <param name="options">The caller's call options.</param>
    /// <param name="requestModifier">The typed client's request modifiers, or <see langword="null"/>.</param>
    /// <param name="credential">The typed client's credential, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result read.</returns>
    /// <exception cref="InvalidOperationException">A route value of the operation is null.</exception>
    /// <exception cref="BlueprintApiException">For a failure status, a timeout or an unreachable server.</exception>
    /// <exception cref="ValidationException">For a 422 Unprocessable Entity.</exception>
    public Task<TResult> SendAsync<TOperation, TResult>(
        BlueprintCallPlan<TOperation, TResult> plan,
        TOperation operation,
        CallOptions? options,
        Action<HttpRequestMessage>? requestModifier,
        IUpstreamCredential? credential,
        CancellationToken cancellationToken = default)
        where TOperation : notnull
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);

        var url = plan.BuildUrl(operation)
            ?? throw new InvalidOperationException(
                $"The URL builder of '{plan.OperationName}' returned null. A required route parameter is likely null.");

        var jsonOptions = plan.SerializerContext is null ? this.JsonOptions : GetDerivedOptions(this.JsonOptions, plan.SerializerContext);
        HttpContent? content;

        if (plan.Method == HttpMethod.Get)
        {
            content = null;

            if (plan.AppendQueryString is not null)
            {
                url = plan.AppendQueryString(operation, url);
            }
        }
        else
        {
            // Written as the declared operation type, whose metadata the options resolve once and cache.
            content = JsonContent.Create(operation, mediaType: null, jsonOptions);
        }

        return this.SendCoreAsync(plan.Method, url, content, plan.OperationName, plan.Reader, jsonOptions, options, requestModifier, credential, cancellationToken);
    }

    /// <summary>
    /// A copy of <paramref name="options"/> with <paramref name="serializerContext"/> first in its resolver chain, made once
    /// per options and context pair. The options' converters still apply, and a type the context lacks falls back to the
    /// options' own resolvers, or to reflection metadata when the options had none.
    /// </summary>
    private static JsonSerializerOptions GetDerivedOptions(JsonSerializerOptions options, IJsonTypeInfoResolver serializerContext)
    {
        var byContext = _derivedOptions.GetValue(options, static _ => new ConcurrentDictionary<IJsonTypeInfoResolver, JsonSerializerOptions>());

        return byContext.GetOrAdd(serializerContext, CreateDerivedOptions, options);
    }

    private static JsonSerializerOptions CreateDerivedOptions(IJsonTypeInfoResolver context, JsonSerializerOptions source)
    {
        var derived = new JsonSerializerOptions(source);

        if (derived.TypeInfoResolverChain.Count == 0)
        {
            derived.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        derived.TypeInfoResolverChain.Insert(0, context);
        derived.MakeReadOnly();

        return derived;
    }

    /// <summary>
    /// The send primitive: builds the request, applies <paramref name="options"/>' <see cref="CallOptions.ModifyRequest"/>,
    /// then <paramref name="requestModifier"/>, then <paramref name="credential"/>, sends it, throws for a failure status,
    /// and reads a success with <paramref name="reader"/>.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">The URL, relative to the client's base address; a leading <c>/</c> is stripped.</param>
    /// <param name="content">The request body, or <see langword="null"/> for none.</param>
    /// <param name="operationName">The operation's type name, which a failure to understand the response names.</param>
    /// <param name="reader">The reader of a success response.</param>
    /// <param name="jsonOptions">The options the response body is read with.</param>
    /// <param name="options">The caller's call options.</param>
    /// <param name="requestModifier">The typed client's request modifiers, applied after <paramref name="options"/>.</param>
    /// <param name="credential">The typed client's credential, applied last.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result read.</returns>
    /// <exception cref="BlueprintApiException">For a failure status, a timeout or an unreachable server.</exception>
    /// <exception cref="ValidationException">For a 422 Unprocessable Entity.</exception>
    internal async Task<TResult> SendCoreAsync<TResult>(
        HttpMethod method,
        string url,
        HttpContent? content,
        string operationName,
        BlueprintResponseReader<TResult> reader,
        JsonSerializerOptions jsonOptions,
        CallOptions? options,
        Action<HttpRequestMessage>? requestModifier,
        IUpstreamCredential? credential,
        CancellationToken cancellationToken)
    {
        // Strip the leading '/' so the URL is treated as relative to the HttpClient's BaseAddress.
        // Without this, HttpClient treats "/templates" as an absolute path from the host root,
        // discarding the BaseAddress path component (e.g. "http://host/api/" → "http://host/templates").
        if (url.Length > 0 && url[0] == '/')
        {
            url = url[1..];
        }

        using var request = new HttpRequestMessage(method, url) { Content = content };
        options?.ModifyRequest?.Invoke(request);
        requestModifier?.Invoke(request);

        if (credential is not null)
        {
            await credential.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var response = await this.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);

            return await reader.ReadAsync(new BlueprintResponseContext(request, response, jsonOptions, operationName), cancellationToken).ConfigureAwait(false);
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
                    $"Response from {BlueprintResponseContext.DescribeRequestedResource(request)} was 404",
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
            AllowTrailingCommas = this.JsonOptions.AllowTrailingCommas,
            CommentHandling = this.JsonOptions.ReadCommentHandling == JsonCommentHandling.Disallow ? JsonCommentHandling.Disallow : JsonCommentHandling.Skip,
            MaxDepth = this.JsonOptions.MaxDepth,
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
                AllowTrailingCommas = this.JsonOptions.AllowTrailingCommas,
                CommentHandling = this.JsonOptions.ReadCommentHandling == JsonCommentHandling.Disallow ? JsonCommentHandling.Disallow : JsonCommentHandling.Skip,
                MaxDepth = this.JsonOptions.MaxDepth,
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
                         && errorsElement.Deserialize<Dictionary<string, string[]>>(this.JsonOptions) is { } readErrors
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
