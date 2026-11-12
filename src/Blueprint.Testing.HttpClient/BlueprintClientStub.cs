using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Http;
using Blueprint.HttpClient;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;

namespace Blueprint.Testing;

/// <summary>
/// A test double of the upstream a <c>[BlueprintClient]</c> calls, answered and recorded by operation type, so that a test
/// of a typed-client call or of an <c>[Expose&lt;TInner&gt;]</c> gateway's forward never restates the upstream's routes.
/// Register it with <see cref="BlueprintClientStubServiceCollectionExtensions.StubBlueprintClient{TClient}"/>.
/// </summary>
/// <remarks>
/// <para>
/// It stands in for the named client's HTTP handler, not the client: every request is still built by the operation's own
/// generated request builder, and every response is still read by the client's own readers. A request is matched by the
/// operation <see cref="BlueprintHttpClient"/> tags it with (<see cref="BlueprintRequestOptions.Operation"/>), by its exact
/// type.
/// </para>
/// <para>
/// A response is written as a Blueprint host writes the same result: a value as a 200 JSON body; an
/// <see cref="OkResult"/>, <see cref="CreatedResult"/>, <see cref="AcceptedResult"/>, <see cref="ConflictResult"/> or
/// <see cref="NotFoundResult"/> as its content with its status, so that a declared <see cref="NotFoundResult{T}"/> is read as
/// that member; a <see cref="PlainTextResult"/> as its text with its own content type; a <see cref="StatusCodeResult"/> or
/// <see cref="NotModifiedResult{T}"/> as its status; a <see cref="NoResultOperationResult"/> as an empty 200; a legacy
/// <see cref="ResourceEvent"/> as the event, a 200 whatever its change type; a <see cref="ResourceEventResult"/> as the
/// host's event envelope with its change type's status, a deleted one as an empty 204; a <see cref="KeyOnlyResourceEventResult"/>
/// as its change type's status alone; a <see cref="ValidationFailedResult"/>, or a thrown
/// <see cref="Validation.ValidationException"/>, as a 422 the client raises as a <see cref="Validation.ValidationException"/>;
/// and a thrown <see cref="ApiException"/> as its problem document, which the client raises as a
/// <see cref="BlueprintApiException"/>. <see langword="null"/> is a 204.
/// </para>
/// <para>
/// A request for an operation given no response fails the call with an <see cref="InvalidOperationException"/> naming it,
/// and is still recorded. Responses and records may be added and read from any thread.
/// </para>
/// </remarks>
/// <typeparam name="TClient">The generated typed client class.</typeparam>
public sealed class BlueprintClientStub<TClient>
    where TClient : class, IBlueprintClient<TClient>
{
    private const string ValidationTitle = "One or more validation errors occurred.";

    private readonly ConcurrentDictionary<Type, Func<IOperation, object?>> _responders = new();
    private readonly List<BlueprintClientStubRequest> _requests = [];

    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintClientStub{TClient}"/> class, as <see cref="BlueprintClientStubServiceCollectionExtensions.StubBlueprintClient{TClient}"/> does.
    /// </summary>
    internal BlueprintClientStub()
    {
    }

    /// <summary>
    /// Gets the base address the named client is given when nothing else gives it one.
    /// </summary>
    public static Uri DefaultBaseAddress { get; } = new($"http://{typeof(TClient).Name.ToLowerInvariant()}.stub.invalid/");

    /// <summary>
    /// Gets every request received so far, in the order they were received.
    /// </summary>
    public IReadOnlyList<BlueprintClientStubRequest> Requests
    {
        get
        {
            lock (this._requests)
            {
                return this._requests.ToArray();
            }
        }
    }

    /// <summary>
    /// Answers every request for <typeparamref name="TOperation"/> with what <paramref name="respond"/> returns or throws
    /// for the operation sent, replacing any response given before. See <see cref="BlueprintClientStub{TClient}"/> for how
    /// each kind of value is written.
    /// </summary>
    /// <typeparam name="TOperation">The operation type, exactly as the client sends it.</typeparam>
    /// <param name="respond">Makes the response from the operation that was sent.</param>
    /// <returns>This stub.</returns>
    public BlueprintClientStub<TClient> Respond<TOperation>(Func<TOperation, object?> respond)
        where TOperation : class, IOperation
    {
        ArgumentNullException.ThrowIfNull(respond);

        this._responders[typeof(TOperation)] = operation => respond((TOperation)operation);

        return this;
    }

    /// <summary>
    /// Gets each <typeparamref name="TOperation"/> sent so far, in order, as the caller built it.
    /// </summary>
    /// <typeparam name="TOperation">The operation type.</typeparam>
    /// <returns>The operations.</returns>
    public IReadOnlyList<TOperation> Received<TOperation>()
        where TOperation : class, IOperation
    {
        return this.Requests.Select(static r => r.Operation).OfType<TOperation>().ToArray();
    }

    /// <summary>
    /// Makes the primary handler of one handler chain of the named client.
    /// </summary>
    /// <param name="services">The container the chain is built from, which the client's JSON options are read from.</param>
    /// <returns>The handler.</returns>
    internal HttpMessageHandler CreateHandler(IServiceProvider services)
    {
        return new StubHandler(this, services);
    }

    private static HttpResponseMessage Write(object? value, JsonSerializerOptions jsonOptions)
    {
        return value switch
        {
            null => new HttpResponseMessage(HttpStatusCode.NoContent),

            // The host writes nothing for it, leaving the default 200, and the client reads it from a 200 alone.
            NoResultOperationResult => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) },
            INotModifiedResult notModified => WithETag(new HttpResponseMessage(HttpStatusCode.NotModified), notModified.ETag),
            OkResult ok => WithHeaders(Json(HttpStatusCode.OK, ok.Content, jsonOptions), ok),
            CreatedResult created => Json(HttpStatusCode.Created, created.Content, jsonOptions),
            AcceptedResult accepted => Json(HttpStatusCode.Accepted, accepted.Content, jsonOptions),
            ConflictResult conflict => Json(HttpStatusCode.Conflict, conflict.Content, jsonOptions),
            NotFoundResult notFound => Json(HttpStatusCode.NotFound, notFound.Content, jsonOptions),
            PlainTextResult text => PlainText(text),
            StatusCodeResult status => new HttpResponseMessage((HttpStatusCode)status.StatusCode),
            ValidationFailedResult failed => ValidationProblem(failed, jsonOptions),
            KeyOnlyResourceEventResult keyOnly => new HttpResponseMessage(ChangeTypeStatus(keyOnly.ChangeType)),
            ResourceEventResult resourceEventResult => ResourceEventEnvelope(resourceEventResult, jsonOptions),

            // Whatever its change type, the host writes a legacy resource event as a 200 with the event itself, as legacy did.
            ResourceEvent resourceEvent => Json(HttpStatusCode.OK, resourceEvent, SerializerContextTypeInfoCache.GetFallbackOptions(jsonOptions)),
            OperationResult other => throw new NotSupportedException(
                $"{typeof(TClient).Name}'s stub cannot answer with a {other.GetType().Name}. Answer with a value, or with an OkResult, CreatedResult, AcceptedResult, ConflictResult, NotFoundResult, StatusCodeResult, PlainTextResult, ValidationFailedResult, NoResultOperationResult, ResourceEvent, ResourceEventResult or KeyOnlyResourceEventResult."),
            _ => Json(HttpStatusCode.OK, value, jsonOptions),
        };
    }

    /// <summary>
    /// Gets the status the host writes a resource event result of <paramref name="changeType"/> with.
    /// </summary>
    private static HttpStatusCode ChangeTypeStatus(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Created => HttpStatusCode.Created,
            ChangeType.Deleted => HttpStatusCode.NoContent,
            _ => HttpStatusCode.OK,
        };
    }

    /// <summary>
    /// Writes a <see cref="ResourceEventResult"/> as the host's event envelope at its change type's status, a created one
    /// with its content's self link as the <c>Location</c>, and a deleted one as an empty 204, as the host writes it.
    /// </summary>
    /// <remarks>
    /// The envelope has no <c>href</c>: the host fills one from its app's registered links, which a stub of the upstream
    /// has none of, and the client reads none back into the result. The content and the metadata bags are written with the
    /// client's options and the reflection fallback the host gives a type its serializer context does not cover.
    /// </remarks>
    private static HttpResponseMessage ResourceEventEnvelope(ResourceEventResult result, JsonSerializerOptions jsonOptions)
    {
        var status = ChangeTypeStatus(result.ChangeType);

        if (status == HttpStatusCode.NoContent)
        {
            return new HttpResponseMessage(status);
        }

        var fallbackOptions = SerializerContextTypeInfoCache.GetFallbackOptions(jsonOptions);
        var contentType = result.Content.GetType();
        var envelope = new JsonObject
        {
            ["$object"] = "event",
            ["eventId"] = result.EventId,
            ["changeType"] = result.ChangeType.ToString(),
            ["created"] = DateTimeOffset.UtcNow,
            ["resourceObject"] = ApiResource.GetTypeName(contentType),
            ["data"] = JsonSerializer.SerializeToNode(result.Content, contentType, fallbackOptions),
        };

        AddBag(envelope, "metadata", result.Metadata, fallbackOptions);
        AddBag(envelope, "secureData", result.SecureData, fallbackOptions);
        AddBag(envelope, "changedValues", result.ChangedValues, fallbackOptions);

        if (result.CorrelationId is not null)
        {
            envelope["correlationId"] = result.CorrelationId;
        }

        var body = new StringContent(envelope.ToJsonString(), Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        var response = new HttpResponseMessage(status) { Content = body };

        if (status == HttpStatusCode.Created && result.Content is ILinkableResource { Links: { } links } && links.TryGetValue("self", out var self))
        {
            response.Headers.TryAddWithoutValidation("Location", self.Href);
        }

        return response;

        static void AddBag(JsonObject envelope, string name, Dictionary<string, object>? bag, JsonSerializerOptions options)
        {
            if (bag is not null)
            {
                envelope[name] = JsonSerializer.SerializeToNode(bag, options);
            }
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object content, JsonSerializerOptions jsonOptions)
    {
        // Serialised asynchronously, as the host writes a body, so a body that is or holds an IAsyncEnumerable<T> is written.
        return new HttpResponseMessage(status) { Content = JsonContent.Create(content, content.GetType(), MediaTypeHeaderValue.Parse("application/json; charset=utf-8"), jsonOptions) };
    }

    private static HttpResponseMessage WithHeaders(HttpResponseMessage response, OkResult ok)
    {
        if (ok.HasHeaders)
        {
            foreach (var (name, values) in ok.Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);
                }
            }
        }

        return response;
    }

    private static HttpResponseMessage WithETag(HttpResponseMessage response, string eTag)
    {
        response.Headers.TryAddWithoutValidation("ETag", eTag);

        return response;
    }

    /// <summary>
    /// Writes a plain-text result with its own content type, verbatim and with no charset appended, as the host does.
    /// </summary>
    private static HttpResponseMessage PlainText(PlainTextResult text)
    {
        // A status that forbids a body is written as the status alone, with no Content-Type (TryGetBodilessStatusCode).
        if (text.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent or HttpStatusCode.NotModified)
        {
            return new HttpResponseMessage(text.StatusCode);
        }

        var body = new ByteArrayContent(Encoding.UTF8.GetBytes(text.Content));
        body.Headers.TryAddWithoutValidation("Content-Type", text.ContentType);

        return new HttpResponseMessage(text.StatusCode) { Content = body };
    }

    /// <summary>
    /// Writes an <see cref="ApiException"/> as the host does: its members and extensions named by the host's own
    /// <see cref="HttpOperationResultExecutor.CreateLegacyExceptionProblem"/>, and the extensions, whose types are open-ended,
    /// written with the reflection fallback the host gives them, so a strict serializer context does not fail them.
    /// </summary>
    private static HttpResponseMessage Problem(ApiException exception, JsonSerializerOptions jsonOptions)
    {
        var details = HttpOperationResultExecutor.CreateLegacyExceptionProblem(exception, exposeDetails: false, activity: null, jsonOptions);
        var problem = new JsonObject
        {
            ["type"] = details.Type,
            ["title"] = details.Title,
            ["status"] = details.Status,
            ["detail"] = details.Detail,
        };

        var extensionOptions = SerializerContextTypeInfoCache.GetFallbackOptions(jsonOptions);

        foreach (var (name, value) in details.Extensions)
        {
            problem[name] = value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), extensionOptions);
        }

        return ProblemResponse((HttpStatusCode)exception.HttpStatus, problem);
    }

    /// <summary>
    /// Writes a validation failure as the host's 422, its keys named by the host's own
    /// <see cref="HttpOperationResultExecutor.CreateValidationErrors"/>.
    /// </summary>
    private static HttpResponseMessage ValidationProblem(ValidationFailedResult failed, JsonSerializerOptions jsonOptions)
    {
        var errors = new JsonObject();

        foreach (var (key, messages) in HttpOperationResultExecutor.CreateValidationErrors(jsonOptions, failed))
        {
            errors[key] = new JsonArray(messages.Select(static m => (JsonNode?)m).ToArray());
        }

        var problem = new JsonObject
        {
            ["title"] = ValidationTitle,
            ["status"] = (int)HttpStatusCode.UnprocessableEntity,
            ["errors"] = errors,
        };

        return ProblemResponse(HttpStatusCode.UnprocessableEntity, problem);
    }

    private static HttpResponseMessage ProblemResponse(HttpStatusCode status, JsonObject problem)
    {
        var body = new StringContent(problem.ToJsonString(), Encoding.UTF8);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json") { CharSet = "utf-8" };

        return new HttpResponseMessage(status) { Content = body };
    }

    /// <summary>
    /// Snapshots a request's headers and its content's, as the upstream receives both.
    /// </summary>
    private static Dictionary<string, string[]> SnapshotHeaders(HttpRequestMessage request)
    {
        var headers = request.Headers.ToDictionary(static h => h.Key, static h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

        if (request.Content is not null)
        {
            foreach (var (name, values) in request.Content.Headers)
            {
                headers[name] = values.ToArray();
            }
        }

        return headers;
    }

    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, StubHandler handler, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(BlueprintRequestOptions.Operation, out var operation))
        {
            throw new InvalidOperationException(
                $"{typeof(TClient).Name}'s stub was sent {request.Method} {request.RequestUri}, which no Blueprint operation built. Only a typed-client call or a gateway's forward can be stubbed by operation type.");
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        request.Options.TryGetValue(BlueprintRequestOptions.RequestAuthorization, out var requestAuthorization);

        lock (this._requests)
        {
            this._requests.Add(new BlueprintClientStubRequest(
                operation,
                request.Method,
                request.RequestUri,
                SnapshotHeaders(request),
                request.Headers.Authorization,
                requestAuthorization,
                body));
        }

        var operationType = operation.GetType();

        if (!this._responders.TryGetValue(operationType, out var respond))
        {
            throw new InvalidOperationException(
                $"{typeof(TClient).Name}'s stub has no response for {operationType.Name}. Give it one with Respond<{operationType.Name}>(…) before the call.");
        }

        var jsonOptions = handler.JsonOptions;

        try
        {
            return Write(respond(operation), jsonOptions);
        }
        catch (Validation.ValidationException e)
        {
            return ValidationProblem(new ValidationFailedResult(e), jsonOptions);
        }
        catch (ApiException e)
        {
            return Problem(e, jsonOptions);
        }
    }

    private sealed class StubHandler(BlueprintClientStub<TClient> stub, IServiceProvider services) : HttpMessageHandler
    {
        private JsonSerializerOptions? _jsonOptions;

        /// <summary>
        /// Gets the options the client reads with, the serializer context it declares included, so that a response is written
        /// as the client will read it. Read on first use, not when the chain is built, as the transport is made from the
        /// named client whose chain this is.
        /// </summary>
        public JsonSerializerOptions JsonOptions => this._jsonOptions ??= BlueprintClientTransport.Create<TClient>(services).JsonOptions;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return stub.HandleAsync(request, this, cancellationToken);
        }
    }
}
