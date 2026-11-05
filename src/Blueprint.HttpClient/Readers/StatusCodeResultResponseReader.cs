using Blueprint.Runtime.Exec;

namespace Blueprint.HttpClient.Readers;

/// <summary>
/// Reads a response declared as a nested <see cref="StatusCodeResult"/> type, for example
/// <see cref="StatusCodeResult.Accepted"/>, which the host writes as its status and no body: a response of that status is
/// the singleton, and any other success is <see langword="null"/>, for the caller to decide whether that is an error.
/// </summary>
/// <remarks>
/// This is the pattern Signature365's gateways use through <c>SendAsync&lt;StatusCodeResult.Accepted&gt;(...)</c>. The
/// body, if any, is never read. In a union, only a response with no media type is this member.
/// </remarks>
/// <typeparam name="T">The nested <see cref="StatusCodeResult"/> type.</typeparam>
public sealed class StatusCodeResultResponseReader<T> : ResponseReader<T>
    where T : StatusCodeResult
{
    private readonly StatusCodeResult? _instance;

    /// <summary>
    /// Initialises a new instance of the <see cref="StatusCodeResultResponseReader{T}"/> class.
    /// </summary>
    /// <param name="instance">The type's singleton, for example <see cref="StatusCodeResult.Accepted.Instance"/>.</param>
    public StatusCodeResultResponseReader(T instance)
        : this((StatusCodeResult)(instance ?? throw new ArgumentNullException(nameof(instance))))
    {
    }

    private StatusCodeResultResponseReader(StatusCodeResult? instance)
    {
        this._instance = instance;
    }

    /// <summary>
    /// Creates the reader <see cref="BlueprintHttpClient{TApp}"/> reads <typeparamref name="T"/> with, from whatever its
    /// public static <c>Instance</c> field holds, as that client found the singleton before its readers existed: a type with
    /// no such singleton reads every response as <see langword="null"/>.
    /// </summary>
    /// <param name="singleton">The value of <typeparamref name="T"/>'s <c>Instance</c> field, if it has one.</param>
    /// <returns>The reader.</returns>
    internal static StatusCodeResultResponseReader<T> FromSingletonField(StatusCodeResult? singleton)
    {
        return new StatusCodeResultResponseReader<T>(singleton);
    }

    /// <inheritdoc />
    internal override ValueTask<T> ReadAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<T>(this.FindForStatus(context.Status)!);
    }

    /// <inheritdoc />
    internal override ValueTask<(bool Read, T Value)> ReadOtherMemberAsync(ResponseReadContext context, CancellationToken cancellationToken)
    {
        return context.MediaType is null && this.FindForStatus(context.Status) is { } singleton
            ? new ValueTask<(bool Read, T Value)>((true, singleton))
            : default;
    }

    private T? FindForStatus(int status)
    {
        return this._instance is { } instance && instance.StatusCode == status ? (T)instance : null;
    }
}
