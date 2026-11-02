using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace Blueprint.Runtime.Http;

/// <summary>
/// The document-only mode the <c>BlueprintEmitOpenApi</c> build target starts an app in, to write its OpenAPI document
/// at build time. The app starts as it always does, but binds no port: once it has started, the document its
/// <c>MapBlueprintApi</c> call mapped is written, only if it has changed, and the app stops itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddBlueprintApi</c> switches the mode on when <see cref="OutputPathVariable" /> names the file to write. Everything
/// else the app starts still starts, its hosted services included; an app whose start-up needs what a build machine does
/// not have, such as a database, checks <see cref="IsActive" /> and skips it.
/// </para>
/// <para>
/// One document is written: the one the last <c>MapBlueprintApi</c> call mapped.
/// </para>
/// </remarks>
public static class BlueprintOpenApiDocumentMode
{
    /// <summary>
    /// The environment variable the build target sets to the full path of the file to write the document to, which starts
    /// the app in document-only mode.
    /// </summary>
    public const string OutputPathVariable = "BLUEPRINT_OPENAPI_OUTPUT_PATH";

    /// <summary>
    /// The environment variable the build target sets to the full path of a file the app writes once the document has been
    /// written or found unchanged. The target fails without it, because an app that returns before starting its host
    /// exits as successfully as one that wrote the document.
    /// </summary>
    internal const string CompletionPathVariable = "BLUEPRINT_OPENAPI_COMPLETION_PATH";

    /// <summary>
    /// Gets a value indicating whether this process was started only to write the app's OpenAPI document, so that start-up
    /// work the document does not need, such as connecting to a database, can be skipped.
    /// </summary>
    public static bool IsActive => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OutputPathVariable));

    /// <summary>
    /// Switches the app on to document-only mode when <see cref="OutputPathVariable" /> is set.
    /// </summary>
    /// <param name="services">The app's services.</param>
    internal static void EnableIfRequested(IServiceCollection services)
    {
        var outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);

        if (!string.IsNullOrEmpty(outputPath))
        {
            Enable(services, outputPath, Environment.GetEnvironmentVariable(CompletionPathVariable));
        }
    }

    /// <summary>
    /// Switches the app on to document-only mode, writing to <paramref name="outputPath" />.
    /// </summary>
    /// <remarks>
    /// The server is replaced by one that never listens. A server registered after this call (such as Kestrel registered
    /// by a web host configured after <c>AddBlueprintApi</c>) wins, and the app then listens briefly before it stops.
    /// </remarks>
    /// <param name="services">The app's services.</param>
    /// <param name="outputPath">The file to write the document to.</param>
    /// <param name="completionPath">
    /// The file to write once the document has been written or found unchanged, which the build target requires before it
    /// accepts the run; <see langword="null" /> writes none.
    /// </param>
    internal static void Enable(IServiceCollection services, string outputPath, string? completionPath = null)
    {
        services.AddSingleton<IServer, DocumentOnlyServer>();
        services.AddSingleton<IHostedService>(sp => new DocumentWriter(
            sp.GetRequiredService<PreRenderedOpenApiDocumentHolder>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger("Blueprint.OpenApi"),
            Path.GetFullPath(outputPath),
            string.IsNullOrEmpty(completionPath) ? null : Path.GetFullPath(completionPath)));
    }

    /// <summary>
    /// Writes <paramref name="json" /> to <paramref name="fullPath" /> unless the file already holds it, so that a build
    /// that changes nothing in the API leaves the file, and its timestamp, untouched. Line endings are not compared: a
    /// document checked out with other line endings than it was written with is the same document.
    /// </summary>
    /// <param name="json">The document.</param>
    /// <param name="fullPath">The full path of the file to write.</param>
    /// <returns><see langword="true" /> if the file was written.</returns>
    internal static bool WriteIfChanged(string json, string fullPath)
    {
        if (File.Exists(fullPath)
            && string.Equals(NormaliseLineEndings(File.ReadAllText(fullPath)), NormaliseLineEndings(json), StringComparison.Ordinal))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, json);

        return true;
    }

    private static string NormaliseLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes the document once every hosted service has started, which is after the web host has run the app's
    /// <c>Configure</c> (where a <c>Startup</c> class maps the API), and then stops the app. A failure is thrown from
    /// start-up, so the app exits with an error and the build fails.
    /// </summary>
    /// <remarks>
    /// The app is stopped once it has started, not from <see cref="StartedAsync" />: stopping it there would cancel the
    /// start token that every later lifecycle service's <c>StartedAsync</c> is handed, and fail a start-up that had
    /// succeeded.
    /// </remarks>
    private sealed class DocumentWriter(
        PreRenderedOpenApiDocumentHolder holder,
        IHostApplicationLifetime lifetime,
        ILogger? logger,
        string fullPath,
        string? completionPath) : IHostedLifecycleService
    {
        public Task StartedAsync(CancellationToken cancellationToken)
        {
            var document = holder.Document ?? throw new InvalidOperationException(
                $"Blueprint was asked to write the OpenAPI document to {fullPath}, but the app mapped no API to document: " +
                "call MapBlueprintApi as the app starts.");

            if (WriteIfChanged(document.Json, fullPath))
            {
                logger?.LogInformation("Blueprint OpenAPI document written to {OpenApiPath}", fullPath);
            }
            else
            {
                logger?.LogInformation("Blueprint OpenAPI document at {OpenApiPath} is unchanged", fullPath);
            }

            if (completionPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(completionPath)!);
                File.WriteAllText(completionPath, fullPath);
            }

            lifetime.ApplicationStarted.Register(lifetime.StopApplication);

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A server that never listens, so writing the document binds no port and cannot clash with one in use.
    /// </summary>
    private sealed class DocumentOnlyServer : IServer
    {
        public IFeatureCollection Features { get; } = CreateFeatures();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        private static FeatureCollection CreateFeatures()
        {
            var features = new FeatureCollection();
            features.Set<IServerAddressesFeature>(new ServerAddressesFeature());

            return features;
        }
    }
}
