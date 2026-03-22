using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Validation;
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
    public Task<OperationResult> RequiredRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredRuntimeFallbackValidationOperation));
        var context = CreateContext<RequiredRuntimeFallbackValidationOperation>(this._app, RequiredRuntimeFallbackValidationOperation.Configure());
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
    public Task<OperationResult> RequiredMaxLengthWhitespaceAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext<RequiredMaxLengthValidationOperation>(
            this._app,
            RequiredMaxLengthValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "    ";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> RequiredMaxLengthTooLongAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext<RequiredMaxLengthValidationOperation>(
            this._app,
            RequiredMaxLengthValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "abcd";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> RequiredMaxLengthValidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredMaxLengthValidationOperation));
        var context = CreateContext<RequiredMaxLengthValidationOperation>(
            this._app,
            RequiredMaxLengthValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "abc";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> MaxLengthTooLongAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(MaxLengthValidationOperation));
        var context = CreateContext<MaxLengthValidationOperation>(
            this._app,
            MaxLengthValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "abcd";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> MaxLengthRuntimeFallbackTooLongAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(MaxLengthRuntimeFallbackValidationOperation));
        var context = CreateContext<MaxLengthRuntimeFallbackValidationOperation>(
            this._app,
            MaxLengthRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "abcd";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> MinLengthTooShortAsync()
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
    public Task<OperationResult> MinLengthRuntimeFallbackTooShortAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(MinLengthRuntimeFallbackValidationOperation));
        var context = CreateContext<MinLengthRuntimeFallbackValidationOperation>(
            this._app,
            MinLengthRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "ab";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> StringLengthTooShortAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(StringLengthValidationOperation));
        var context = CreateContext<StringLengthValidationOperation>(
            this._app,
            StringLengthValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "ab";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> StringLengthRuntimeFallbackTooShortAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(StringLengthRuntimeFallbackValidationOperation));
        var context = CreateContext<StringLengthRuntimeFallbackValidationOperation>(
            this._app,
            StringLengthRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Name = "ab";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> EmailAddressInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(EmailAddressValidationOperation));
        var context = CreateContext<EmailAddressValidationOperation>(
            this._app,
            EmailAddressValidationOperation.Configure(),
            operation =>
            {
                operation.Email = "invalid";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> EmailAddressRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(EmailAddressRuntimeFallbackValidationOperation));
        var context = CreateContext<EmailAddressRuntimeFallbackValidationOperation>(
            this._app,
            EmailAddressRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Email = "invalid";
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> GreaterThanPropertyInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(GreaterThanPropertyValidationOperation));
        var context = CreateContext<GreaterThanPropertyValidationOperation>(
            this._app,
            GreaterThanPropertyValidationOperation.Configure(),
            operation =>
            {
                operation.Minimum = 10m;
                operation.Maximum = 10m;
            });
        return pipeline.ExecuteAsync(context);
    }

    [Benchmark]
    public Task<OperationResult> GreaterThanPropertyRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(GreaterThanPropertyRuntimeFallbackValidationOperation));
        var context = CreateContext<GreaterThanPropertyRuntimeFallbackValidationOperation>(
            this._app,
            GreaterThanPropertyRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Minimum = 10m;
                operation.Maximum = 10m;
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

public partial class RequiredRuntimeFallbackValidationOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [Required]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(RequiredRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class RequiredMaxLengthValidationOperation : ICommand<string>
{
    [Required]
    [MaxLength(3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(RequiredMaxLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class MaxLengthValidationOperation : ICommand<string>
{
    [MaxLength(3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(MaxLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class MaxLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [MaxLength(3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(MaxLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
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

public partial class MinLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [Required]
    [MinLength(3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(MinLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class StringLengthValidationOperation : ICommand<string>
{
    [StringLength(5, MinimumLength = 3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(StringLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class StringLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [StringLength(5, MinimumLength = 3)]
    public string? Name { get; set; }

    public static Task<string> HandleAsync(StringLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

public partial class EmailAddressValidationOperation : ICommand<string>
{
    [EmailAddress]
    public string? Email { get; set; }

    public static Task<string> HandleAsync(EmailAddressValidationOperation operation)
    {
        return Task.FromResult(operation.Email ?? string.Empty);
    }
}

public partial class EmailAddressRuntimeFallbackValidationOperation : ICommand<string>
{
    [Display(Name = "Display name")]
    [EmailAddress]
    public string? Email { get; set; }

    public static Task<string> HandleAsync(EmailAddressRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Email ?? string.Empty);
    }
}

public partial class GreaterThanPropertyValidationOperation : ICommand<string>
{
    public decimal Minimum { get; set; }

    [GreaterThanProperty(nameof(Minimum))]
    public decimal Maximum { get; set; }

    public static Task<string> HandleAsync(GreaterThanPropertyValidationOperation operation)
    {
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
    }
}

public partial class GreaterThanPropertyRuntimeFallbackValidationOperation : ICommand<string>
{
    public decimal Minimum { get; set; }

    [Display(Name = "Maximum value")]
    [GreaterThanProperty(nameof(Minimum))]
    public decimal Maximum { get; set; }

    public static Task<string> HandleAsync(GreaterThanPropertyRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
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
