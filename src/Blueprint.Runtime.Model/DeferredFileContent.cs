using System.Net;

namespace Blueprint.Model;

/// <summary>
/// A file part whose content is opened only when the part is sent, and again each time it is (see
/// <see cref="HttpRequestBodies.AddFile"/>).
/// </summary>
/// <param name="openContent">Opens the file's content.</param>
/// <param name="fileLength">The file's length in bytes.</param>
internal sealed class DeferredFileContent(Func<Stream> openContent, long fileLength) : HttpContent
{
    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await this.SerializeToStreamAsync(stream, context, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var content = openContent();
        await using (content.ConfigureAwait(false))
        {
            await content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = fileLength;
        return true;
    }
}
