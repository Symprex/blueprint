using System.Diagnostics;
using Blueprint.Runtime.Http;

// Matches ASP.NET Core convention for IHost extension discovery
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides development-time helpers for exporting the pre-rendered OpenAPI document to disk
/// so it can be used by code generators, documentation tools, and API clients.
/// </summary>
public static class BlueprintOpenApiExportExtensions
{
    /// <summary>
    /// Writes the pre-rendered OpenAPI 3.x JSON document to <c>Spec/openapi.json</c> relative to the current
    /// working directory. The output is only produced in <c>DEBUG</c> builds and only when the
    /// <c>DOTNET_ENVIRONMENT</c> or <c>ASPNETCORE_ENVIRONMENT</c> environment variable is <c>Development</c>.
    /// </summary>
    /// <remarks>
    /// This mirrors the legacy <c>OutputOpenApi</c> helper from Signature365 and is designed for local
    /// development workflows only. The method is compiled out entirely in Release builds. It is replaced by the
    /// <c>BlueprintEmitOpenApi</c> MSBuild property, which writes the document on every build, in any configuration,
    /// without running the app by hand.
    /// </remarks>
    /// <param name="host">The configured host whose Blueprint app provides the OpenAPI document.</param>
    /// <param name="outputPath">
    /// Optional override for the output file path. When <see langword="null"/> the default
    /// <c>Spec/openapi.json</c> path is used.
    /// </param>
    [Conditional("DEBUG")]
    [Obsolete("Set <BlueprintEmitOpenApi>true</BlueprintEmitOpenApi> in the app's project file instead, which writes Spec/openapi.json at build time. See docs/openapi-migration.md. OutputOpenApi will be removed in the next major version.")]
    public static void OutputOpenApi(this IHost host, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        var environment = host.Services.GetService<IHostEnvironment>();

        if (environment is not null && !environment.IsDevelopment())
        {
            return;
        }

        var json = host.Services.GetService<PreRenderedOpenApiDocumentHolder>()?.Document?.Json;

        if (json is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(outputPath ?? Path.Combine("Spec", "openapi.json"));

        // Only write when content has changed to avoid unnecessary file-system churn
        if (!BlueprintOpenApiDocumentMode.WriteIfChanged(json, fullPath))
        {
            return;
        }

        var logger = host.Services.GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(BlueprintOpenApiExportExtensions));
        logger?.LogInformation("Blueprint OpenAPI document written to {OpenApiPath}", fullPath);
    }
}
