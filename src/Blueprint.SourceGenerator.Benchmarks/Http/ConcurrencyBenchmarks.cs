using System;
using System.Diagnostics;
using System.Net.Http;
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
/// Measures p50/p95/p99 latency under sustained concurrent load to validate there are no
/// tail-latency issues from lock contention, GC pauses, or thread-pool starvation. Each
/// benchmark fires a fixed burst of parallel requests and reports the distribution.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ConcurrencyBenchmarks : IDisposable
{
    private const int RequestsPerBurst = 200;

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
    /// Fires 200 concurrent GET requests to a minimal query endpoint and returns the p99 latency
    /// in microseconds. This isolates Blueprint overhead from I/O and is sensitive to GC pauses
    /// or lock contention within the pipeline.
    /// </summary>
    /// <returns>The p99 latency in microseconds for the burst.</returns>
    [Benchmark(Baseline = true)]
    public async Task<double> ConcurrentGet_MinimalQuery_P99us()
    {
        var latencies = await this.FireBurstAsync("/api/benchmarks/fp/minimal", HttpMethod.Get).ConfigureAwait(false);
        return Percentile(latencies, 0.99);
    }

    /// <summary>
    /// Fires 200 concurrent POST requests with JSON body and returns the p99 latency.
    /// Stresses JSON deserialization, validation, and handler execution under concurrency.
    /// </summary>
    /// <returns>The p99 latency in microseconds for the burst.</returns>
    [Benchmark]
    public async Task<double> ConcurrentPost_FullStack_P99us()
    {
        var latencies = await this.FireBurstAsync(
            "/api/benchmarks/fp/fullstack",
            HttpMethod.Post,
            """{"tenantId":"tenant-42","name":"test-op","description":"A benchmark test"}""").ConfigureAwait(false);
        return Percentile(latencies, 0.99);
    }

    /// <summary>
    /// Fires 200 concurrent GET requests with validation and returns the p99 latency.
    /// Tests the validation hot path under contention.
    /// </summary>
    /// <returns>The p99 latency in microseconds for the burst.</returns>
    [Benchmark]
    public async Task<double> ConcurrentGet_ValidatedQuery_P99us()
    {
        var latencies = await this.FireBurstAsync(
            "/api/benchmarks/fp/validated?name=Alice&email=alice@example.com",
            HttpMethod.Get).ConfigureAwait(false);
        return Percentile(latencies, 0.99);
    }

    private async Task<double[]> FireBurstAsync(string url, HttpMethod method, string? jsonBody = null)
    {
        var latencies = new double[RequestsPerBurst];
        var tasks = new Task[RequestsPerBurst];

        for (var i = 0; i < RequestsPerBurst; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();

                using var request = new HttpRequestMessage(method, url);
                if (jsonBody != null)
                {
                    request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                }

                using var response = await this._client.SendAsync(request).ConfigureAwait(false);
                sw.Stop();

                latencies[index] = sw.Elapsed.TotalMicroseconds;
            });
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return latencies;
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        Array.Sort(sortedValues);
        var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Max(0, index)];
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public void Dispose()
    {
        this._client?.Dispose();
        this._host?.Dispose();
    }
}
