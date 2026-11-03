using Blueprint.Runtime.Exec;

namespace Blueprint.Runtime.Http;

/// <summary>
/// An app's own writer of HTTP responses, consulted with every result an operation's HTTP request ends with before
/// Blueprint writes anything, so an app can answer some results in a shape of its own — a legacy error envelope, a CSV
/// rendering of a successful result — and leave the rest to Blueprint. Registered with
/// <see cref="BlueprintServiceBuilder.AddHttpResultWriter{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The writer sees each result the HTTP host writes: an <see cref="OkResult"/> (and <see cref="OkResult{T}"/>), a
/// <see cref="ValidationFailedResult"/> (422), an <see cref="UnauthenticatedResult"/> (401), a <see cref="ForbiddenResult"/>
/// (403), a <see cref="NotFoundResult"/>, a <see cref="BindingFailureResult"/> (400), an
/// <see cref="UnhandledExceptionResult"/> and every other result. An exception that escapes the operation's pipeline or
/// Blueprint's own write of a result is given to it as an <see cref="UnhandledExceptionResult"/> too, while the response
/// has not started, so <see cref="UnhandledExceptionResult.Exception"/> is the original exception on every path. On that
/// path the response may carry the status and headers the failed write set, though not its <c>Content-Length</c>, which
/// is removed; a writer sets what its own response needs.
/// </para>
/// <para>
/// Returning <see langword="false"/> declines: Blueprint then writes the result exactly as it would with no writer
/// registered. A writer that declines must not have changed the response. One that returns <see langword="true"/> has
/// written the whole response, status and content type included, and Blueprint writes nothing more.
/// </para>
/// <para>
/// There is one writer per app; the last registered wins. A writer that wants to delegate by result type composes those
/// decisions itself. It is a singleton, consulted concurrently; request-scoped services come from
/// <see cref="HttpContext.RequestServices"/>.
/// </para>
/// <para>
/// An exception the writer throws is handled as an exception Blueprint's own write threw: the writer is consulted again
/// with it as an <see cref="UnhandledExceptionResult"/>, and an exception thrown from that call propagates to ASP.NET Core.
/// </para>
/// </remarks>
public interface IHttpResultWriter
{
    /// <summary>
    /// Writes <paramref name="result"/> to the response, or declines to.
    /// </summary>
    /// <param name="httpContext">The current HTTP context, whose response has not been written.</param>
    /// <param name="operationContext">The operation's context: its descriptor, app, services and activity.</param>
    /// <param name="result">The result the operation ended with.</param>
    /// <returns>
    /// <see langword="true"/> when the writer has written the response; <see langword="false"/> to have Blueprint write it.
    /// </returns>
    ValueTask<bool> TryWriteAsync(HttpContext httpContext, ApiOperationContext operationContext, OperationResult result);
}
