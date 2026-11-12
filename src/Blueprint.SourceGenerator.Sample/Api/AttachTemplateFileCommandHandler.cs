using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles <see cref="AttachTemplateFileCommand"/>: reads the attached file as a stream and answers with what arrived.
/// </summary>
public sealed class AttachTemplateFileCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Reads the file the multipart body carried.
    /// </summary>
    /// <param name="command">The command, its file bound from the form.</param>
    /// <returns>What arrived.</returns>
    public async Task<TemplateUploadApiResource> HandleAsync(AttachTemplateFileCommand command)
    {
        _ = store.GetById(command.Id) ?? throw new InvalidOperationException("Template not found");

        await using var file = command.File.OpenReadStream();
        var (length, sha256) = await TemplateUploadDigest.ReadAsync(file);

        return new TemplateUploadApiResource(command.Id, command.File.FileName, command.File.ContentType, command.Description, length, sha256);
    }
}
