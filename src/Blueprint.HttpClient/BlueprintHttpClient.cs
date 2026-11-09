using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Blueprint.Http;
using Blueprint.HttpClient.Readers;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;
using Blueprint.Validation;

namespace Blueprint.HttpClient;

/// <summary>
/// The transport every Blueprint HTTP call goes through. It sends an operation to a remote Blueprint API host with the
/// HTTP method and URL its caller resolved, maps a failure as the remote API reported it, and hands a success response to
/// the <see cref="ResponseReader{T}"/> its caller chose.
/// </summary>
/// <remarks>
/// <para>
/// A generated typed client calls <see cref="SendAsync{TOperation, TResult}"/> with a constant HTTP method, the
/// operation's own request builders and a reader chosen at compile time, so its call looks nothing up.
/// <see cref="BlueprintHttpClient{TApp}"/> resolves the same three from its app's operation descriptors and calls the same
/// primitive, so the two send and read a request identically.
/// </para>
/// <para>
/// An instance holds nothing a call changes, so it is safe to use from many threads at once, as its
/// <see cref="System.Net.Http.HttpClient"/> is.
/// </para>
/// </remarks>
public class BlueprintHttpClient
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
    /// The options derived from each options instance for each context, so that a transient typed client resolved many times
    /// copies its options once. Keyed weakly, so neither the options nor their derivations outlive their users.
    /// </summary>
    private static readonly ConditionalWeakTable<JsonSerializerOptions, ConditionalWeakTable<JsonSerializerContext, JsonSerializerOptions>> _derivedOptions = new();

    /// <summary>
    /// The options of every transport given none: Blueprint's defaults, one instance so that what is cached by options (the
    /// metadata resolved from them, the options derived from them for a context, a typed client's plan) is made once, and
    /// locked so that no holder of it can change it under the others. Made on first use, so that a process with System.Text.Json's
    /// reflection turned off, whose transports are all given metadata of their own, never makes options that need it.
    /// </summary>
    private static readonly Lazy<JsonSerializerOptions> _defaultJsonOptions = new(CreateDefaultJsonOptions, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly System.Net.Http.HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintHttpClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured <see cref="System.Net.Http.HttpClient"/>, whose base address the URLs are relative to.</param>
    /// <param name="jsonOptions">
    /// JSON serialization options; by default Blueprint's standard camelCase options, one read-only instance that every
    /// transport given none shares.
    /// </param>
    public BlueprintHttpClient(System.Net.Http.HttpClient httpClient, JsonSerializerOptions? jsonOptions = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this._jsonOptions = jsonOptions ?? _defaultJsonOptions.Value;
    }

    /// <summary>
    /// Gets the inner HTTP client the calls are sent with, as legacy's client exposed it (Signature365
    /// <c>Common.Core/Api/BlueprintHttpClient.cs:47-50</c>), so a caller can read or set its default headers.
    /// </summary>
    public System.Net.Http.HttpClient HttpClient => this._httpClient;

    /// <summary>
    /// Gets the JSON options every call reads and writes with, by which a typed client finds the metadata it resolved from
    /// them once. They are read-only once a call has used them or metadata has been resolved from them.
    /// </summary>
    public JsonSerializerOptions JsonOptions => this._jsonOptions;

    /// <summary>
    /// Makes a client that sends with the same <see cref="HttpClient"/> and reads and writes with the metadata of a
    /// serializer context, as well as this client's JSON options, whose converters still apply.
    /// </summary>
    /// <remarks>
    /// The options are derived once for each pair of this client's options instance and the context, then reused, so making
    /// such a client for each resolve of a transient typed client over the same options copies nothing after the first: a
    /// transport given no options shares one default instance, and one given them from DI is given the same instance each time.
    /// Options still open to change are derived afresh each time instead, as what was derived from them would otherwise be
    /// returned after they changed; they are read-only once a serializer call or <see cref="ResolveTypeInfo{T}"/> has used them.
    /// </remarks>
    /// <param name="context">The context whose metadata is consulted before the options' own.</param>
    /// <returns>The new client; this client is unchanged.</returns>
    public BlueprintHttpClient WithSerializerContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new BlueprintHttpClient(this._httpClient, GetDerivedOptions(this._jsonOptions, context));
    }

    /// <summary>
    /// Resolves the metadata this client's JSON options give a type, for a reader or a request to be given once rather than
    /// to look it up on every call.
    /// </summary>
    /// <remarks>
    /// Options no serializer call has used yet are locked first, as that first use would lock them: until then options without
    /// a resolver of their own (<see cref="JsonSerializerDefaults.Web"/>'s, Blueprint's defaults) resolve no metadata, and
    /// options open to change cache none, so the same type's metadata could not be resolved once.
    /// </remarks>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The type's metadata, the one instance the options hold for it.</returns>
    public JsonTypeInfo<T> ResolveTypeInfo<T>()
    {
        if (!this._jsonOptions.IsReadOnly)
        {
            this._jsonOptions.MakeReadOnly(populateMissingResolver: true);
        }

        return (JsonTypeInfo<T>)this._jsonOptions.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// Sends an operation to the remote API with the given HTTP method and URL, and reads a successful response with
    /// <paramref name="reader"/>. Every Blueprint call is sent by this method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GET carries the operation's other properties in its URL, so it has no body; every other method, a DELETE included,
    /// sends the operation as its JSON body, as legacy's client sent them (Signature365 <c>8b40ecc961</c>,
    /// <c>Common.Core/Api/BlueprintHttpClient.cs:79</c>, <c>:88-91</c>) and as the host reads them.
    /// </para>
    /// <para>
    /// <paramref name="options"/>' <see cref="CallOptions.ModifyRequest"/> runs on the built request, then
    /// <paramref name="requestModifier"/>, then <paramref name="credential"/> is applied, so the credential wins over anything
    /// either set, before it is sent.
    /// A request that cannot be sent, or answers late, fails as a <see cref="BlueprintApiException"/>, as does any response
    /// that is not a success; a 422 fails as a <see cref="ValidationException"/>. A reader only ever reads a success.
    /// </para>
    /// <para>
    /// An <see cref="System.Net.Http.HttpClient"/> that answers at the headers
    /// (<see cref="HttpCompletionOption.ResponseHeadersRead"/>, or a handler that streams) leaves the body to the reader. A
    /// connection that fails during that read fails as the same <c>service_unavailable</c> exception as one that fails while
    /// sending, but <see cref="System.Net.Http.HttpClient.Timeout"/> does not cover the read, so a body that stalls is bounded
    /// only by <paramref name="cancellationToken"/>; such a caller should pass one with a deadline.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOperation">The operation's type.</typeparam>
    /// <typeparam name="TResult">The type <paramref name="reader"/> reads a response as.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="url">
    /// The URL, relative to the <see cref="HttpClient"/>'s base address even with a leading <c>/</c>, and with a GET's
    /// query string already appended; <see langword="null"/> when a route placeholder had no value, as an operation's URL
    /// builder returns it, which fails the call before anything is sent.
    /// </param>
    /// <param name="operation">The operation to send.</param>
    /// <param name="operationTypeInfo">
    /// The metadata to write the body with, from this client's JSON options; or <see langword="null"/> to write it as the
    /// operation's run-time type with those options, as an instance of a type derived from <typeparamref name="TOperation"/>
    /// is written whatever this is.
    /// </param>
    /// <param name="reader">The reader of a successful response, chosen for the operation's declared result.</param>
    /// <param name="operationName">The operation's name, which a failure to read the response names.</param>
    /// <param name="options">Optional per-call request customisation.</param>
    /// <param name="requestModifier">
    /// The customisation the caller applies to each of its requests, such as a typed client's <c>WithRequestModifier</c>
    /// modifiers, run after <paramref name="options"/>' own; or <see langword="null"/> for none.
    /// </param>
    /// <param name="credential">The credential to authenticate the request with, applied last; or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response, as <paramref name="reader"/> read it.</returns>
    /// <exception cref="BlueprintApiException">The request could not be sent or timed out, or the response was not a success.</exception>
    /// <exception cref="ValidationException">The response was a 422 Unprocessable Entity.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="url"/> is <see langword="null"/>, or the response could not be read as the declared result.</exception>
    public async Task<TResult> SendAsync<TOperation, TResult>(
        HttpMethod method,
        string? url,
        TOperation operation,
        JsonTypeInfo<TOperation>? operationTypeInfo,
        ResponseReader<TResult> reader,
        string operationName,
        CallOptions? options,
        Action<HttpRequestMessage>? requestModifier = null,
        IUpstreamCredential? credential = null,
        CancellationToken cancellationToken = default)
        where TOperation : class, IOperation
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(operationName);

        using var request = this.CreateRequest(method, url, operation, operationTypeInfo);
        options?.ModifyRequest?.Invoke(request);
        requestModifier?.Invoke(request);

        if (credential is not null)
        {
            await credential.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var context = new ResponseReadContext(request, response, operationName, this._jsonOptions);

            if (!response.IsSuccessStatusCode)
            {
                // Only a forward's reader claims a failure (a declared NotFoundResult the host wrote); any other is mapped.
                var (claimed, value) = await reader.ReadFailureAsync(context, cancellationToken).ConfigureAwait(false);

                if (claimed)
                {
                    return value;
                }

                await this.EnsureSuccessOrThrowAsync(request, response, cancellationToken).ConfigureAwait(false);
            }

            return await reader.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            throw CreateTimeoutException(e);
        }
        catch (HttpRequestException e)
        {
            throw CreateUnreachableException(e);
        }
        catch (IOException e)
        {
            // A body the send did not buffer fails as the connection's own HttpIOException, which only a buffering send
            // wraps in an HttpRequestException.
            throw CreateUnreachableException(e);
        }
    }

    /// <summary>
    /// Forwards an inner operation to the remote Blueprint API that owns it, as a gateway's handler does, and reads the upstream
    /// outcome as the result the caller declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operation is sent with its own generated request builders, through <see cref="IHttpRequestBuilder{TSelf}"/>: the
    /// HTTP method and URL a generated typed client of it sends, with no app, descriptor or lookup. A GET carries its other
    /// properties in its query string; any other method sends the operation as its JSON body.
    /// </para>
    /// <para>
    /// <typeparamref name="TResult"/> is the type the response is read as: the inner operation's declared result, or a
    /// <see cref="Results{T1, T2}"/> of it, read exactly as a typed client reads it. When it is a <see cref="Results{T1, T2}"/>
    /// with a <see cref="Blueprint.Runtime.Exec.NotFoundResult{T}"/> member, an upstream 404 the host wrote as that result (its
    /// content, as JSON) becomes that member. Every other failure, a 404 problem document included, escapes as the
    /// <see cref="BlueprintApiException"/> (or the <see cref="ValidationException"/> of a 422) <see cref="SendAsync{TOperation, TResult}"/>
    /// raises, so a gateway answers the upstream problem unchanged.
    /// </para>
    /// <para>
    /// The two are told apart by content type: a host writes a <see cref="Blueprint.Runtime.Exec.NotFoundResult{T}"/> as
    /// <c>application/json</c> and a problem as <c>application/problem+json</c>. A forward asks for its errors as
    /// <c>Accept: application/problem+json</c>, so a host with <c>NegotiateErrorContentType</c> on, which otherwise writes a
    /// problem as <c>application/json</c> to a request with no <c>Accept</c>, still writes it as a problem. It replaces any
    /// <c>Accept</c> the caller sets through <see cref="CallOptions.ModifyRequest"/>, which would otherwise let a negotiating
    /// host's 404 problem be read as the not-found member.
    /// </para>
    /// <para>
    /// A forwarded resource arrives without the inner service's <c>$links</c>: <see cref="ApiResource.Links"/> is get-only and
    /// never read from JSON, and a gateway generates its own from its own routes.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInner">The inner operation's type, exactly: a forward sends a fresh instance of it and nothing else.</typeparam>
    /// <typeparam name="TResult">The type the response is read as.</typeparam>
    /// <param name="inner">
    /// The inner operation to send, constructed for this forward. An instance of a type derived from
    /// <typeparamref name="TInner"/>, such as the gateway's own operation, is refused, as it would be written as its run-time
    /// type with every property of its own.
    /// </param>
    /// <param name="options">Optional per-call request customisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upstream outcome, as <typeparamref name="TResult"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="inner"/> is an instance of a type derived from <typeparamref name="TInner"/>.</exception>
    /// <exception cref="BlueprintApiException">The request could not be sent or timed out, or the response was a failure no declared member claims.</exception>
    /// <exception cref="ValidationException">The response was a 422 Unprocessable Entity.</exception>
    /// <exception cref="InvalidOperationException">A route placeholder has no value, or the response could not be read as <typeparamref name="TResult"/>.</exception>
    public Task<TResult> ForwardAsync<TInner, TResult>(TInner inner, CallOptions? options = null, CancellationToken cancellationToken = default)
        where TInner : class, IOperation, IHttpRequestBuilder<TInner>
    {
        return this.ForwardCoreAsync<TInner, TResult>(inner, options, credential: null, cancellationToken);
    }

    /// <summary>
    /// Forwards an inner operation for the gateway operation <paramref name="context"/> serves, authenticated and with its
    /// upstream failures translated as that operation declares, and reads the upstream outcome as the result the caller
    /// declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sends and reads exactly as <see cref="ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/> does,
    /// and in addition (D7, D8):
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Credential.</b> The request is authenticated, last, with the caller's own <c>Authorization</c> header
    /// (<see cref="CallerAuthorizationCredential"/>), or, when the gateway operation carries an
    /// <see cref="UpstreamCredentialAttribute"/>, with the credential its <see cref="IUpstreamCredentialStrategy"/>, resolved from
    /// <see cref="ApiOperationContext.ServiceProvider"/>, creates for this forward.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Error translation.</b> A <see cref="BlueprintApiException"/> whose problem type a <see cref="TranslateErrorAttribute"/>
    /// on the gateway operation, or else on its app, names is raised as that attribute's local
    /// <see cref="ApiExceptionFactory"/> problem, with the upstream problem's detail and the upstream failure as its inner
    /// exception. Every other failure escapes unchanged.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// The gateway operation's attributes are read the first time it forwards, and cached.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInner">The inner operation's type, exactly: a forward sends a fresh instance of it and nothing else.</typeparam>
    /// <typeparam name="TResult">The type the response is read as.</typeparam>
    /// <param name="context">The gateway operation being served, whose caller and attributes the forward uses.</param>
    /// <param name="inner">The inner operation to send, constructed for this forward.</param>
    /// <param name="options">Optional per-call request customisation, applied before the credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upstream outcome, as <typeparamref name="TResult"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="inner"/> is an instance of a type derived from <typeparamref name="TInner"/>.</exception>
    /// <exception cref="ApiException">The upstream failed with a problem type the gateway operation or its app translates.</exception>
    /// <exception cref="BlueprintApiException">The request could not be sent or timed out, or the response was a failure no declared member claims and nothing translates.</exception>
    /// <exception cref="ValidationException">The response was a 422 Unprocessable Entity.</exception>
    /// <exception cref="InvalidOperationException">
    /// The gateway operation's credential strategy is not registered, or its attributes are invalid; a route placeholder has
    /// no value; or the response could not be read as <typeparamref name="TResult"/>.
    /// </exception>
    public async Task<TResult> ForwardAsync<TInner, TResult>(
        ApiOperationContext context,
        TInner inner,
        CallOptions? options = null,
        CancellationToken cancellationToken = default)
        where TInner : class, IOperation, IHttpRequestBuilder<TInner>
    {
        ArgumentNullException.ThrowIfNull(context);

        var policy = UpstreamForwardPolicy.For(context.Descriptor.OperationType, context.App.GetType());
        var credential = CreateForwardCredential(context, inner, policy);

        try
        {
            return await this.ForwardCoreAsync<TInner, TResult>(inner, options, credential, cancellationToken).ConfigureAwait(false);
        }
        catch (BlueprintApiException e) when (policy.FindTranslation(e.Type) is { } factory)
        {
            throw e.Detail is null ? factory.Create(e) : factory.Create(e, e.Detail);
        }
    }

    /// <summary>
    /// Asks a forward's errors to be written as <c>application/problem+json</c>, replacing any <c>Accept</c> the caller set; see
    /// <see cref="ForwardAsync{TInner, TResult}(TInner, CallOptions, CancellationToken)"/>. A Blueprint host reads <c>Accept</c> for nothing but an error's content
    /// type, so a success is written as it would be without it, and a caller's own <c>Accept</c> could only make a thrown 404
    /// look like a not-found result.
    /// </summary>
    /// <param name="request">The forward's request, after <see cref="CallOptions.ModifyRequest"/> has run.</param>
    private static void AcceptProblemJson(HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.TryAddWithoutValidation("Accept", "application/problem+json");
    }

    /// <summary>
    /// Describes the resource a request asked for by its path, never its host.
    /// </summary>
    /// <param name="request">The request, whose URI the client has made absolute once sent.</param>
    /// <returns>The path and query, or the URI as given when it is not absolute.</returns>
    internal static string DescribeRequestedResource(HttpRequestMessage request)
    {
        return request.RequestUri switch
        {
            { IsAbsoluteUri: true } uri => uri.PathAndQuery,
            { } uri => uri.OriginalString,
            null => "the requested resource",
        };
    }

    /// <summary>
    /// Creates the credential a forward from the gateway operation <paramref name="context"/> serves authenticates with.
    /// </summary>
    /// <param name="context">The gateway operation being served.</param>
    /// <param name="inner">The inner operation being forwarded, which a strategy reads its arguments from.</param>
    /// <param name="policy">The operation's forward policy.</param>
    /// <returns>The strategy's credential when the operation selects one; otherwise the caller's own.</returns>
    private static IUpstreamCredential CreateForwardCredential(ApiOperationContext context, IOperation inner, UpstreamForwardPolicy policy)
    {
        if (policy.CredentialAttribute is not { } attribute)
        {
            return CallerAuthorizationCredential.FromContext(context);
        }

        var strategy = context.ServiceProvider.GetService(attribute.CredentialType) as IUpstreamCredentialStrategy
            ?? throw new InvalidOperationException(
                $"{context.Descriptor.OperationType.Name} forwards with the upstream credential strategy {attribute.CredentialType.Name}, which is not registered in the request's services as an {nameof(IUpstreamCredentialStrategy)}. Register it, for example with services.AddSingleton<{attribute.CredentialType.Name}>().");

        return strategy.CreateCredential(context, inner, attribute);
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
    /// <param name="exception">
    /// The failure the handler raised: an <see cref="HttpRequestException"/>, or the <see cref="IOException"/> a body read
    /// raises when the send did not buffer the body.
    /// </param>
    /// <returns>The exception to throw.</returns>
    private static BlueprintApiException CreateUnreachableException(Exception exception)
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

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = BlueprintJsonOptions.CreateDefaultOptions();
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    private static JsonSerializerOptions GetDerivedOptions(JsonSerializerOptions options, JsonSerializerContext context)
    {
        if (!options.IsReadOnly)
        {
            // Not cached, as the options can still change, and what is derived from them is a copy of how they are now.
            return DeriveOptions(options, context);
        }

        var derivations = _derivedOptions.GetOrCreateValue(options);

        return derivations.TryGetValue(context, out var derived)
            ? derived
            : derivations.GetValue(context, key => DeriveOptions(options, key));
    }

    private static JsonSerializerOptions DeriveOptions(JsonSerializerOptions options, JsonSerializerContext context)
    {
        // A copy, so the caller's options are neither locked by use nor changed.
        var derived = new JsonSerializerOptions(options);
        var hadResolver = derived.TypeInfoResolver is not null;

        // A context that is the options' own resolver is already the only thing consulted, and cannot be put in front of itself.
        // One in their chain is moved to the front of the copy's, so that it is consulted before a reflection resolver ahead of it.
        if (!ReferenceEquals(derived.TypeInfoResolver, context))
        {
            derived.TypeInfoResolverChain.Remove(context);
            derived.TypeInfoResolverChain.Insert(0, context);
        }

        if (!hadResolver)
        {
            // Options that had no resolver read through reflection; the context ahead of it must not take that away from the
            // types it does not cover.
            derived.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        }

        // Locked, as the options a call uses are: options still open to change resolve their metadata afresh on each ask, and
        // these are shared.
        derived.MakeReadOnly();

        return derived;
    }

    /// <summary>
    /// Sends a forward of <paramref name="inner"/> through its own request builders, authenticated with
    /// <paramref name="credential"/>, and reads the outcome with the forward readers.
    /// </summary>
    /// <typeparam name="TInner">The inner operation's type, exactly.</typeparam>
    /// <typeparam name="TResult">The type the response is read as.</typeparam>
    /// <param name="inner">The inner operation to send.</param>
    /// <param name="options">Optional per-call request customisation.</param>
    /// <param name="credential">The credential to apply last, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The upstream outcome.</returns>
    private Task<TResult> ForwardCoreAsync<TInner, TResult>(TInner inner, CallOptions? options, IUpstreamCredential? credential, CancellationToken cancellationToken)
        where TInner : class, IOperation, IHttpRequestBuilder<TInner>
    {
        ArgumentNullException.ThrowIfNull(inner);

        if (inner.GetType() != typeof(TInner))
        {
            throw new ArgumentException(
                $"A forward sends a fresh {typeof(TInner).Name} and nothing else, but was given a {inner.GetType().Name}, which would be sent with every property of its own. Construct a new {typeof(TInner).Name} from it.",
                nameof(inner));
        }

        return this.SendAsync(
            TInner.GetHttpMethod(),
            TInner.BuildRequestUrl(inner),
            inner,
            operationTypeInfo: null,
            ForwardResponseReaders.For<TResult>(),
            typeof(TInner).Name,
            options,
            AcceptProblemJson,
            credential,
            cancellationToken);
    }

    private HttpRequestMessage CreateRequest<TOperation>(HttpMethod method, string? url, TOperation operation, JsonTypeInfo<TOperation>? operationTypeInfo)
        where TOperation : class, IOperation
    {
        if (url is null)
        {
            throw new InvalidOperationException(
                $"Compiled URL builder returned null for '{operation.GetType().FullName}'. " +
                $"A required route parameter is likely null.");
        }

        // Strip the leading '/' so the URL is treated as relative to the HttpClient's BaseAddress.
        // Without this, HttpClient treats "/templates" as an absolute path from the host root,
        // discarding the BaseAddress path component (e.g. "http://host/api/" → "http://host/templates").
        if (url.Length > 0 && url[0] == '/')
        {
            url = url[1..];
        }

        var request = new HttpRequestMessage(method, url);

        if (method != HttpMethod.Get)
        {
            // The declared type's metadata writes none of a derived type's own members, so an instance of one is written as its
            // run-time type, as an operation without metadata is.
            request.Content = operationTypeInfo is not null && operation.GetType() == typeof(TOperation)
                ? JsonContent.Create(operation, operationTypeInfo)
                : JsonContent.Create(operation, operation.GetType(), options: this._jsonOptions);
        }

        return request;
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
                         && ReadValidationErrors(errorsElement) is { } readErrors
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
    /// Reads the <c>errors</c> member of a 422 as <c>Deserialize&lt;Dictionary&lt;string, string[]&gt;&gt;</c> read it, by hand:
    /// the transport's own types must not depend on the consumer's resolver, which with reflection disabled supplies no
    /// metadata for them. Keys are kept exactly as sent (a later duplicate replaces an earlier), a value is a JSON array of
    /// strings or <c>null</c>, and a <c>null</c> element is a <see langword="null"/> string, as the serializer read them.
    /// </summary>
    /// <param name="errors">The <c>errors</c> member's value.</param>
    /// <returns>The errors, or <see langword="null"/> for a JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">
    /// The value is not an object, or a member of it is not an array of strings, as the serializer threw for one; the caller
    /// treats it as an unreadable body.
    /// </exception>
    private static Dictionary<string, string[]>? ReadValidationErrors(JsonElement errors)
    {
        if (errors.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (errors.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"The validation errors are a JSON {errors.ValueKind}, not an object.");
        }

        var result = new Dictionary<string, string[]>();

        foreach (var member in errors.EnumerateObject())
        {
            if (member.Value.ValueKind == JsonValueKind.Null)
            {
                result[member.Name] = null!;

                continue;
            }

            if (member.Value.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException($"The validation errors for '{member.Name}' are a JSON {member.Value.ValueKind}, not an array of strings.");
            }

            var messages = new string[member.Value.GetArrayLength()];
            var index = 0;

            foreach (var message in member.Value.EnumerateArray())
            {
                messages[index++] = message.ValueKind switch
                {
                    JsonValueKind.String => message.GetString()!,
                    JsonValueKind.Null => null!,
                    _ => throw new JsonException($"A validation error for '{member.Name}' is a JSON {message.ValueKind}, not a string."),
                };
            }

            result[member.Name] = messages;
        }

        return result;
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
