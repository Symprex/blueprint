using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Mcp;

/// <summary>
/// The HTTP request an operation's endpoint is given for one tool call: its route values and path, query string,
/// headers and JSON body, each made from the tool's arguments by where the operation reads that input from.
/// </summary>
/// <remarks>
/// Arguments are written as an HTTP caller would write them. A string goes as itself, any other JSON value as its JSON
/// text (so <c>true</c>, <c>42</c>), an array in the query or a header as one value per element, and a body property as
/// the JSON value it is. An argument the schema has no property for is ignored, as an unknown query parameter or body
/// property is over HTTP. A null query, header or path argument is left out.
/// </remarks>
internal sealed class OperationToolRequest
{
    private OperationToolRequest(
        string path,
        RouteValueDictionary routeValues,
        QueryString queryString,
        IReadOnlyList<KeyValuePair<string, StringValues>> headers,
        byte[]? body)
    {
        this.Path = path;
        this.RouteValues = routeValues;
        this.QueryString = queryString;
        this.Headers = headers;
        this.Body = body;
    }

    /// <summary>Gets the request path, under the route pattern's own base path, with every route value written in.</summary>
    public string Path { get; }

    /// <summary>Gets the route values, unescaped, as routing would have matched them from <see cref="Path"/>.</summary>
    public RouteValueDictionary RouteValues { get; }

    /// <summary>Gets the query string.</summary>
    public QueryString QueryString { get; }

    /// <summary>Gets the headers the arguments supply.</summary>
    public IReadOnlyList<KeyValuePair<string, StringValues>> Headers { get; }

    /// <summary>Gets the UTF-8 JSON body, or <see langword="null"/> for an operation that reads no body.</summary>
    public byte[]? Body { get; }

    /// <summary>
    /// Makes the request for one call of the tool for <paramref name="schema"/>'s operation.
    /// </summary>
    /// <param name="route">The route pattern of the endpoint the call runs through.</param>
    /// <param name="schema">The operation's input schema, which says where each argument goes.</param>
    /// <param name="arguments">The tool call's arguments, or <see langword="null"/> for none.</param>
    /// <param name="error">Why no request can be made, when this returns <see langword="null"/>.</param>
    /// <returns>The request, or <see langword="null"/> when a route value it needs is missing.</returns>
    public static OperationToolRequest? Create(
        RoutePattern route,
        OperationInputSchema schema,
        IDictionary<string, JsonElement>? arguments,
        out string? error)
    {
        var routeValues = new RouteValueDictionary();
        var query = new List<KeyValuePair<string, string?>>();
        var headers = new List<KeyValuePair<string, StringValues>>();
        JsonObject? body = null;

        foreach (var property in schema.Properties)
        {
            if (property.Location == OperationInputLocation.Body)
            {
                body ??= [];
            }

            if (!TryGetArgument(arguments, property.Name, out var value))
            {
                continue;
            }

            switch (property.Location)
            {
                case OperationInputLocation.Path when value.ValueKind != JsonValueKind.Null:
                    routeValues[property.Name] = ToText(value);
                    break;

                case OperationInputLocation.Query when value.ValueKind != JsonValueKind.Null:
                    foreach (var item in ToTexts(value))
                    {
                        query.Add(new KeyValuePair<string, string?>(property.Name, item));
                    }

                    break;

                case OperationInputLocation.Header when value.ValueKind != JsonValueKind.Null:
                    headers.Add(new KeyValuePair<string, StringValues>(property.Name, ToTexts(value).ToArray()));
                    break;

                case OperationInputLocation.Body:
                    body![property.Name] = JsonNode.Parse(value.GetRawText());
                    break;
            }
        }

        var path = WritePath(route, routeValues, out error);

        if (path is null)
        {
            return null;
        }

        return new OperationToolRequest(
            path,
            routeValues,
            QueryString.Create(query),
            headers,
            body is null ? null : Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    private static bool TryGetArgument(IDictionary<string, JsonElement>? arguments, string name, out JsonElement value)
    {
        if (arguments is null)
        {
            value = default;
            return false;
        }

        if (arguments.TryGetValue(name, out value))
        {
            return true;
        }

        // The schema names each property as the operation reads it; a client that changed its case still means it.
        foreach (var (key, candidate) in arguments)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    private static string? WritePath(RoutePattern route, RouteValueDictionary routeValues, out string? error)
    {
        var path = new StringBuilder();

        foreach (var segment in route.PathSegments)
        {
            path.Append('/');

            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        path.Append(literal.Content);
                        break;

                    case RoutePatternSeparatorPart separator:
                        path.Append(separator.Content);
                        break;

                    case RoutePatternParameterPart parameter:
                        var value = FindRouteValue(routeValues, parameter.Name);

                        if (value is null)
                        {
                            if (parameter.IsOptional || parameter.Default is not null)
                            {
                                break;
                            }

                            error = $"The argument '{parameter.Name}' is required: it is part of the operation's URL.";
                            return null;
                        }

                        // Routing matches a catch-all's slashes as part of its value, and every other value unescaped.
                        path.Append(parameter.IsCatchAll
                            ? string.Join('/', value.Split('/').Select(Uri.EscapeDataString))
                            : Uri.EscapeDataString(value));
                        break;
                }
            }
        }

        error = null;
        return path.Length == 0 ? "/" : path.ToString();
    }

    private static string? FindRouteValue(RouteValueDictionary routeValues, string name)
    {
        // RouteValueDictionary compares its keys ignoring case, as routing does.
        return routeValues.TryGetValue(name, out var value) ? value as string : null;
    }

    private static string ToText(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    }

    private static IEnumerable<string> ToTexts(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [ToText(value)];
        }

        return value.EnumerateArray()
            .Where(static item => item.ValueKind != JsonValueKind.Null)
            .Select(static item => ToText(item))
            .ToList();
    }
}
