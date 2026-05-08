using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Sample.Contract.Api;

namespace Blueprint.SourceGenerator.Sample.Api;

/// <summary>
/// Handles the <see cref="HealthCheckQuery"/> by returning a plain-text health status string.
/// Demonstrates how <see cref="PlainTextResult"/> bypasses JSON serialization and writes
/// a raw <c>text/plain</c> response body — useful for load-balancer probes and simple status endpoints.
/// </summary>
public class HealthCheckQueryHandler
{
    /// <summary>
    /// Returns a plain-text health status.
    /// </summary>
    /// <param name="query">The health-check query (unused).</param>
    /// <returns>A <see cref="PlainTextResult"/> with the health status string.</returns>
    public PlainTextResult Handle(HealthCheckQuery query)
    {
        return new PlainTextResult("healthy");
    }
}
