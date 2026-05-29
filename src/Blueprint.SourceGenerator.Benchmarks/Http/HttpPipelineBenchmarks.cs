using System;
using System.Net;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Benchmarks.Http;

/// <summary>
/// Measures end-to-end HTTP request latency through the full ASP.NET Core pipeline including
/// Kestrel routing, Blueprint middleware, pipeline execution, and JSON response serialization.
/// This benchmark reflects real production behaviour more accurately than isolated pipeline benchmarks.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class HttpPipelineBenchmarks : IDisposable
{
    private IHost _host = null!;
    private System.Net.Http.HttpClient _client = null!;

    /// <summary>
    /// Creates the TestServer host with Blueprint endpoints mapped.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        this._host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureLogging(logging => logging.ClearProviders());
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddBlueprintApi<ValidationBenchmarksApp, ValidationBenchmarksAppExecutor>();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapBlueprintApi<ValidationBenchmarksApp, ValidationBenchmarksAppExecutor>("api/");
                    });
                });
            })
            .Build();

        this._host.Start();
        this._client = this._host.GetTestClient();
    }

    /// <summary>
    /// GET request to a minimal query endpoint — measures absolute minimum HTTP overhead.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> GetMinimalQueryAsync()
    {
        var response = await this._client.GetAsync("/api/benchmarks/fp/minimal").ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// GET request to a validated query endpoint with valid parameters.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> GetValidatedQueryValidAsync()
    {
        var response = await this._client.GetAsync("/api/benchmarks/fp/validated?name=Alice&email=alice@example.com").ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// GET request to a validated query endpoint with invalid parameters — measures validation failure path.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> GetValidatedQueryInvalidAsync()
    {
        var response = await this._client.GetAsync("/api/benchmarks/fp/validated").ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// POST request to a full-stack command endpoint with JSON body.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> PostFullStackCommandValidAsync()
    {
        var content = new System.Net.Http.StringContent(
            """{"tenantId":"tenant-42","name":"test-op","description":"A benchmark test"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await this._client.PostAsync("/api/benchmarks/fp/fullstack", content).ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public void Dispose()
    {
        this._client?.Dispose();
        this._host?.Dispose();
    }
}
