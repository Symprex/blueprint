using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Blueprint.SourceGenerator.Benchmarks.Http;

/// <summary>
/// Compares Blueprint pipeline overhead against a raw ASP.NET Core minimal API endpoint
/// to quantify the exact cost of the framework's CQRS dispatch, validation, and result translation.
/// Both endpoints return identical JSON payloads so the comparison isolates framework overhead only.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FrameworkOverheadBenchmarks : IDisposable
{
    private IHost _blueprintHost = null!;
    private IHost _rawHost = null!;
    private System.Net.Http.HttpClient _blueprintClient = null!;
    private System.Net.Http.HttpClient _rawClient = null!;

    /// <summary>
    /// Creates two TestServer hosts: one with Blueprint, one with raw minimal API.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        // Blueprint host
        this._blueprintHost = new HostBuilder()
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

        this._blueprintHost.Start();
        this._blueprintClient = this._blueprintHost.GetTestClient();

        // Raw minimal API host — same response shape, no framework overhead
        this._rawHost = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureLogging(logging => logging.ClearProviders());
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.ConfigureHttpJsonOptions(options =>
                    {
                        options.SerializerOptions.TypeInfoResolverChain.Insert(0, RawBenchmarkJsonContext.Default);
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/raw/minimal", () => Results.Ok("hello"));
                        endpoints.MapPost("/api/raw/fullstack", (RawFullStackRequest request) =>
                        {
                            // Simulate the same work as Blueprint's FullStack benchmark handler
                            if (string.IsNullOrEmpty(request.Name))
                            {
                                return Results.BadRequest("Name is required");
                            }

                            return Results.Ok($"Created: {request.Name} for tenant {request.TenantId}");
                        });
                    });
                });
            })
            .Build();

        this._rawHost.Start();
        this._rawClient = this._rawHost.GetTestClient();
    }

    /// <summary>
    /// Raw ASP.NET Core GET — establishes the baseline without any framework overhead.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> RawMinimalApi_GetAsync()
    {
        var response = await this._rawClient.GetAsync("/api/raw/minimal").ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// Blueprint GET — the framework overhead above raw ASP.NET Core is the difference.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> Blueprint_GetMinimalAsync()
    {
        var response = await this._blueprintClient.GetAsync("/api/benchmarks/fp/minimal").ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// Raw ASP.NET Core POST with JSON deserialization — baseline for command-style operations.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> RawMinimalApi_PostAsync()
    {
        var content = new StringContent(
            """{"tenantId":"tenant-42","name":"test-op","description":"A benchmark test"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await this._rawClient.PostAsync("/api/raw/fullstack", content).ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <summary>
    /// Blueprint POST — the framework overhead above raw ASP.NET Core is the difference.
    /// </summary>
    /// <returns>The HTTP status code from the response.</returns>
    [Benchmark]
    public async Task<HttpStatusCode> Blueprint_PostFullStackAsync()
    {
        var content = new StringContent(
            """{"tenantId":"tenant-42","name":"test-op","description":"A benchmark test"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await this._blueprintClient.PostAsync("/api/benchmarks/fp/fullstack", content).ConfigureAwait(false);
        return response.StatusCode;
    }

    /// <inheritdoc />
    [GlobalCleanup]
    public void Dispose()
    {
        this._blueprintClient?.Dispose();
        this._blueprintHost?.Dispose();
        this._rawClient?.Dispose();
        this._rawHost?.Dispose();
    }
}

/// <summary>
/// Request model for the raw ASP.NET Core benchmark endpoint.
/// </summary>
internal sealed class RawFullStackRequest
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Source-generated JSON context for the raw benchmark endpoint.
/// </summary>
[JsonSerializable(typeof(RawFullStackRequest))]
[JsonSerializable(typeof(string))]
internal partial class RawBenchmarkJsonContext : JsonSerializerContext;
