using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Blueprint.SourceGenerator.Benchmarks;

/// <summary>
/// Forces all benchmarks to run in-process with the emit toolchain. The source generator
/// dependency prevents BDN's default out-of-process build from succeeding, so we use
/// <see cref="InProcessEmitToolchain"/> to avoid the separate project build entirely.
/// </summary>
public sealed class InProcessBenchmarkConfig : ManualConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessBenchmarkConfig"/> class.
    /// </summary>
    public InProcessBenchmarkConfig()
    {
        this.AddJob(Job.MediumRun.WithToolchain(InProcessEmitToolchain.Instance));
    }
}
