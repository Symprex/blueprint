namespace Blueprint.Runtime.Http;

/// <summary>
/// How the OpenAPI document names an operation's query parameters.
/// </summary>
public enum OpenApiParameterNaming
{
    /// <summary>
    /// The name the parameter is bound by, which is the one the framework's typed client sends (<c>pageSize</c>).
    /// </summary>
    BindingName = 0,

    /// <summary>
    /// The CLR property's name (<c>PageSize</c>), as legacy Blueprint named it. The host binds a query parameter whatever
    /// its case, so either name reaches the operation.
    /// </summary>
    PropertyName = 1,
}
