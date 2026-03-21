using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Benchmarks.Validation;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ValidationPipelineBenchmarks
{
    private readonly ValidationBenchmarksApp _app = new();
    private readonly ValidationBenchmarksAppExecutor _executor;

    public ValidationPipelineBenchmarks()
    {
        this._executor = new ValidationBenchmarksAppExecutor(NullLoggerFactory.Instance, this._app);
    }

    [Benchmark(Baseline = true)]
    public Task<OperationResult> RequiredFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredFallbackValidationOperation));
        var context = CreateContext<RequiredFallbackValidationOperation>(this._app, RequiredFallbackValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> RequiredOnlyInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredOnlyValidationOperation));
        var context = CreateContext<RequiredOnlyValidationOperation>(this._app, RequiredOnlyValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> RequiredOnlyWhitespaceAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredOnlyValidationOperation));
        var context = CreateContext<RequiredOnlyValidationOperation>(
            this._app,
            RequiredOnlyValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "   ";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> RequiredOnlyValidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredOnlyValidationOperation));
        var context = CreateContext<RequiredOnlyValidationOperation>(
            this._app,
            RequiredOnlyValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "Blueprint";
                operation.Age = 42;
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> MixedBuiltInFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(MixedValidationOperation));
        var context = CreateContext<MixedValidationOperation>(
            this._app,
            MixedValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "ab";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> ValidatableObjectFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(ValidatableBenchmarkOperation));
        var context = CreateContext<ValidatableBenchmarkOperation>(this._app, ValidatableBenchmarkOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    private static ApiOperationContext CreateContext<TOperation>(
        BlueprintApp app,
        ApiOperationDescriptor descriptor,
        Action<TOperation>? configure = null)
        where TOperation : class, IOperation
    {
        var context = new ApiOperationContext(new EmptyServiceProvider(), app, descriptor, CancellationToken.None);
        configure?.Invoke((TOperation)context.Operation);
        return context;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

[BlueprintAppScan]
public partial class ValidationBenchmarksApp : BlueprintApp;

public sealed class AlwaysValidAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        return ValidationResult.Success;
    }
}

public partial class RequiredOnlyValidationOperation : ICommand<string>
{
    [Required]
    public string? Name { get; set; }

    [Required]
    public int? Age { get; set; }

    public static Task<string> HandleAsync(RequiredOnlyValidationOperation operation)
    {
        return Task.FromResult($"{operation.Name}:{operation.Age}");
    }
}

public partial class RequiredFallbackValidationOperation : ICommand<string>
{
    [Required]
    [AlwaysValid]
    public string? Name { get; set; }

    [Required]
    [AlwaysValid]
    public int? Age { get; set; }

    public static Task<string> HandleAsync(RequiredFallbackValidationOperation operation)
    {
        return Task.FromResult($"{operation.Name}:{operation.Age}");
    }
}

public partial class MixedValidationOperation : ICommand<string>
{
    [Required]
    [MinLength(3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(MixedValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class ValidatableBenchmarkOperation : ICommand<string>, IValidatableObject
{
    public string? Code { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(this.Code))
        {
            yield return new ValidationResult("Code is required.", [nameof(this.Code)]);
        }
    }

    public static Task<string> HandleAsync(ValidatableBenchmarkOperation operation)
    {
        return Task.FromResult(operation.Code ?? string.Empty);
    }
}
