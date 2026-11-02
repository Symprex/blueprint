using System.Net;
using System.Net.Http.Json;
using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as an <see cref="OkResult{T}"/>, <see cref="CreatedResult{T}"/> or
/// <see cref="AcceptedResult{T}"/>, which the host writes as its content with its own status (200, 201 or 202).
/// </summary>
/// <remarks>
/// As a member of a <see cref="Blueprint.Model.Results{T1, T2}"/>, a JSON body of the result's status is its content,
/// wrapped in the result, which is what this reader is for: the sample's <c>GetTemplateQuery</c> returns such a union. Read
/// alone, it reads the response as <see cref="JsonResponseReader{T}"/> of <typeparamref name="TResult"/> does, as the client
/// read one before its readers existed.
/// </remarks>
/// <typeparam name="TResult"><see cref="OkResult{T}"/>, <see cref="CreatedResult{T}"/> or <see cref="AcceptedResult{T}"/> of <typeparamref name="TContent"/>.</typeparam>
/// <typeparam name="TContent">The type of the result's content.</typeparam>
public sealed class ContentResultResponseReader<TResult, TContent> : ResponseReader<TResult>
    where TResult : OperationResult
    where TContent : notnull
{
    private readonly int _status;
    private readonly Func<TContent, TResult> _create;

    /// <summary>
    /// Initialises a new instance of the <see cref="ContentResultResponseReader{TResult, TContent}"/> class.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TResult"/> is not <see cref="OkResult{T}"/>, <see cref="CreatedResult{T}"/> or
    /// <see cref="AcceptedResult{T}"/> of <typeparamref name="TContent"/>.
    /// </exception>
    public ContentResultResponseReader()
    {
        if (typeof(TResult) == typeof(OkResult<TContent>))
        {
            this._status = (int)HttpStatusCode.OK;
            this._create = static content => (TResult)(object)new OkResult<TContent>(content);
        }
        else if (typeof(TResult) == typeof(CreatedResult<TContent>))
        {
            this._status = (int)HttpStatusCode.Created;
            this._create = static content => (TResult)(object)new CreatedResult<TContent>(content);
        }
        else if (typeof(TResult) == typeof(AcceptedResult<TContent>))
        {
            this._status = (int)HttpStatusCode.Accepted;
            this._create = static content => (TResult)(object)new AcceptedResult<TContent>(content);
        }
        else
        {
            throw new ArgumentException(
                $"A content result reader reads an OkResult, CreatedResult or AcceptedResult of {typeof(TContent)}, not a {typeof(TResult)}.");
        }
    }

    /// <inheritdoc />
    internal override async ValueTask<TResult> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        if (context.Status == (int)HttpStatusCode.NoContent)
        {
            throw context.CreateNotUnderstoodException();
        }

        var value = await context.Response.Content.ReadFromJsonAsync<TResult>(context.JsonOptions, cancellationToken).ConfigureAwait(false);

        return value ?? throw await context.CreateResponseEmptyExceptionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    internal override bool IsJsonBodyOfStatus(int status)
    {
        return status == this._status;
    }

    /// <inheritdoc />
    internal override async ValueTask<(bool Read, TResult Value)> ReadJsonMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        var content = await context.Response.Content.ReadFromJsonAsync<TContent>(context.JsonOptions, cancellationToken).ConfigureAwait(false);

        return content is null ? default : (true, this._create(content));
    }
}
