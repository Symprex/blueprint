using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;

namespace Blueprint.Model;

/// <summary>
/// The pieces a generated <see cref="IHttpRequestBodyBuilder"/> builds a stream or multipart request body from, so each
/// operation's implementation is only the list of its own properties.
/// </summary>
/// <remarks>
/// The source generator calls these; they are not meant to be called by hand. A field is written as the host's generated
/// binding parses it back (<c>PipelineHelpers.BindFormValue</c>), with the invariant culture, so a number, a date or a
/// <see langword="bool"/> round-trips whatever the sending machine's culture.
/// </remarks>
public static class HttpRequestBodies
{
    /// <summary>
    /// Creates the content of an operation whose <c>[FromBody]</c> <see cref="Stream"/> property is its whole body.
    /// </summary>
    /// <remarks>
    /// The stream is sent a read at a time, each read written and flushed before the next is made, so a forward passes an
    /// inbound body upstream as it arrives. A <see cref="StreamContent"/> leaves the copy to the stream's own
    /// <see cref="Stream.CopyToAsync(Stream, int, CancellationToken)"/>, and over Kestrel's request body stream that used to
    /// hold a 24 MB upload through a gateway back from the inner service until the caller had sent all of it
    /// (<c>StreamRequestTests</c> in <c>Blueprint.EndToEnd.Tests</c>).
    /// </remarks>
    /// <param name="body">The stream, read as the request is sent; <see langword="null"/> sends an empty body.</param>
    /// <returns>The content, typed <c>application/octet-stream</c>.</returns>
    public static HttpContent CreateStreamContent(Stream? body)
    {
        var content = new ForwardingStreamContent(body ?? Stream.Null);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return content;
    }

    /// <summary>
    /// Adds a file part to a form, its content opened and streamed only when the part itself is sent.
    /// </summary>
    /// <remarks>
    /// An inbound form read with <c>FormOptions.BufferBody</c> gives each file a view of its own range of one shared stream,
    /// which refuses to be read once anything else has moved it; opening every file while the form was built used to move
    /// it under the first part, so a forward of two such files failed. Each send opens the file again, so a redirect or a
    /// retry sends it whole.
    /// </remarks>
    /// <param name="form">The form.</param>
    /// <param name="name">The part's name, the property's binding name.</param>
    /// <param name="openContent">Opens the file's content, as <c>IFormFile.OpenReadStream</c> does.</param>
    /// <param name="length">The file's length in bytes.</param>
    /// <param name="fileName">The file's name, which the host's <c>IFormFile.FileName</c> gives back.</param>
    /// <param name="contentType">The file's media type, or <see langword="null"/> or empty for none.</param>
    public static void AddFile(MultipartFormDataContent form, string name, Func<Stream> openContent, long length, string? fileName, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(openContent);

        var part = new DeferredFileContent(openContent, length);
        if (!string.IsNullOrEmpty(contentType) && MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
        {
            part.Headers.ContentType = mediaType;
        }

        if (string.IsNullOrEmpty(fileName))
        {
            form.Add(part, name);
        }
        else
        {
            form.Add(part, name, fileName);
        }
    }

    /// <summary>
    /// Adds a field to a form for a value, or nothing when it is <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The property's type, whose TypeConverter writes a value no other rule does.</typeparam>
    /// <param name="form">The form.</param>
    /// <param name="name">The field's name, the property's binding name.</param>
    /// <param name="value">The value.</param>
    public static void AddField<T>(MultipartFormDataContent form, string name, T? value)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (FormatField(value) is { } text)
        {
            form.Add(new StringContent(text), name);
        }
    }

    /// <summary>
    /// Adds a field to a form for each value of a collection, which the host reads back as one array.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="form">The form.</param>
    /// <param name="name">The fields' name, the property's binding name.</param>
    /// <param name="values">The values, or <see langword="null"/> for none.</param>
    public static void AddFields<T>(MultipartFormDataContent form, string name, IEnumerable<T>? values)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            AddField(form, name, value);
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The host's generated binding looks up the same TypeConverter for the property's type to read the field back, so the type and its converter are already preserved.")]
    [UnconditionalSuppressMessage("Trimming", "IL2087", Justification = "As above: the host preserves the property's type for its own converter lookup.")]
    private static string? FormatField<T>(T? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),

            // TimeOnly's default invariant format is HH:mm, which would drop the seconds.
            TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),

            // The host parses any other field with its TypeConverter's ConvertFromInvariantString, so it is written with the
            // same converter; a type without one of its own gets the default, which is ToString().
            _ => TypeDescriptor.GetConverter(typeof(T)).ConvertToInvariantString(value),
        };
    }
}
