using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="ExportTemplatesCommand"/> by accepting the export request and returning
/// a job tracking reference. Demonstrates the <see cref="AcceptedResult{T}"/> pattern for
/// long-running operations — the HTTP response is <c>202 Accepted</c> with a body that callers
/// use to poll for completion.
/// </summary>
public class ExportTemplatesCommandHandler
{
    /// <summary>
    /// Accepts the export request and returns a tracking reference.
    /// </summary>
    /// <param name="command">The export command with optional filtering.</param>
    /// <returns>An <see cref="AcceptedResult{T}"/> wrapping the export job reference.</returns>
    public AcceptedResult<ExportJobApiResource> Handle(ExportTemplatesCommand command)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];

        return new AcceptedResult<ExportJobApiResource>(new ExportJobApiResource
        {
            JobId = jobId,
            EstimatedSeconds = 30,
        });
    }
}
