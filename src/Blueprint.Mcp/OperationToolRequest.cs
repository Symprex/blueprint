using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blueprint.Runtime.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
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

    /// <summary>
    /// Gets the request path, under the route pattern's own base path, with every route value written in, decoded as a
    /// server gives <c>HttpRequest.Path</c>.
    /// </summary>
    public string Path { get; }

    /// <summary>Gets the route values as routing would have matched them from <see cref="Path"/>, an escaped slash left escaped.</summary>
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
    /// <param name="ambientRouteValues">
    /// The route values the tools were called at, which give a route value no input supplies, such as a parameterised
    /// route group's (<c>tenants/{TenantId}</c>), or <see langword="null"/> for none.
    /// </param>
    /// <param name="error">Why no request can be made, when this returns <see langword="null"/>.</param>
    /// <returns>The request, or <see langword="null"/> when a route value it needs is missing.</returns>
    public static OperationToolRequest? Create(
        RoutePattern route,
        OperationInputSchema schema,
        IDictionary<string, JsonElement>? arguments,
        RouteValueDictionary? ambientRouteValues,
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

        // A route value no input of the operation's supplies, such as a parameterised route group's, is the one the tools
        // were called at, as the HTTP API mapped beside them is called at it.
        if (ambientRouteValues is not null)
        {
            foreach (var parameter in route.Parameters)
            {
                if (!routeValues.ContainsKey(parameter.Name)
                    && !schema.Properties.Any(p => p.Location == OperationInputLocation.Path && string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                    && ambientRouteValues.TryGetValue(parameter.Name, out var ambient)
                    && ambient is not null)
                {
                    routeValues[parameter.Name] = Convert.ToString(ambient, CultureInfo.InvariantCulture);
                }
            }
        }

        var path = WritePath(route, routeValues, out error);

        if (path is null)
        {
            return null;
        }

        // The route values are those routing reads from the path, not the arguments as given: a segment holding two
        // values ({From}-{To}) may split differently from how they were written into it, and binding, validation and
        // authorisation must see what an HTTP request for the same path would give them.
        var matched = new RouteValueDictionary();

        if (!new TemplateMatcher(new RouteTemplate(route), new RouteValueDictionary(route.Defaults)).TryMatch(new PathString(path), matched))
        {
            error = $"No route of the operation's matches the URL its arguments make, {path}.";
            return null;
        }

        return new OperationToolRequest(
            path,
            matched,
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

            for (var i = 0; i < segment.Parts.Count; i++)
            {
                switch (segment.Parts[i])
                {
                    case RoutePatternLiteralPart literal:
                        path.Append(literal.Content);
                        break;

                    // A separator comes before an optional parameter ({Name}.{Ext?}), which a URL may leave out with it.
                    case RoutePatternSeparatorPart separator:
                        if (i + 1 >= segment.Parts.Count
                            || segment.Parts[i + 1] is not RoutePatternParameterPart next
                            || !string.IsNullOrEmpty(FindRouteValue(routeValues, next.Name)))
                        {
                            path.Append(separator.Content);
                        }

                        break;

                    case RoutePatternParameterPart parameter:
                        var value = FindRouteValue(routeValues, parameter.Name);

                        // An empty value is no value: no HTTP request reaches the endpoint with an empty segment for it,
                        // except for a catch-all, which matches an empty remainder, and a defaulted parameter, which
                        // routing gives its default. Constraints are the caller's to evaluate (MatchesConstraints).
                        if (string.IsNullOrEmpty(value))
                        {
                            routeValues.Remove(parameter.Name);

                            if (parameter.Default is not null)
                            {
                                routeValues[parameter.Name] = Convert.ToString(parameter.Default, CultureInfo.InvariantCulture);
                                break;
                            }

                            if (parameter.IsOptional || parameter.IsCatchAll)
                            {
                                break;
                            }

                            error = $"The argument '{parameter.Name}' is required: it is part of the operation's URL.";
                            return null;
                        }

                        // As a server gives the path, decoded but for an escaped slash, which it leaves escaped, and as
                        // routing then gives the value: a catch-all's slashes separate its segments, while any other
                        // value's are part of it and reach the operation escaped, as they do over HTTP.
                        if (!parameter.IsCatchAll && value.Contains('/', StringComparison.Ordinal))
                        {
                            value = value.Replace("/", "%2F", StringComparison.Ordinal);
                            routeValues[parameter.Name] = value;
                        }

                        path.Append(value);
                        break;
                }
            }
        }

        error = null;
        return path.Length == 0 ? "/" : path.ToString();
    }

    /// <summary>
    /// Evaluates the constraints <paramref name="route"/> puts on its parameters (<c>{Id:int:min(1)}</c>) against the
    /// request's route values, as routing does before it chooses an endpoint, since a tool call does not go through
    /// routing. An optional parameter without a value is not held to its constraints, as routing does not hold it.
    /// </summary>
    /// <param name="route">The route pattern of the endpoint the call runs through.</param>
    /// <param name="routeValues">The request's route values.</param>
    /// <param name="httpContext">The request the constraints are evaluated for.</param>
    /// <param name="policyFactory">Makes each constraint from its reference.</param>
    /// <param name="refused">The parameter whose constraint refused its value, when this returns <see langword="false"/>.</param>
    /// <returns>Whether every constraint matches, so HTTP routing would reach the endpoint.</returns>
    public static bool MatchesConstraints(
        RoutePattern route,
        RouteValueDictionary routeValues,
        HttpContext httpContext,
        ParameterPolicyFactory policyFactory,
        out string? refused)
    {
        foreach (var parameter in route.Parameters)
        {
            if (parameter.IsOptional && !routeValues.ContainsKey(parameter.Name))
            {
                continue;
            }

            foreach (var reference in route.ParameterPolicies.GetValueOrDefault(parameter.Name) ?? [])
            {
                if (policyFactory.Create(parameter, reference) is IRouteConstraint constraint
                    && !constraint.Match(httpContext, null, parameter.Name, routeValues, RouteDirection.IncomingRequest))
                {
                    refused = parameter.Name;
                    return false;
                }
            }
        }

        refused = null;
        return true;
    }

    /// <summary>
    /// Gets whether <paramref name="request"/>'s host is one <paramref name="hosts"/> allows, as routing's host matching
    /// decides it (<c>RequireHost</c>): <c>example.com</c>, a subdomain of <c>*.example.com</c>, or <c>*</c>, each with an
    /// optional port, which the request's port, or its scheme's default, must then be.
    /// </summary>
    /// <param name="hosts">The endpoint's host metadata, or <see langword="null"/> for none.</param>
    /// <param name="request">The request.</param>
    /// <returns>Whether HTTP routing would reach the endpoint at the request's host.</returns>
    public static bool MatchesHost(IHostMetadata? hosts, HttpRequest request)
    {
        if (hosts is null || hosts.Hosts.Count == 0)
        {
            return true;
        }

        var host = request.Host.Host;
        var port = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        foreach (var allowed in hosts.Hosts)
        {
            // A colon inside an IPv6 address's brackets ([::1]) is part of the host, not the port's separator.
            var separator = allowed.LastIndexOf(':');

            if (separator < allowed.LastIndexOf(']'))
            {
                separator = -1;
            }

            var allowedHost = separator < 0 ? allowed : allowed[..separator];
            var allowedPort = separator < 0 ? "*" : allowed[(separator + 1)..];

            var hostMatches = allowedHost == "*"
                              || (allowedHost.StartsWith("*.", StringComparison.Ordinal)
                                  ? host.EndsWith(allowedHost[1..], StringComparison.OrdinalIgnoreCase)
                                  : string.Equals(host, allowedHost, StringComparison.OrdinalIgnoreCase));
            var portMatches = allowedPort == "*" || allowedPort == port.ToString(CultureInfo.InvariantCulture);

            if (hostMatches && portMatches)
            {
                return true;
            }
        }

        return false;
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
