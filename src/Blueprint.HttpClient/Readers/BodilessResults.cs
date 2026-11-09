using System.Net;
using Blueprint.Http;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Runtime.Http;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// The results the host writes with no body, which a reader makes from a response's status alone.
/// </summary>
internal static class BodilessResults
{
    /// <summary>
    /// Every nested singleton <see cref="StatusCodeResult"/> declares, listed rather than discovered so that reading one
    /// looks nothing up. <c>BlueprintHttpClientTransportTests</c> pins this against the nested types reflection finds, so a
    /// singleton added to <see cref="StatusCodeResult"/> without being added here fails a test instead of reading as nothing.
    /// </summary>
    private static readonly StatusCodeResult[] _statusCodeResults =
    [
        StatusCodeResult.OK.Instance,
        StatusCodeResult.Created.Instance,
        StatusCodeResult.Accepted.Instance,
        StatusCodeResult.NoContent.Instance,
        StatusCodeResult.BadRequest.Instance,
        StatusCodeResult.NotImplemented.Instance,
        StatusCodeResult.ServiceUnavailable.Instance,
    ];

    /// <summary>
    /// Finds the nested <see cref="StatusCodeResult"/> singleton of <paramref name="status"/>, as legacy's client made a
    /// <c>StatusCodeResult</c> of any status (Signature365 <c>8b40ecc961</c>, <c>Common.Core/Api/BlueprintHttpClient.cs:223-226</c>).
    /// </summary>
    /// <param name="status">The response's status.</param>
    /// <returns>The singleton, or <see langword="null"/> for a status no nested result has (for example 205).</returns>
    /// <remarks>
    /// Legacy's <c>StatusCodeResult</c> could hold any status; the nested singletons here cannot. That is a pre-existing
    /// departure, not one this fixes.
    /// </remarks>
    internal static StatusCodeResult? FindStatusCodeResult(int status)
    {
        foreach (var result in _statusCodeResults)
        {
            if (result.StatusCode == status)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds how the host's bodiless write of <paramref name="resultType"/> is read back: an <see cref="HttpNoContentResult"/>
    /// as a 204, a <see cref="NoResultOperationResult"/> as a 200, an <see cref="HttpAcceptedResult"/> as a 202, and a
    /// <see cref="KeyOnlyResourceEventResult"/> as the 201, 200 or 204 of its change type.
    /// </summary>
    /// <param name="resultType">The declared result type.</param>
    /// <returns>
    /// A function making the result of a status, which is <see langword="null"/> for a status the host never writes it
    /// with; or <see langword="null"/> when the host writes no <paramref name="resultType"/> without a body.
    /// </returns>
    internal static Func<int, object?>? ForSemanticResult(Type resultType)
    {
        if (resultType == typeof(HttpNoContentResult))
        {
            return static status => status == (int)HttpStatusCode.NoContent ? new HttpNoContentResult() : null;
        }

        if (resultType == typeof(NoResultOperationResult))
        {
            return static status => status == (int)HttpStatusCode.OK ? NoResultOperationResult.Instance : null;
        }

        if (resultType == typeof(HttpAcceptedResult))
        {
            return static status => status == (int)HttpStatusCode.Accepted ? new HttpAcceptedResult() : null;
        }

        // The host writes a key-only event's change type as its status, and nothing else of it.
        if (resultType == typeof(KeyOnlyResourceEventResult))
        {
            return static status => TryGetKeyOnlyChangeType(status, out var changeType) ? new KeyOnlyResourceEventResult(changeType) : null;
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
    /// A client-materialised <see cref="HttpResult"/> carrying only a status, for a bodiless, media-type-less response
    /// when the declared result is the base <see cref="HttpResult"/> and could not, unlike <see cref="OperationResult"/>
    /// or <see cref="object"/>, also be answered by a <see cref="StatusCodeResult"/> singleton (they are siblings here,
    /// where legacy's <c>StatusCodeResult</c> derived from <c>HttpResult</c> and so could answer this shape itself). Its
    /// <see cref="HttpResult.ExecuteAsync"/> (the base implementation — this type adds nothing) writes exactly the status
    /// and no body, so a gateway handler that forwards this value unchanged answers its own caller with the same bodiless
    /// response.
    /// </summary>
    internal sealed class BodilessHttpResult : HttpResult
    {
        /// <summary>
        /// Initialises a new instance of the <see cref="BodilessHttpResult"/> class.
        /// </summary>
        /// <param name="statusCode">The response's status.</param>
        internal BodilessHttpResult(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }
    }
}
