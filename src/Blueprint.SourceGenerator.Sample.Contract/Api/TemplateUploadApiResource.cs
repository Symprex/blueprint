using Blueprint.Model;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// What a template upload received, <see cref="AttachTemplateFileCommand"/>'s file or <see cref="ImportTemplateSourceCommand"/>'s
/// body: enough to prove the bytes arrived intact.
/// </summary>
public partial class TemplateUploadApiResource : ApiResource
{
    // For JSON deserialization
    private TemplateUploadApiResource()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateUploadApiResource"/> class.
    /// </summary>
    /// <param name="templateId">The template the upload was for.</param>
    /// <param name="fileName">The uploaded file's name, or <see langword="null"/> for a raw body.</param>
    /// <param name="contentType">The uploaded content's media type, when it had one.</param>
    /// <param name="description">The upload's description, when it had one.</param>
    /// <param name="length">How many bytes arrived.</param>
    /// <param name="sha256">The upper-case hex SHA-256 of the bytes that arrived.</param>
    public TemplateUploadApiResource(string templateId, string? fileName, string? contentType, string? description, long length, string sha256)
    {
        this.TemplateId = templateId;
        this.FileName = fileName;
        this.ContentType = contentType;
        this.Description = description;
        this.Length = length;
        this.Sha256 = sha256;
    }

    /// <summary>
    /// The template the upload was for.
    /// </summary>
    public string TemplateId { get; private set; } = null!;

    /// <summary>
    /// The uploaded file's name, or <see langword="null"/> for a raw body.
    /// </summary>
    public string? FileName { get; private set; }

    /// <summary>
    /// The uploaded content's media type, when it had one.
    /// </summary>
    public string? ContentType { get; private set; }

    /// <summary>
    /// The upload's description, when it had one.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// How many bytes arrived.
    /// </summary>
    public long Length { get; private set; }

    /// <summary>
    /// The upper-case hex SHA-256 of the bytes that arrived.
    /// </summary>
    public string Sha256 { get; private set; } = null!;
}
