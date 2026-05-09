using Blueprint.Model;

namespace Blueprint.Http;

/// <summary>
/// Generates URLs and <see cref="Link"/> instances for registered <see cref="ApiOperationLink"/>s,
/// enabling runtime link population on resources and URL generation for operations.
/// </summary>
public interface IApiLinkGenerator
{
    /// <summary>
    /// Creates the "self" link for the resource type represented by <typeparamref name="T"/>, filling
    /// the URL placeholders with values from <paramref name="idDefinition"/>.
    /// </summary>
    /// <typeparam name="T">The <see cref="ApiResource"/> type.</typeparam>
    /// <param name="idDefinition">
    /// An object whose properties are used to fill URL placeholders (typically the resource itself
    /// or an anonymous object with the same property names as route parameters).
    /// </param>
    /// <param name="queryString">
    /// An optional object whose properties will be appended as query string parameters.
    /// </param>
    /// <returns>A new <see cref="Link"/> referencing the given resource.</returns>
    Link CreateSelfLink<T>(object idDefinition, object? queryString = null)
        where T : ApiResource;

    /// <summary>
    /// Creates the "self" link for a resource identified by a single string <paramref name="id"/>.
    /// </summary>
    /// <typeparam name="T">The <see cref="ApiResource"/> type.</typeparam>
    /// <param name="id">The identifier used to fill the <c>{Id}</c> placeholder.</param>
    /// <param name="queryString">Optional query string parameters.</param>
    /// <returns>A new <see cref="Link"/> referencing the given resource.</returns>
    Link CreateSelfLink<T>(string id, object? queryString = null)
        where T : ApiResource;

    /// <summary>
    /// Creates the "self" link for a resource identified by an integer <paramref name="id"/>.
    /// </summary>
    /// <typeparam name="T">The <see cref="ApiResource"/> type.</typeparam>
    /// <param name="id">The identifier used to fill the <c>{Id}</c> placeholder.</param>
    /// <param name="queryString">Optional query string parameters.</param>
    /// <returns>A new <see cref="Link"/> referencing the given resource.</returns>
    Link CreateSelfLink<T>(int id, object? queryString = null)
        where T : ApiResource;

    /// <summary>
    /// Creates the "self" link for a resource identified by a <see cref="Guid"/> <paramref name="id"/>.
    /// </summary>
    /// <typeparam name="T">The <see cref="ApiResource"/> type.</typeparam>
    /// <param name="id">The identifier used to fill the <c>{Id}</c> placeholder.</param>
    /// <param name="queryString">Optional query string parameters.</param>
    /// <returns>A new <see cref="Link"/> referencing the given resource.</returns>
    Link CreateSelfLink<T>(Guid id, object? queryString = null)
        where T : ApiResource;

    /// <summary>
    /// Creates a fully qualified URL for the specified <paramref name="link"/>, using the
    /// <paramref name="result"/> object to fill route placeholders.
    /// </summary>
    /// <param name="link">The link to generate a URL for.</param>
    /// <param name="result">An object whose properties fill route placeholders. May be <see langword="null"/> for placeholder-less links.</param>
    /// <returns>A fully-qualified URL, or <see langword="null"/> if required placeholders cannot be filled.</returns>
    string? CreateUrl(ApiOperationLink link, object? result = null);

    /// <summary>
    /// Generates a fully-qualified URL for an operation instance, using the first registered
    /// link for the operation type and the operation's property values to fill placeholders.
    /// </summary>
    /// <param name="operation">The populated operation instance.</param>
    /// <returns>A fully-qualified URL.</returns>
    /// <exception cref="InvalidOperationException">No link is registered for the operation type.</exception>
    string CreateUrl(object operation);
}
