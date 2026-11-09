using System.Diagnostics;
using Blueprint.Model;
using Microsoft.Extensions.Options;

namespace Blueprint.Runtime.Http;

/// <summary>
/// The document-only mode the <c>BlueprintEmitOpenApi</c> build target runs an app in, to write its OpenAPI document at
/// build time without starting the app: as soon as the app's host is built, the document is written, only if it has
/// changed, and the process exits.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddBlueprintApi</c> switches the mode on when <see cref="OutputPathVariable" /> names the file to write. The app's
/// <c>Main</c> runs as far as building its host, which registers its services. Nothing after that runs: no hosted service
/// starts, no middleware is set up, and no code the app runs between building its host and running it is reached. So an
/// app needs no checks for whether it is only writing its document.
/// </para>
/// <para>
/// The host announces that it has been built through the <c>Microsoft.Extensions.Hosting</c> diagnostic listener, as
/// every host built by <c>Host.CreateDefaultBuilder</c>, <c>Host.CreateApplicationBuilder</c> or
/// <c>WebApplication.CreateBuilder</c> does; EF Core's design-time tools stop an app at the same point.
/// </para>
/// <para>
/// <c>MapBlueprintApi</c> is never reached, so the document is rendered from what <c>AddBlueprintApi</c> declared with
/// <c>UseBasePath</c> and the <c>ConfigureOpenApi</c> methods, which is also what <c>MapBlueprintApi</c> serves.
/// </para>
/// </remarks>
internal static class BlueprintOpenApiDocumentMode
{
    /// <summary>
    /// The environment variable the build target sets to the full path of the file to write the document to, which runs
    /// the app in document-only mode.
    /// </summary>
    internal const string OutputPathVariable = "BLUEPRINT_OPENAPI_OUTPUT_PATH";

    /// <summary>
    /// The environment variable the build target sets to the full path of a file the app writes once the document has been
    /// written or found unchanged. The target fails without it, because an app that returns before building its host
    /// exits as successfully as one that wrote the document.
    /// </summary>
    internal const string CompletionPathVariable = "BLUEPRINT_OPENAPI_COMPLETION_PATH";

    private const string HostingListenerName = "Microsoft.Extensions.Hosting";

    private const string HostBuiltEventName = "HostBuilt";

    /// <summary>
    /// Switches the app on to document-only mode when <see cref="OutputPathVariable" /> is set.
    /// </summary>
    /// <typeparam name="TApp">The app whose document is written.</typeparam>
    /// <param name="services">The app's services.</param>
    internal static void EnableIfRequested<TApp>(IServiceCollection services)
        where TApp : BlueprintApp
    {
        var outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);

        if (!string.IsNullOrEmpty(outputPath))
        {
            Enable<TApp>(services, outputPath, Environment.GetEnvironmentVariable(CompletionPathVariable), ExitProcess);
        }
    }

    /// <summary>
    /// Switches the app on to document-only mode, writing to <paramref name="outputPath" /> once the host built from
    /// <paramref name="services" /> is built.
    /// </summary>
    /// <typeparam name="TApp">The app whose document is written.</typeparam>
    /// <param name="services">The app's services.</param>
    /// <param name="outputPath">The file to write the document to.</param>
    /// <param name="completionPath">
    /// The file to write once the document has been written or found unchanged, which the build target requires before it
    /// accepts the run; <see langword="null" /> writes none.
    /// </param>
    /// <param name="finish">
    /// Ends the app once the host is built, given the exception that stopped the document being written, if one did. It
    /// must not return: the app would carry on from its <c>Build()</c> call.
    /// </param>
    internal static void Enable<TApp>(IServiceCollection services, string outputPath, string? completionPath, Action<IHost, Exception?> finish)
        where TApp : BlueprintApp
    {
        var fullPath = Path.GetFullPath(outputPath);
        var fullCompletionPath = string.IsNullOrEmpty(completionPath) ? null : Path.GetFullPath(completionPath);

        // Marks the services as this app's, so that another host built in the process is left alone.
        var marker = new DocumentModeMarker();
        services.AddSingleton(marker);
        services.AddSingleton<IHostedService>(new UnseenBuildGuard(fullPath));

        var observer = new HostBuiltObserver(marker, host =>
        {
            Exception? failure = null;

            try
            {
                WriteDocument<TApp>(host.Services, fullPath, fullCompletionPath);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            finish(host, failure);
        });

        observer.Subscription = DiagnosticListener.AllListeners.Subscribe(observer);
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

    private static void WriteDocument<TApp>(IServiceProvider services, string fullPath, string? completionPath)
        where TApp : BlueprintApp
    {
        var mapping = services.GetKeyedService<BlueprintApiMapping>(typeof(TApp));

        if (mapping?.BasePath is null)
        {
            throw new InvalidOperationException(
                $"Blueprint was asked to write the OpenAPI document to {fullPath}, but AddBlueprintApi declared no base path. " +
                "The app is stopped as soon as its host is built, before MapBlueprintApi is reached: declare the base path with " +
                "UseBasePath and the OpenAPI description with the ConfigureOpenApi methods in AddBlueprintApi, and call " +
                "MapBlueprintApi without arguments.");
        }

        var (_, document, _) = OpenApiDocumentEndpoint.CreateConfigured(
            services.GetRequiredService<TApp>(),
            services,
            services.GetService<IOptions<BlueprintHttpOptions>>()?.Value ?? new BlueprintHttpOptions(),
            BlueprintApiMapping.NormaliseBasePath(mapping.BasePath),
            mapping.ConfigureOpenApi,
            mapping.ConfigureOpenApiSchemas,
            mapping.ConfigureOpenApiOperations,
            renderOnFirstRequest: false);

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Blueprint.OpenApi");

        if (WriteIfChanged(document.Value.Json, fullPath))
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
    }

    /// <summary>
    /// Ends the process the build target started: with 0 once the document is written, or with 1 and the failure on the
    /// error output. The process is exited rather than an exception thrown, which an app's own handler around its
    /// <c>Main</c> could catch and carry on from.
    /// </summary>
    private static void ExitProcess(IHost host, Exception? failure)
    {
        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
        }

        try
        {
            // Flushes the app's logging; nothing was started, so there is nothing else to stop.
            host.Dispose();
        }
        catch (Exception exception)
        {
            // The document is already written, or already failed; this is only reported.
            Console.Error.WriteLine(exception);
        }

        Environment.Exit(failure is null ? 0 : 1);
    }

    /// <summary>
    /// Registered with the app's services to tell its host from any other the process builds.
    /// </summary>
    private sealed class DocumentModeMarker
    {
    }

    /// <summary>
    /// Watches every host the process builds and acts on the one built from the app's services.
    /// </summary>
    private sealed class HostBuiltObserver(DocumentModeMarker marker, Action<IHost> onBuilt)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private int _handled;

        public IDisposable? Subscription { get; set; }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == HostingListenerName)
            {
                value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != HostBuiltEventName
                || value.Value is not IHost host
                || !ReferenceEquals(host.Services.GetService<DocumentModeMarker>(), marker)
                || Interlocked.Exchange(ref this._handled, 1) != 0)
            {
                return;
            }

            this.Subscription?.Dispose();

            onBuilt(host);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    /// <summary>
    /// Fails the app's start-up if its host starts, which it does only when it was built without announcing it, so that
    /// the build fails at once rather than running the app until the build target's timeout ends it.
    /// </summary>
    private sealed class UnseenBuildGuard(string fullPath) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(
                $"The BlueprintEmitOpenApi build target could not write the OpenAPI document to {fullPath}: the app's host started " +
                "without Blueprint seeing it built. Build the host with Host.CreateDefaultBuilder, Host.CreateApplicationBuilder " +
                "or WebApplication.CreateBuilder.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
