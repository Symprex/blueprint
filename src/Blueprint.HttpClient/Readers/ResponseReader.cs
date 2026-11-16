namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a successful response of a Blueprint call as <typeparamref name="T"/>. The transport
/// (<see cref="BlueprintHttpClient.SendAsync{TOperation, TResult}"/>) maps every failure before a reader sees a response,
/// so a reader only ever reads a 2xx.
/// </summary>
/// <remarks>
/// <para>
/// Each reader reads one shape of declared result and is constructed with whatever it needs to make that result, so a read
/// looks nothing up: a generated typed client constructs the reader its operation's declared result needs and passes it to
/// every call. The declared result decides the reader, the first that applies:
/// </para>
/// <list type="number">
/// <item><description><see cref="Blueprint.Http.ResourceEvent{TResource}"/> and its generic subclasses:
/// <see cref="ResourceEventResponseReader{TEvent, TResource}"/>.</description></item>
/// <item><description><see cref="Blueprint.Runtime.Exec.ResourceEventResult{T}"/>:
/// <see cref="ResourceEventResultResponseReader{T}"/>.</description></item>
/// <item><description>A <see cref="Blueprint.Runtime.Exec.StatusCodeResult"/> nested type such as
/// <see cref="Blueprint.Runtime.Exec.StatusCodeResult.Accepted"/>: <see cref="StatusCodeResultResponseReader{T}"/>.</description></item>
/// <item><description><see cref="Blueprint.Runtime.Exec.PlainTextResult"/>: <see cref="PlainTextResponseReader"/>.</description></item>
/// <item><description><see cref="Blueprint.Model.Results{T1, T2}"/>: <see cref="UnionResponseReader{T1, T2}"/>, composed of
/// the readers its members are given by this same list.</description></item>
/// <item><description><see cref="Blueprint.Runtime.Exec.StatusCodeResult"/> itself, <see cref="Blueprint.Http.HttpResult"/>,
/// <see cref="Blueprint.OperationResult"/> or <see cref="object"/>: <see cref="BodilessResultResponseReader{T}"/>.</description></item>
/// <item><description><see cref="Blueprint.Runtime.Exec.OkResult{T}"/>, <see cref="Blueprint.Runtime.Exec.CreatedResult{T}"/>
/// or <see cref="Blueprint.Runtime.Exec.AcceptedResult{T}"/>: <see cref="ContentResultResponseReader{TResult, TContent}"/>,
/// which reads one alone exactly as <see cref="JsonResponseReader{T}"/> does and is needed only for a union's member.</description></item>
/// <item><description>Anything else, <see cref="string"/> included: <see cref="JsonResponseReader{T}"/>.</description></item>
/// </list>
/// <para>
/// <see cref="BlueprintHttpClient{TApp}"/> makes the same choice at run time, once per response type, so a typed client
/// and the app-driven client read every response identically. Each reader reproduces, for the types it is chosen for, the
/// reading that client did with one chain of type tests before these readers existed.
/// </para>
/// <para>
/// A reader holds nothing a read changes, so one instance can be shared by every call on every thread. The set is closed:
/// only this assembly defines readers, because the transport and <see cref="UnionResponseReader{T1, T2}"/> rely on how each
/// one reads.
/// </para>
/// </remarks>
/// <typeparam name="T">The type a response is read as.</typeparam>
public abstract class ResponseReader<T>
{
    /// <summary>
    /// Initialises a new instance of the <see cref="ResponseReader{T}"/> class. Only this assembly's readers derive from it.
    /// </summary>
    private protected ResponseReader()
    {
    }

    /// <summary>
    /// Gets the type name of the resource this reader's events carry (<see cref="Blueprint.Model.ApiResource.GetTypeName"/>), as
    /// the host writes it as a resource event's <c>resourceObject</c>, or <see langword="null"/> when it reads no event of a
    /// resource. A union whose two members could each be a JSON body tells them apart by it.
    /// </summary>
    internal virtual string? EventResourceObject => null;

    /// <summary>
    /// Gets a value indicating whether what this reader reads owns the response, so that the transport hands it over at its
    /// headers (<see cref="HttpCompletionOption.ResponseHeadersRead"/>) and does not dispose it after a successful read. Only
    /// a forward's <see cref="Blueprint.Http.ProxyResult"/> reader does, to stream the body on unbuffered.
    /// </summary>
    internal virtual bool TakesResponse => false;

    /// <summary>
    /// Reads the response as the declared result.
    /// </summary>
    /// <param name="context">The response and what it is read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result.</returns>
    internal abstract ValueTask<T> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Whether, as a member of a <see cref="Blueprint.Model.Results{T1, T2}"/>, this reader's type is what the host writes as a
    /// JSON body of <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns><see langword="true"/> when the host writes this type as a JSON body of that status.</returns>
    internal virtual bool IsJsonBodyOfStatus(int status)
    {
        return false;
    }

    /// <summary>
    /// Reads a JSON body as this reader's type, as a member of a <see cref="Blueprint.Model.Results{T1, T2}"/>; called only
    /// when <see cref="IsJsonBodyOfStatus"/> said the body is one.
    /// </summary>
    /// <param name="context">The response and what it is read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a member was read, and the member; a JSON read that is <see langword="null"/> is no member.</returns>
    internal virtual ValueTask<(bool Read, T Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException($"{typeof(T)} is not a member the host writes as a JSON body.");
    }

    /// <summary>
    /// Reads a response that is not JSON as this reader's type, as a member of a <see cref="Blueprint.Model.Results{T1, T2}"/>:
    /// a response with no media type when the host writes this type with no body at the response's status, or a text body
    /// when this reader reads text.
    /// </summary>
    /// <param name="context">The response and what it is read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this member claims the response, and the member.</returns>
    internal virtual ValueTask<(bool Read, T Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return default;
    }

    /// <summary>
    /// Reads a response that is not a success as this reader's type, when the declared result has a member the host writes
    /// at that status; called by the transport before it maps the failure, and only a reader made for a forward
    /// (<see cref="ForwardResponseReaders"/>) claims one. A reader that does not claim the response must not read its body.
    /// </summary>
    /// <param name="context">The failure response and what it is read with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this reader claims the response, and what it read; otherwise the transport maps the failure as always.</returns>
    internal virtual ValueTask<(bool Read, T Value)> ReadFailureAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return default;
    }
}
