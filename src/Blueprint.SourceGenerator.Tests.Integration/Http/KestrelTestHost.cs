using System.Collections.Concurrent;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Tests.Integration.Http;

/// <summary>The integration app, mapped as a consumer maps it, served by Kestrel on a free local port.</summary>
/// <remarks>
/// Kestrel enforces HTTP rules the in-memory endpoint harness and TestServer do not (no body on a 204, for one), and a
/// broken response can still reach the client as a clean status once the server has thrown. So the host records every
/// exception the server raises or logs while handling a request, for a test to assert there were none.
/// </remarks>
internal sealed class KestrelTestHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private KestrelTestHost(WebApplication app, System.Net.Http.HttpClient client, ConcurrentQueue<Exception> serverErrors)
    {
        this._app = app;
        this.Client = client;
        this.ServerErrors = serverErrors;
    }

    /// <summary>Gets a client addressed at the host.</summary>
    public System.Net.Http.HttpClient Client { get; }

    /// <summary>Gets every exception the server raised or logged while handling a request.</summary>
    public ConcurrentQueue<Exception> ServerErrors { get; }

    /// <summary>Starts the integration app on Kestrel, on a free local port.</summary>
    /// <returns>The started host.</returns>
    public static async Task<KestrelTestHost> StartAsync()
    {
        var serverErrors = new ConcurrentQueue<Exception>();
        var builder = WebApplication.CreateSlimBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ExceptionRecordingLoggerProvider(serverErrors));
        builder.Services
            .AddSingleton<IntegrationTestBlueprintApp>()
            .AddSingleton<IntegrationTestBlueprintAppExecutor>()

            // As AddBlueprintApi registers it, so that a pipeline can execute a nested operation such as a self query.
            .AddSingleton<Blueprint.BlueprintAppExecutor>(static sp => sp.GetRequiredService<IntegrationTestBlueprintAppExecutor>());

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception)
            {
                serverErrors.Enqueue(exception);
                throw;
            }
        });

        app.MapBlueprintApi<IntegrationTestBlueprintApp, IntegrationTestBlueprintAppExecutor>("api/");

        await app.StartAsync();

        var client = new System.Net.Http.HttpClient { BaseAddress = new Uri(app.Urls.First().TrimEnd('/') + "/") };

        return new KestrelTestHost(app, client, serverErrors);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        await this._app.StopAsync();
        await this._app.DisposeAsync();
    }

    private sealed class ExceptionRecordingLoggerProvider(ConcurrentQueue<Exception> exceptions) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ExceptionRecordingLogger(exceptions);
        }

        public void Dispose()
        {
        }
    }

    private sealed class ExceptionRecordingLogger(ConcurrentQueue<Exception> exceptions) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                exceptions.Enqueue(exception ?? new InvalidOperationException(formatter(state, exception)));
            }
        }
    }
}
