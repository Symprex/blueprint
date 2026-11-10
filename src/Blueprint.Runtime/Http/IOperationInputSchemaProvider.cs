using System.Collections.Immutable;
using System.Text.Json;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Gives each operation the app exposes over HTTP one flattened JSON Schema of its input: the route, query, header and
/// body properties a caller sends, as the properties of one object, with the constraints and descriptions the OpenAPI
/// document gives them. It is what a tool surface (MCP) or a search over the API (Code Mode) describes an operation by.
/// </summary>
/// <remarks>
/// <para>
/// The schemas are made from the same rendering as the app's OpenAPI document, once, when they are first asked for, so
/// they agree with the document the app serves, its operation and schema processors included. A property the caller
/// does not send, set by the auth pipeline (<c>[FromAuthContext]</c>) or by the runtime, is not in it, nor is a cookie.
/// </para>
/// <para>
/// <c>AddBlueprintApi</c> registers the provider and <c>MapBlueprintApi</c> gives it the app's operations. It fails
/// with <see cref="InvalidOperationException"/> until the API is mapped, and for an operation two of whose properties
/// would take the same name in the one object (compared ignoring case), which a caller could not tell apart.
/// </para>
/// </remarks>
public interface IOperationInputSchemaProvider
{
    /// <summary>
    /// Gets the input schema of every exposed operation, ordered by operation name.
    /// </summary>
    /// <returns>One schema per exposed operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// The API has not been mapped, or an operation's properties collide; the message names every such operation.
    /// </exception>
    IReadOnlyList<OperationInputSchema> GetSchemas();

    /// <summary>
    /// Gets the input schema of one operation.
    /// </summary>
    /// <param name="operationName">
    /// The operation's name, as its descriptor gives it. Case is ignored only when no operation has the name exactly.
    /// </param>
    /// <returns>Its schema, or <see langword="null"/> when the app exposes no operation of that name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The API has not been mapped, two of the operation's properties take the same name, or the name matches two
    /// operations' only ignoring case.
    /// </exception>
    OperationInputSchema? GetSchema(string operationName);
}

/// <summary>
/// The flattened input of one exposed operation.
/// </summary>
/// <param name="OperationName">The operation's name, as its descriptor gives it.</param>
/// <param name="OperationId">
/// The <c>operationId</c> the OpenAPI document gives the operation at the slot a request made from
/// <paramref name="Schema"/> is sent to: the first URL it declares with a placeholder for each of its path inputs, at a
/// method that reads its body when it has one. The id is unique across the document.
/// </param>
/// <param name="Schema">
/// A self-contained JSON Schema (draft 2020-12) of an object whose properties are the operation's inputs: any type
/// it refers to is defined under its <c>$defs</c>.
/// </param>
/// <param name="Properties">
/// Each property of <paramref name="Schema"/>, in its order, with where the operation reads it from, so a caller
/// holding an instance of the schema can make the operation's request.
/// </param>
public sealed record OperationInputSchema(
    string OperationName,
    string OperationId,
    JsonElement Schema,
    ImmutableArray<OperationInputProperty> Properties)
{
    /// <summary>Gets the operation's summary, as the OpenAPI document gives it, or <see langword="null"/> for none.</summary>
    public string? Summary { get; init; }

    /// <summary>Gets the operation's longer description, as the OpenAPI document gives it, or <see langword="null"/> for none.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the URL, as the operation declares it, that <see cref="OperationId"/> names and a request made from
    /// <see cref="Schema"/> is sent to, or <see langword="null"/> when it is not known.
    /// </summary>
    internal string? UrlFormat { get; init; }

    /// <summary>
    /// Gets the HTTP method, upper-case, that <see cref="OperationId"/> names and a request made from
    /// <see cref="Schema"/> is sent with, or <see langword="null"/> when it is not known.
    /// </summary>
    internal string? HttpMethod { get; init; }
}

/// <summary>
/// One property of an operation's flattened input, and where the operation reads it from.
/// </summary>
/// <param name="Name">
/// The property's name in the schema, which is also its name where the operation reads it: the route placeholder,
/// the query or header name, or the body's JSON property name.
/// </param>
/// <param name="Location">Where the operation reads the property from.</param>
public sealed record OperationInputProperty(string Name, OperationInputLocation Location);

/// <summary>
/// Where an operation reads one of its inputs from.
/// </summary>
public enum OperationInputLocation
{
    /// <summary>A route placeholder.</summary>
    Path,

    /// <summary>The query string.</summary>
    Query,

    /// <summary>A request header.</summary>
    Header,

    /// <summary>A property of the JSON request body.</summary>
    Body,
}
