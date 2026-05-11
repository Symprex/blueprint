# Blueprint Pipeline Benchmark Results

## Environment

- **.NET Version:** 10.0
- **OS:** Windows 11
- **Hardware:** Development workstation
- **Date:** 2025-07-14 (updated 2025-07-15)
- **BenchmarkDotNet:** InProcessEmitToolchain, MediumRun (15 iterations, 2 warmups, 10 invocations)

## Full Pipeline Benchmarks

These benchmarks measure end-to-end pipeline execution through the generated executor,
including validation, handler invocation, and result wrapping. They exercise the same
code path that runs in production HTTP requests (minus HTTP I/O and serialization).

| Benchmark | Mean | Error | StdDev | Ratio | Gen0 | Allocated | Alloc Ratio |
|-----------|-----:|------:|-------:|------:|-----:|----------:|------------:|
| MinimalQuery (baseline) | 2.06 µs | 0.175 µs | 0.116 µs | 1.00 | 0.2022 | 2.51 KB | 1.00 |
| ValidatedQuery (invalid input) | 8.07 µs | 0.171 µs | 0.090 µs | 3.92 | 0.4120 | 5.12 KB | 2.04 |
| FullStackCommand (valid input) | 8.98 µs | 0.326 µs | 0.216 µs | 4.37 | 0.4578 | 5.73 KB | 2.28 |
| FullStackCommand (validation failure) | 9.02 µs | 0.283 µs | 0.187 µs | 4.39 | 0.4578 | 5.73 KB | 2.28 |
| ValidatedQuery (valid input) | 10.27 µs | 3.812 µs | 2.269 µs | 4.99 | 0.4120 | 5.12 KB | 2.04 |

### Analysis

**Baseline (MinimalQuery):** ~2 µs / 2.51 KB
- This is the absolute minimum pipeline overhead — no validation, just handler invocation
- The 2.51 KB includes `ApiOperationContext` allocation and the operation instance
- **This is the floor.** Any production operation will add to this baseline

**Validation cost:** ~6-8 µs / 2.6 KB additional
- Validation adds ~6-8 µs latency and ~2.6 KB allocation over the baseline
- Both valid and invalid paths have similar cost — the generated validation code runs
  the same checks either way, just with different outcomes
- The 2.6 KB is primarily from `ValidationFailedOperationResult` and error message strings

**Command overhead:** ~1-2 µs / 0.6 KB additional (over validated query)
- Commands have slightly more pipeline stages (e.g., success guards, result wrapping)
- The additional allocation is from command-specific result types

### Comparison Context

For reference, typical HTTP request handling costs:
- Kestrel request parsing: ~5-10 µs
- JSON deserialization (small body): ~2-5 µs
- JSON serialization (small body): ~1-3 µs
- Network round-trip (local): ~100-500 µs

The pipeline execution (2-10 µs) is **well within the noise floor** of HTTP handling.
Even the heaviest validated command pipeline (~10 µs) is faster than typical JSON deserialization.

### Optimizations Applied

1. **Validation attribute caching (commit 789225f)** — All DataAnnotations validation
   attributes (Required, MaxLength, MinLength, Range, RegularExpression, StringLength,
   Length, EmailAddress, Phone, Url, Compare) are hoisted to `private static readonly`
   fields in generated pipeline code. This eliminates per-request allocations on the
   validation failure path. Previously only Blueprint-custom attributes (Password,
   ResourceKey, UkPostcode) were cached.

2. **Static ActivityTagsCollection** — Audit and observability tags are pre-allocated
   as static fields per operation type, avoiding per-request dictionary creation.

### Remaining Optimization Opportunities

1. **ApiOperationContext allocation** — Could be pooled or made a struct, but the
   savings (~300 bytes) are minimal compared to HTTP overhead
2. **ValidationFailedOperationResult** — Could potentially use pooled error lists,
   but failure path is not the hot path

**Conclusion:** Pipeline performance is already excellent. Further optimization effort
should focus on HTTP layer (serialization, routing) rather than pipeline execution.
