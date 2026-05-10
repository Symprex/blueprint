using System.Text;

namespace Blueprint.Utilities;

/// <summary>
/// Extension methods for <see cref="Stream"/> providing convenience methods to
/// fully read stream contents and to create streams from strings and byte arrays.
/// </summary>
public static class StreamExtensions
{
    private const int SixteenKilobytes = 16 * 1024;

    /// <summary>
    /// Reads the remaining contents of the stream from the current position into
    /// a byte array.
    /// </summary>
    /// <param name="input">The stream to read from.</param>
    /// <returns>A byte array containing the remaining contents.</returns>
    public static byte[] ReadFully(this Stream input)
    {
        Guard.NotNull(input);

        var buffer = new byte[SixteenKilobytes];

        using var ms = new MemoryStream();

        int read;

        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Reads the stream fully as a string. If the stream supports seeking,
    /// the position is reset to zero before reading.
    /// </summary>
    /// <param name="input">The stream to read.</param>
    /// <returns>The full string content of the stream.</returns>
    public static async Task<string> ReadFullyAsStringAsync(this Stream input)
    {
        Guard.NotNull(input);

        if (input.CanSeek)
        {
            input.Seek(0, SeekOrigin.Begin);
        }

        // leaveOpen: true so the caller retains ownership of the underlying stream
        using var streamReader = new StreamReader(input, leaveOpen: true);
        return await streamReader.ReadToEndAsync();
    }

    /// <summary>
    /// Converts this string to a <see cref="MemoryStream"/> encoded as UTF-8.
    /// </summary>
    /// <param name="value">The string to convert.</param>
    /// <returns>A new stream containing the UTF-8 bytes.</returns>
    public static Stream AsUtf8Stream(this string value)
    {
        return Encoding.UTF8.GetBytes(value).AsStream();
    }

    /// <summary>
    /// Creates a new <see cref="MemoryStream"/> from this byte array, positioned
    /// at the start ready for reading.
    /// </summary>
    /// <param name="bytes">The bytes to populate the stream with.</param>
    /// <returns>A new stream with the given bytes as content.</returns>
    public static Stream AsStream(this byte[] bytes)
    {
        return new MemoryStream(bytes, writable: false);
    }
}
