namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Represents an accepted asynchronous export job with a tracking identifier and estimated completion time.
/// Returned as the body of a <c>202 Accepted</c> response to let callers poll for completion.
/// </summary>
public class ExportJobApiResource
{
    /// <summary>
    /// Gets or sets the unique job identifier that callers can use to check export status.
    /// </summary>
    public required string JobId { get; set; }

    /// <summary>
    /// Gets or sets the estimated number of seconds until the export is ready for download.
    /// </summary>
    public int EstimatedSeconds { get; set; }
}
