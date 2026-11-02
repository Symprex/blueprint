using System.Text.Json.Serialization.Metadata;

namespace Blueprint.HttpClient;

/// <summary>
/// How a typed client sends one operation, fixed when the client is compiled: the HTTP method, the operation's generated
/// <c>Op.Http</c> request builders, the reader of its result and, optionally, a serializer context whose metadata comes
/// first. A call through <see cref="BlueprintHttpClient.SendAsync{TOperation, TResult}"/> therefore needs no
/// <c>GetType</c>, descriptor lookup, type test, <c>Activator</c> or uninitialised object.
/// </summary>
/// <typeparam name="TOperation">The operation type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public sealed class BlueprintCallPlan<TOperation, TResult>
    where TOperation : notnull
{
    /// <summary>
    /// Initialises a new instance of the <see cref="BlueprintCallPlan{TOperation, TResult}"/> class.
    /// </summary>
    /// <param name="operationName">The operation's type name, which a failure to understand a response names.</param>
    /// <param name="method">The HTTP method. A GET carries the operation's query-bound properties as its query string; every other method carries the operation as its JSON body.</param>
    /// <param name="buildUrl">The operation's <c>Op.Http.BuildUrl</c>.</param>
    /// <param name="appendQueryString">The operation's <c>Op.Http.AppendQueryString</c> for a GET, or <see langword="null"/>.</param>
    /// <param name="reader">The reader of the result.</param>
    /// <param name="serializerContext">A serializer context whose metadata comes first, or <see langword="null"/> to use the client's options as they are.</param>
    public BlueprintCallPlan(
        string operationName,
        HttpMethod method,
        Func<TOperation, string?> buildUrl,
        Func<TOperation, string, string>? appendQueryString,
        BlueprintResponseReader<TResult> reader,
        IJsonTypeInfoResolver? serializerContext = null)
    {
        this.OperationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
        this.Method = method ?? throw new ArgumentNullException(nameof(method));
        this.BuildUrl = buildUrl ?? throw new ArgumentNullException(nameof(buildUrl));
        this.AppendQueryString = appendQueryString;
        this.Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.SerializerContext = serializerContext;
    }

    /// <summary>
    /// Gets the operation's type name.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the HTTP method.
    /// </summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// Gets the URL builder.
    /// </summary>
    public Func<TOperation, string?> BuildUrl { get; }

    /// <summary>
    /// Gets the query string builder of a GET.
    /// </summary>
    public Func<TOperation, string, string>? AppendQueryString { get; }

    /// <summary>
    /// Gets the reader of the result.
    /// </summary>
    public BlueprintResponseReader<TResult> Reader { get; }

    /// <summary>
    /// Gets the serializer context whose metadata comes first, if any.
    /// </summary>
    public IJsonTypeInfoResolver? SerializerContext { get; }
}
