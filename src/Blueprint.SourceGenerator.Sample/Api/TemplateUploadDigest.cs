using System.Security.Cryptography;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Reads an upload to its end a buffer at a time, so its size never decides how much is held in memory.
/// </summary>
internal static class TemplateUploadDigest
{
    /// <summary>
    /// Reads <paramref name="content"/> to its end.
    /// </summary>
    /// <param name="content">The upload.</param>
    /// <returns>How many bytes it held, and their upper-case hex SHA-256.</returns>
    public static async Task<(long Length, string Sha256)> ReadAsync(Stream content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;

        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        return (length, Convert.ToHexString(hash.GetHashAndReset()));
    }
}
