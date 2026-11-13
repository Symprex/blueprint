using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;

namespace Blueprint.Mcp;

/// <summary>
/// Turns the response an operation's endpoint gave a tool call into the call's result: the response an HTTP caller
/// would have been given, so a client sees one API whichever way it calls it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The body is the result's text, unchanged, so a problem body (a 401, a 422's validation errors) reads as it
/// does over HTTP.</item>
/// <item>A JSON object body is also the structured content. MCP's structured content is an object, so an array body is
/// text only.</item>
/// <item>The status is <c>_meta.httpStatus</c>, and a 4xx or 5xx status makes the result a tool error, which a model
/// can read and act on, rather than a protocol error, which it cannot.</item>
/// <item>A body that is not text (a file, a stream), or text longer than <see cref="MaxTextBytes"/>, is not given; the
/// result says what it was instead.</item>
/// </list>
/// </remarks>
internal static class OperationToolResult
{
    /// <summary>The <c>_meta</c> key the HTTP status is given under.</summary>
    public const string HttpStatusMetaKey = "httpStatus";

    /// <summary>The most bytes of a text body a result gives, which bounds what a tool call holds in memory.</summary>
    public const int MaxTextBytes = 1024 * 1024;

    /// <summary>
    /// Makes a tool call's result from the response the operation's endpoint wrote.
    /// </summary>
    /// <param name="statusCode">The response's status code.</param>
    /// <param name="contentType">The response's content type, or <see langword="null"/> for none.</param>
    /// <param name="body">The response's body, as the endpoint wrote it.</param>
    /// <returns>The result.</returns>
    public static CallToolResult Create(int statusCode, string? contentType, ToolResponseBody body)
    {
        var result = new CallToolResult
        {
            IsError = statusCode >= 400,
            Meta = new JsonObject { [HttpStatusMetaKey] = statusCode },
        };

        if (body.BytesWritten == 0)
        {
            return result;
        }

        if (!body.IsComplete)
        {
            var described = contentType ?? "unknown content";
            var description = body.IsText
                ? $"The operation returned {body.BytesWritten} bytes of {described}, more than the {body.MaxTextBytes} bytes of text a tool result carries."
                : $"The operation returned {body.BytesWritten} bytes of {described}, which a tool result does not carry.";

            result.Content = [new TextContentBlock { Text = description }];
            return result;
        }

        var text = GetEncoding(contentType).GetString(body.GetText().Span);
        result.Content = [new TextContentBlock { Text = text }];

        if (IsJson(contentType))
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    result.StructuredContent = document.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                // A body that claims to be JSON and is not is still given as the text it is.
            }
        }

        return result;
    }

    /// <summary>
    /// Gets whether a body of <paramref name="contentType"/> is text, which a result gives as it is.
    /// </summary>
    /// <param name="contentType">The response's content type, or <see langword="null"/> for none.</param>
    /// <returns>Whether the body is text.</returns>
    public static bool IsText(string? contentType)
    {
        return contentType is null
               || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || IsJson(contentType);
    }

    /// <summary>
    /// Gets the encoding the content type's charset names, or UTF-8 when it names none, or one this runtime lacks.
    /// </summary>
    private static Encoding GetEncoding(string? contentType)
    {
        try
        {
            return MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ? mediaType.Encoding ?? Encoding.UTF8 : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static bool IsJson(string? contentType)
    {
        // application/json, application/problem+json, application/hal+json and the like.
        return contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }
}
