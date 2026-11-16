using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles <see cref="ImportTemplateSourceCommand"/>: reads the request body as it arrives and answers with what arrived.
/// </summary>
public sealed class ImportTemplateSourceCommandHandler(TemplateStore store)
{
    /// <summary>
    /// Reads the request body the command was handed.
    /// </summary>
    /// <param name="command">The command, its content the request body itself.</param>
    /// <returns>What arrived.</returns>
    public async Task<TemplateUploadApiResource> HandleAsync(ImportTemplateSourceCommand command)
    {
        _ = store.GetById(command.Id) ?? throw new InvalidOperationException("Template not found");

        var (length, sha256) = await TemplateUploadDigest.ReadAsync(command.Content);

        return new TemplateUploadApiResource(command.Id, fileName: null, contentType: null, description: null, length, sha256);
    }
}
