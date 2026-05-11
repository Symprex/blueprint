# Performance Profiling Guide

This document covers profiling Blueprint pipelines and the source generator compiler using command-line tools. Use these techniques to identify bottlenecks, measure allocations, and compare performance before and after migration.

---

## Tools Overview

| Tool | Purpose | Install |
|---|---|---|
| `dotnet-trace` | CPU profiling, event tracing | `dotnet tool install -g dotnet-trace` |
| `dotnet-counters` | Live runtime metrics | `dotnet tool install -g dotnet-counters` |
| `dotnet-gcdump` | Heap snapshots, GC analysis | `dotnet tool install -g dotnet-gcdump` |
| `dotnet-dump` | Process dumps, SOS analysis | `dotnet tool install -g dotnet-dump` |
| BenchmarkDotNet | Micro-benchmarks | NuGet package (already in project) |
| NBomber | Load testing | NuGet package |

---

## Pipeline Profiling

### CPU Profiling with dotnet-trace

Profile a running Blueprint API to identify hot paths:

```bash
# Start the API
dotnet run --project src/Blueprint.SourceGenerator.Sample

# In another terminal, find the process
dotnet-trace ps

# Collect a trace (30 seconds)
dotnet-trace collect --process-id <PID> --duration 00:00:30

# Or collect with specific providers for more detail
dotnet-trace collect --process-id <PID> \
    --providers "Microsoft-DotNETCore-SampleProfiler,Microsoft-Windows-DotNETRuntime:0x1F000080018:5" \
    --duration 00:00:30

# Convert to SpeedScope format for browser viewing
dotnet-trace convert trace.nettrace --format speedscope
# Open: https://www.speedscope.app/ and load the .speedscope.json file
```

**What to look for:**
- Time spent in validation vs handler vs serialization
- Allocation hotspots in pipeline stages
- Lock contention in concurrent scenarios
- Excessive string allocations in URL building / link generation

### Live Metrics with dotnet-counters

Monitor runtime metrics during load testing:

```bash
# Watch key metrics
dotnet-counters monitor --process-id <PID> \
    --counters "System.Runtime[cpu-usage,working-set,gc-heap-size,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,alloc-rate,threadpool-queue-length]"

# Watch ASP.NET Core specific metrics
dotnet-counters monitor --process-id <PID> \
    --counters "Microsoft.AspNetCore.Hosting[requests-per-second,total-requests,current-requests,failed-requests]"

# Export to CSV for analysis
dotnet-counters collect --process-id <PID> --format csv --output metrics.csv --duration 00:01:00
```

**Key metrics to track during migration:**

| Metric | Healthy Range | Red Flag |
|---|---|---|
| `cpu-usage` | < 70% under load | > 90% sustained |
| `alloc-rate` | < 100 MB/s | > 500 MB/s |
| `gen-2-gc-count` | < 1/min | > 10/min |
| `gc-heap-size` | Stable | Growing monotonically |
| `threadpool-queue-length` | < 10 | > 100 |
| `requests-per-second` | Comparable to baseline | > 20% regression |

### Heap Analysis with dotnet-gcdump

Capture heap snapshots to find memory leaks or excessive allocations:

```bash
# Capture a GC dump
dotnet-gcdump collect --process-id <PID> --output heap.gcdump

# Analyze in Visual Studio (open the .gcdump file)
# Or use dotnet-dump for CLI analysis:
dotnet-dump analyze heap.gcdump
> dumpheap -stat
> dumpheap -type Blueprint
> gcroot <address>
```

---

## Source Generator Compiler Profiling

### Measuring Generator Execution Time

```bash
# Build with detailed timing
dotnet build src/Blueprint.SourceGenerator.Sample/Blueprint.SourceGenerator.Sample.csproj \
    -v diag 2>&1 | Select-String "GenerateSource|SourceGenerator"

# Build with binary log for detailed analysis
dotnet build src/Blueprint.SourceGenerator.Sample/Blueprint.SourceGenerator.Sample.csproj \
    -bl:generator-build.binlog

# Analyze with MSBuild Structured Log Viewer
# Download from: https://msbuildlog.com/
```

### Generator Benchmarks

The existing `Blueprint.SourceGenerator.Benchmarks` project measures compiler performance:

```bash
# Run all benchmarks
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --filter "*"

# Run specific benchmark
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --filter "*EndpointMapping*"

# Export results
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- \
    --filter "*" --exporters json csv
```

---

## Comparing Old vs New Blueprint Performance

### Before Migration (S365 Baseline)

Capture baseline metrics from the current S365 service:

```bash
# 1. Start the S365 service
cd D:\Projects\Signature365
dotnet run --project src/Services/Templates/Symprex.Signature365.Templates.Api

# 2. Warm up
for ($i = 0; $i -lt 100; $i++) { Invoke-RestMethod "http://localhost:5000/api/templates" -Headers @{Authorization="Bearer $token"} | Out-Null }

# 3. Capture baseline trace
dotnet-trace collect --process-id <PID> --duration 00:00:30

# 4. Capture baseline counters during steady load
dotnet-counters collect --process-id <PID> --format csv --output baseline-metrics.csv --duration 00:01:00
```

### After Migration (Blueprint Source-Gen)

Repeat the same process after migrating the service:

```bash
# Same steps with the migrated service
dotnet run --project src/Services/Templates/Symprex.Signature365.Templates.Api

# Compare traces side-by-side in SpeedScope
# Compare CSV metrics for regressions
```

### Automated Comparison Script

```powershell
# compare-perf.ps1 — Run NBomber against both old and new, compare results
param(
    [string]$OldBaseUrl = "http://localhost:5000",
    [string]$NewBaseUrl = "http://localhost:5001",
    [int]$DurationSeconds = 60
)

Write-Host "Baseline: $OldBaseUrl"
Write-Host "Candidate: $NewBaseUrl"
Write-Host "Duration: ${DurationSeconds}s"

# Run NBomber scenarios against both endpoints
# Results saved to reports/ folder for comparison
dotnet run --project src/Blueprint.LoadTests -- `
    --baseline-url $OldBaseUrl `
    --candidate-url $NewBaseUrl `
    --duration $DurationSeconds
```

---

## BenchmarkDotNet: Runtime Benchmarks

### Pipeline Execution Benchmarks

Measure individual pipeline stages:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class PipelineExecutionBenchmarks
{
    private BlueprintTestHost<SampleApp, SampleAppExecutor> _host = null!;
    private GetTemplateQuery _query = null!;

    [GlobalSetup]
    public void Setup()
    {
        _host = new BlueprintTestHostBuilder<SampleApp, SampleAppExecutor>()
            .ConfigureServices(s => s.AddSingleton<IUnitOfWork, NoOpUnitOfWork>())
            .Build();
        _query = new GetTemplateQuery { Id = "benchmark-1" };
    }

    [Benchmark(Baseline = true)]
    public async Task<object> SimpleQuery()
    {
        return await _host.ExecuteAsync(_query, ctx => ctx.SkipAuthorisation());
    }

    [Benchmark]
    public async Task<object> QueryWithAuth()
    {
        return await _host.ExecuteAsync(_query, ctx => ctx
            .WithUser("user-1")
            .WithPermissions("Template.Read"));
    }

    [Benchmark]
    public async Task<object> CommandWithValidation()
    {
        return await _host.ExecuteAsync(
            new CreateTemplateCommand { Name = "" },  // triggers validation
            ctx => ctx.SkipAuthorisation());
    }
}
```

### Serialization Benchmarks

```csharp
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private TemplateResource _resource = null!;
    private byte[] _jsonBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _resource = new TemplateResource { Id = "1", Name = "Test", CreatedAt = DateTime.UtcNow };
        _jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_resource, AppJsonContext.Default.TemplateResource);
    }

    [Benchmark]
    public byte[] Serialize() =>
        JsonSerializer.SerializeToUtf8Bytes(_resource, AppJsonContext.Default.TemplateResource);

    [Benchmark]
    public TemplateResource? Deserialize() =>
        JsonSerializer.Deserialize(_jsonBytes, AppJsonContext.Default.TemplateResource);
}
```

### Link Generation Benchmarks

```csharp
[MemoryDiagnoser]
public class LinkGenerationBenchmarks
{
    // Benchmark link generation for resources with many links
    // Compare: reflection-based (old) vs source-generated (new)
}
```

### Running Benchmarks

```bash
# Run all runtime benchmarks
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --filter "*"

# Run with specific exporters
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- \
    --filter "*Pipeline*" --exporters json html markdown

# Results appear in BenchmarkDotNet.Artifacts/results/
```

---

## NBomber Load Tests

### Scenario Definitions

```csharp
// In Blueprint.LoadTests project
public static class LoadTestScenarios
{
    public static ScenarioProps SimpleQuery(string baseUrl, string token) =>
        Scenario.Create("simple_query", async context =>
        {
            var request = Http.CreateRequest("GET", $"{baseUrl}/api/templates")
                .WithHeader("Authorization", $"Bearer {token}");

            var response = await Http.Send(request);
            return response;
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1)));

    public static ScenarioProps CommandWithValidation(string baseUrl, string token) =>
        Scenario.Create("command_validation", async context =>
        {
            var request = Http.CreateRequest("POST", $"{baseUrl}/api/templates")
                .WithHeader("Authorization", $"Bearer {token}")
                .WithJsonBody(new { });  // empty = validation failure

            var response = await Http.Send(request);
            return response;
        })
        .WithLoadSimulations(
            Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(1)));
}
```

### Running Load Tests

```bash
# Run load tests
dotnet run --project src/Blueprint.LoadTests -- \
    --target-url http://localhost:5000/api \
    --scenario simple-query \
    --duration 60 \
    --rate 100

# Results exported to reports/ with latency percentiles, throughput, error rates
```

---

## Key Metrics to Track

### Per-Service Migration Checklist

Before declaring a migration "performance equivalent":

| Metric | Measurement | Acceptable Delta |
|---|---|---|
| p50 latency | NBomber report | < 10% increase |
| p99 latency | NBomber report | < 20% increase |
| Throughput (req/s) | NBomber report | < 10% decrease |
| Allocation rate | dotnet-counters | < 20% increase |
| Gen 2 GC frequency | dotnet-counters | No increase |
| Startup time | `time dotnet run` | < 2x increase |
| Memory (working set) | dotnet-counters | < 30% increase |
| CPU under load | dotnet-counters | Comparable |

### Recording Results

After each service migration, record results in `docs/performance/`:

```
docs/performance/
├── baseline/
│   ├── templates-service.md
│   ├── analytics-service.md
│   └── ...
├── migrated/
│   ├── templates-service.md
│   └── ...
└── comparison-summary.md
```

Each report should include:
- NBomber summary (p50, p95, p99, throughput, error rate)
- dotnet-counters CSV snapshot
- Any flame graph screenshots from notable findings
- Go/no-go recommendation
