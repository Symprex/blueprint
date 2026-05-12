# Blueprint Benchmarks

Micro-benchmarks for the source-generated Blueprint pipeline, covering the critical hot paths
that execute on every API request.

## Running

All benchmarks use `InProcessEmitToolchain` (the source generator is referenced as an analyzer
and cannot be resolved by BenchmarkDotNet's default out-of-process harness).

```bash
# Run all benchmarks
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --filter '*'

# Run a specific benchmark class
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --filter '*ExecutorDispatch*'

# List available benchmarks
dotnet run -c Release --project src/Blueprint.SourceGenerator.Benchmarks -- --list flat
```

## Benchmark categories

| Class | What it measures |
|-------|-----------------|
| `ExecutorDispatchBenchmarks` | Pipeline lookup (~13 ns) and full executor dispatch (~65 ns) per operation |
| `ValidationPipelineBenchmarks` | Generated fast-path validation vs runtime fallback for all supported attributes |
| `BlueprintAppBenchmarks` | App registration scaling (10–500 operations) |
| `BlueprintEndpointMappingBenchmarks` | ASP.NET Core endpoint mapping scaling (10–500 operations) |
| `PopulationPipelineBenchmarks` | Property population from route, query, headers, and JSON body |
| `FullPipelineBenchmarks` | End-to-end pipeline execution: validation → handler → result |
| `SourceGeneratorCompilationBenchmarks` | Source generator compile-time cost across 10–250 operation apps |

## Synthetic fixture generator

`SyntheticBlueprintFixtureGenerator` creates realistic N-operation apps at runtime for scaling
benchmarks. Each synthetic operation has a unique route, handler, and descriptor so the
benchmarks exercise the same code paths as a production deployment.

## Key results

See `docs/observability-and-dev-experience.md` for the full results table. Summary:

- **Pipeline lookup**: ~13 ns, zero allocations, O(1) via `FrozenDictionary`
- **Pipeline execution**: ~52–67 ns, 216 B allocation
- **Validation fast-path**: 2.3–4.5 μs (40–60% faster than runtime fallback)
- **App registration (500 ops)**: ~120 μs, 219 KB
- **Endpoint mapping (100 ops)**: ~628 μs (under 1 ms for S365-sized API surface)
