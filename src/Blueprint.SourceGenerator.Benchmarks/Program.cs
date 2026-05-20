using System;
using System.Threading;
using BenchmarkDotNet.Running;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Benchmarks.FullPipeline;
using Blueprint.SourceGenerator.Benchmarks.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length > 0 && args[0] == "profile")
{
    // Profiling mode: runs a tight loop for 15 seconds so a profiler can attach
    // and capture representative CPU/memory data.
    // Usage: dnx asynkron-profiler --cpu -- dotnet run -c Release -- profile
    var serviceProvider = new ServiceCollection().BuildServiceProvider();
    var app = new ValidationBenchmarksApp();
    var executor = new ValidationBenchmarksAppExecutor(
        NullLoggerFactory.Instance,
        app,
        serviceProvider);

    var minimalDescriptor = FpMinimalBenchmarkQuery.Configure();
    var fullStackDescriptor = FpFullStackBenchmarkCommand.Configure();
    var pipeline = executor.GetPipeline(typeof(FpMinimalBenchmarkQuery));
    var fullStackPipeline = executor.GetPipeline(typeof(FpFullStackBenchmarkCommand));

    Console.WriteLine("Profile mode active — running pipeline loop for 15 seconds...");
    Console.WriteLine("Attach profiler now or use: dnx asynkron-profiler --cpu -- <this process>");

    var iterations = 0L;
    var deadline = DateTime.UtcNow.AddSeconds(15);

    while (DateTime.UtcNow < deadline)
    {
        // Alternate between minimal and full-stack to capture both paths.
        for (var i = 0; i < 1000; i++)
        {
            var ctx1 = new ApiOperationContext(serviceProvider, app, minimalDescriptor, CancellationToken.None);
            await pipeline.ExecuteAsync(ctx1);

            var ctx2 = new ApiOperationContext(serviceProvider, app, fullStackDescriptor, CancellationToken.None)
            {
                Operation = new FpFullStackBenchmarkCommand
                {
                    TenantId = "tenant-42",
                    Name = "test-operation",
                    Description = "Profile test",
                },
            };
            await fullStackPipeline.ExecuteAsync(ctx2);
        }

        iterations += 2000;
    }

    Console.WriteLine($"Completed {iterations:N0} pipeline executions in 15 seconds ({iterations / 15.0:N0} ops/sec)");
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
