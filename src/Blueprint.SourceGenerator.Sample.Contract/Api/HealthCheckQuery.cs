using System.ComponentModel;
using Blueprint.Model;
using Blueprint.Model.Attributes;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Sample.Contract.Api;

/// <summary>
/// Returns a plain-text health check response. Demonstrates <see cref="PlainTextResult"/> which
/// bypasses JSON serialization and writes a raw <c>text/plain</c> response body.
/// </summary>
[ApiOperations("System")]
[RootLink("/system/health")]
[HttpGet]
[AllowAnonymous]
[Description("Returns a plain-text health status for load balancers and probes.")]
public partial class HealthCheckQuery : IQuery<PlainTextResult>
{
}
