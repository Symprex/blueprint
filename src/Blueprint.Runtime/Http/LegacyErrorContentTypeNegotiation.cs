using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Primitives;

namespace Blueprint.Runtime.Http;

/// <summary>
/// Chooses an error response's content type from the request's <c>Accept</c> header as legacy Blueprint chose it, for
/// <see cref="BlueprintHttpOptions.NegotiateErrorContentType"/>.
/// </summary>
/// <remarks>
/// <para>
/// A port of what legacy (Signature365 <c>8b40ecc961</c>, <c>Blueprint.Http/Formatters</c>) did with the one output
/// formatter Signature365 registered, System.Text.Json's, and <c>RespectBrowserAcceptHeader</c> and
/// <c>ReturnHttpNotAcceptable</c> left <see langword="false"/>:
/// </para>
/// <list type="bullet">
/// <item><description>Every <c>Accept</c> line is parsed into media ranges, an unparseable one skipped
/// (<c>AcceptHeaderParser.cs:21-128</c>, itself ASP.NET Core MVC's).</description></item>
/// <item><description>Any <c>*/*</c> discards them all (<c>DefaultOutputFormatterSelector.cs:102-114</c>). With none, the
/// formatter's first media type is written, <c>application/json</c> (:54-61, :121-136;
/// <c>SystemTextJsonResultOutputFormatter.cs:36-40</c>).</description></item>
/// <item><description>Otherwise the ranges are taken in descending quality (:116). The first that is a subset of
/// <c>application/json</c>, <c>text/json</c> or <c>application/*+json</c> wins (:138-156;
/// <c>SystemTextJsonResultOutputFormatter.cs:25-30</c>, :43-49). With none, <c>application/json</c> is written
/// (:71-80).</description></item>
/// <item><description>The content type written was the winning <c>MediaType</c>'s <c>ToString()</c>
/// (<c>SystemTextJsonResultOutputFormatter.cs:60</c>). Legacy's <c>MediaType</c> kept, for that, the whole buffer its
/// segment came from (<c>MediaType.cs:34-37</c>, :71, :347-350): the <c>Accept</c> line, not the range alone. So a
/// single-range <c>Accept</c> gives that range. Several on one line give the line.</description></item>
/// </list>
/// <para>
/// No charset is added: legacy added none. Media type matching is ASP.NET Core MVC's <see cref="MediaType"/>, which
/// legacy's was copied from.
/// </para>
/// </remarks>
internal static class LegacyErrorContentTypeNegotiation
{
    /// <summary>
    /// The content type written when the request names no JSON media type, or accepts anything.
    /// </summary>
    internal const string DefaultContentType = "application/json";

    private static readonly MediaType[] _supportedMediaTypes =
    [
        new("application/json"),
        new("text/json"),
        new("application/*+json"),
    ];

    /// <summary>
    /// Chooses the content type legacy Blueprint would have written an error with, for a request with these
    /// <c>Accept</c> header lines.
    /// </summary>
    /// <param name="acceptHeaders">The request's <c>Accept</c> header lines.</param>
    /// <returns>The content type to write.</returns>
    internal static string Negotiate(StringValues acceptHeaders)
    {
        List<MediaTypeSegmentWithQuality>? accepted = null;

        foreach (var line in acceptHeaders)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var charIndex = 0;

            while (charIndex < line.Length)
            {
                var startCharIndex = charIndex;

                if (TryParseValue(line, ref charIndex, out var output) && output.MediaType.HasValue)
                {
                    var mediaType = new MediaType(output.MediaType);

                    if (mediaType.MatchesAllTypes && mediaType.MatchesAllSubTypes)
                    {
                        return DefaultContentType;
                    }

                    (accepted ??= []).Add(output);
                }

                if (charIndex <= startCharIndex)
                {
                    break;
                }
            }
        }

        if (accepted is null)
        {
            return DefaultContentType;
        }

        // Legacy sorted with List<T>.Sort, an insertion sort for the handful of ranges an Accept header carries, so equal
        // qualities kept their order; OrderByDescending is stable for any length.
        foreach (var candidate in accepted.OrderByDescending(static a => a.Quality))
        {
            var mediaType = new MediaType(candidate.MediaType);

            foreach (var supported in _supportedMediaTypes)
            {
                if (mediaType.IsSubsetOf(supported))
                {
                    return candidate.MediaType.Buffer!;
                }
            }
        }

        return DefaultContentType;
    }

    // AcceptHeaderParser.TryParseValue (legacy AcceptHeaderParser.cs:60-128).
    private static bool TryParseValue(string value, ref int index, out MediaTypeSegmentWithQuality parsedValue)
    {
        parsedValue = default;

        if (index == value.Length)
        {
            return true;
        }

        var currentIndex = GetNextNonEmptyOrWhitespaceIndex(value, index, out _);

        if (currentIndex == value.Length)
        {
            index = currentIndex;
            return true;
        }

        // Media types that cannot be parsed are skipped, to the next separator, as legacy and ASP.NET Core skip them: some
        // browsers send invalid ones.
        MediaTypeSegmentWithQuality result;
        int length;
        try
        {
            result = MediaType.CreateMediaTypeSegmentWithQuality(value, currentIndex);
            length = result.MediaType.HasValue ? result.MediaType.Length : 0;
        }
        catch (FormatException)
        {
            result = default;
            length = 0;
        }
        catch (OverflowException)
        {
            result = default;
            length = 0;
        }

        if (length == 0)
        {
            currentIndex = value.IndexOf(',', currentIndex);
            index = currentIndex == -1 ? value.Length : currentIndex;
            return false;
        }

        currentIndex += length;
        currentIndex = GetNextNonEmptyOrWhitespaceIndex(value, currentIndex, out var separatorFound);

        if (!separatorFound && currentIndex < value.Length)
        {
            index = currentIndex;
            return false;
        }

        index = currentIndex;
        parsedValue = result;
        return true;
    }

    // AcceptHeaderParser.GetNextNonEmptyOrWhitespaceIndex (legacy AcceptHeaderParser.cs:130-159).
    private static int GetNextNonEmptyOrWhitespaceIndex(string input, int startIndex, out bool separatorFound)
    {
        separatorFound = false;
        var current = startIndex + GetWhitespaceLength(input, startIndex);

        if (current == input.Length || input[current] != ',')
        {
            return current;
        }

        separatorFound = true;
        current++;
        current += GetWhitespaceLength(input, current);

        while (current < input.Length && input[current] == ',')
        {
            current++;
            current += GetWhitespaceLength(input, current);
        }

        return current;
    }

    // HttpTokenParsingRules.GetWhitespaceLength (legacy HttpTokenParsingRules.cs:88-126).
    private static int GetWhitespaceLength(string input, int startIndex)
    {
        if (startIndex >= input.Length)
        {
            return 0;
        }

        var current = startIndex;

        while (current < input.Length)
        {
            var c = input[current];

            if (c is ' ' or '\t')
            {
                current++;
                continue;
            }

            if (c == '\r' && current + 2 < input.Length && input[current + 1] == '\n' && input[current + 2] is ' ' or '\t')
            {
                current += 3;
                continue;
            }

            return current - startIndex;
        }

        return input.Length - startIndex;
    }
}
