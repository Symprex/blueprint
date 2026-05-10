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

/// <summary>
/// Measures generated validation pipeline costs across fast-path and runtime-fallback validation scenarios.
/// </summary>
[Config(typeof(InProcessBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ValidationPipelineBenchmarks
{
    /// <summary>
    /// Holds the benchmark app that owns the generated descriptors and pipelines.
    /// </summary>
    private readonly ValidationBenchmarksApp _app = new();

    /// <summary>
    /// Caches the generated executor so each benchmark only measures pipeline execution work.
    /// </summary>
    private readonly ValidationBenchmarksAppExecutor _executor;

    /// <summary>
    /// Initializes the benchmark fixture and creates the shared generated executor instance.
    /// </summary>
    public ValidationPipelineBenchmarks()
    {
        this._executor = new ValidationBenchmarksAppExecutor(NullLoggerFactory.Instance, this._app, new EmptyServiceProvider());
    }

    /// <summary>
    /// Measures the fallback path when a required scenario also carries a custom validation attribute.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark(Baseline = true)]
    public Task<OperationResult> RequiredFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredFallbackValidationOperation));
        var context = CreateContext<RequiredFallbackValidationOperation>(this._app, RequiredFallbackValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures required validation when display metadata forces the pipeline onto the runtime fallback path.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredRuntimeFallbackValidationOperation));
        var context = CreateContext<RequiredRuntimeFallbackValidationOperation>(this._app, RequiredRuntimeFallbackValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures generated fast-path required validation for a missing required value.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredOnlyInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredOnlyValidationOperation));
        var context = CreateContext<RequiredOnlyValidationOperation>(this._app, RequiredOnlyValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures generated fast-path required validation for a whitespace-only string.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures the happy path when required values satisfy the generated fast-path checks.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures combined required and max-length validation when whitespace violates the required rule first.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures combined required and max-length validation when the supplied value exceeds the configured maximum.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures combined required and max-length validation when the value satisfies both rules.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures standalone max-length validation on the generated fast path.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures max-length validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures mixed required and min-length validation when the supplied string is too short.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures min-length validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures string-length validation when the supplied value is shorter than the configured minimum.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures string-length validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures generated fast-path email-address validation for an invalid address.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures email-address validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures cross-property comparison validation on the generated fast path.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures cross-property comparison validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
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

    /// <summary>
    /// Measures scalar greater-than validation on the generated fast path.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> GreaterThanInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(GreaterThanValidationOperation));
        var context = CreateContext<GreaterThanValidationOperation>(
            this._app,
            GreaterThanValidationOperation.Configure(),
            operation =>
            {
                operation.Count = 10;
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures scalar greater-than validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> GreaterThanRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(GreaterThanRuntimeFallbackValidationOperation));
        var context = CreateContext<GreaterThanRuntimeFallbackValidationOperation>(
            this._app,
            GreaterThanRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Count = 10;
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional required validation on the generated fast path when the condition makes the value mandatory.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfValidationOperation));
        var context = CreateContext<RequiredIfValidationOperation>(
            this._app,
            RequiredIfValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional required validation on the generated fast path when the required value is supplied.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfValidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfValidationOperation));
        var context = CreateContext<RequiredIfValidationOperation>(
            this._app,
            RequiredIfValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
                operation.Name = "Blueprint";
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional required validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfRuntimeFallbackValidationOperation));
        var context = CreateContext<RequiredIfRuntimeFallbackValidationOperation>(
            this._app,
            RequiredIfRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures inverse conditional required validation on the generated fast path when the dependent value is present.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfNotInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfNotValidationOperation));
        var context = CreateContext<RequiredIfNotValidationOperation>(
            this._app,
            RequiredIfNotValidationOperation.Configure(),
            operation =>
            {
                operation.TokenId = "token";
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures inverse conditional required validation on the generated fast path when the optional branch is taken.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfNotValidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfNotValidationOperation));
        var context = CreateContext<RequiredIfNotValidationOperation>(this._app, RequiredIfNotValidationOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures inverse conditional required validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> RequiredIfNotRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(RequiredIfNotRuntimeFallbackValidationOperation));
        var context = CreateContext<RequiredIfNotRuntimeFallbackValidationOperation>(
            this._app,
            RequiredIfNotRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.TokenId = "token";
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional prohibition validation on the generated fast path when the forbidden value is supplied.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> NotAllowedIfInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(NotAllowedIfValidationOperation));
        var context = CreateContext<NotAllowedIfValidationOperation>(
            this._app,
            NotAllowedIfValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
                operation.Name = "Blueprint";
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional prohibition validation on the generated fast path when the forbidden value remains unset.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> NotAllowedIfValidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(NotAllowedIfValidationOperation));
        var context = CreateContext<NotAllowedIfValidationOperation>(
            this._app,
            NotAllowedIfValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures conditional prohibition validation when display metadata forces runtime fallback.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> NotAllowedIfRuntimeFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(NotAllowedIfRuntimeFallbackValidationOperation));
        var context = CreateContext<NotAllowedIfRuntimeFallbackValidationOperation>(
            this._app,
            NotAllowedIfRuntimeFallbackValidationOperation.Configure(),
            operation =>
            {
                operation.Enabled = true;
                operation.Name = "Blueprint";
            });
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Measures the whole-object fallback path used for operations that implement IValidatableObject.
    /// </summary>
    /// <returns>The operation result produced by executing the measured validation pipeline.</returns>
    [Benchmark]
    public Task<OperationResult> ValidatableObjectFallbackInvalidAsync()
    {
        var pipeline = this._executor.GetPipeline(typeof(ValidatableBenchmarkOperation));
        var context = CreateContext<ValidatableBenchmarkOperation>(this._app, ValidatableBenchmarkOperation.Configure());
        return pipeline.ExecuteAsync(context);
    }

    /// <summary>
    /// Creates an execution context for a benchmark operation and optionally mutates the operation before execution.
    /// </summary>
    /// <typeparam name="TOperation">The operation type being executed by the benchmark.</typeparam>
    /// <param name="app">The benchmark app that owns the descriptor and generated pipeline.</param>
    /// <param name="descriptor">The descriptor used to construct the operation instance.</param>
    /// <param name="configure">An optional delegate that adjusts the operation into the measured scenario.</param>
    /// <returns>A context ready to execute through the generated validation pipeline.</returns>
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

    /// <summary>
    /// Provides an empty service provider so validation benchmarks do not include dependency injection cost.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}

/// <summary>
/// Benchmark Blueprint app that exposes generated validation pipelines for the operations declared in this file.
/// </summary>
[BlueprintAppScan]
public partial class ValidationBenchmarksApp : BlueprintApp;

/// <summary>
/// Validation attribute used to force runtime fallback while still returning success.
/// </summary>
public sealed class AlwaysValidAttribute : ValidationAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        return ValidationResult.Success;
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path required validation for both string and nullable value members.
/// </summary>
public partial class RequiredOnlyValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Required]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the nullable numeric value used by required validation benchmarks.
    /// </summary>
    [Required]
    public int? Age { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredOnlyValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredOnlyValidationOperation operation)
    {
        return Task.FromResult($"{operation.Name}:{operation.Age}");
    }
}

/// <summary>
/// Benchmark operation that forces required validation through the runtime fallback path by applying display metadata.
/// </summary>
public partial class RequiredRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [Required]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that combines required and max-length rules on the generated fast path.
/// </summary>
public partial class RequiredMaxLengthValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Required]
    [MaxLength(3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredMaxLengthValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredMaxLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path max-length validation.
/// </summary>
public partial class MaxLengthValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [MaxLength(3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="MaxLengthValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(MaxLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces max-length validation through the runtime fallback path.
/// </summary>
public partial class MaxLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [MaxLength(3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="MaxLengthRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(MaxLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that mixes required validation with a custom attribute so the pipeline must use runtime fallback.
/// </summary>
public partial class RequiredFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Required]
    [AlwaysValid]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the nullable numeric value used by required validation benchmarks.
    /// </summary>
    [Required]
    [AlwaysValid]
    public int? Age { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredFallbackValidationOperation operation)
    {
        return Task.FromResult($"{operation.Name}:{operation.Age}");
    }
}

/// <summary>
/// Benchmark operation that combines required and min-length validation on the generated fast path.
/// </summary>
public partial class MixedValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Required]
    [MinLength(3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="MixedValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(MixedValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces min-length validation through the runtime fallback path.
/// </summary>
public partial class MinLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [Required]
    [MinLength(3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="MinLengthRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(MinLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path string-length validation.
/// </summary>
public partial class StringLengthValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [StringLength(5, MinimumLength = 3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="StringLengthValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(StringLengthValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces string-length validation through the runtime fallback path.
/// </summary>
public partial class StringLengthRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [StringLength(5, MinimumLength = 3)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="StringLengthRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(StringLengthRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path email-address validation.
/// </summary>
public partial class EmailAddressValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the email value validated by the benchmark scenario.
    /// </summary>
    [Blueprint.Validation.EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="EmailAddressValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(EmailAddressValidationOperation operation)
    {
        return Task.FromResult(operation.Email ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces email-address validation through the runtime fallback path.
/// </summary>
public partial class EmailAddressRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the email value validated by the benchmark scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [Blueprint.Validation.EmailAddress]
    public string? Email { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="EmailAddressRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(EmailAddressRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Email ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path cross-property comparison validation.
/// </summary>
public partial class GreaterThanPropertyValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the lower bound used by the comparison validation scenarios.
    /// </summary>
    public decimal Minimum { get; set; }

    /// <summary>
    /// Gets or sets the value that must be greater than <see cref="Minimum"/> in the comparison scenarios.
    /// </summary>
    [GreaterThanProperty(nameof(Minimum))]
    public decimal Maximum { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="GreaterThanPropertyValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(GreaterThanPropertyValidationOperation operation)
    {
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
    }
}

/// <summary>
/// Benchmark operation that forces cross-property comparison validation through the runtime fallback path.
/// </summary>
public partial class GreaterThanPropertyRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the lower bound used by the comparison validation scenarios.
    /// </summary>
    public decimal Minimum { get; set; }

    /// <summary>
    /// Gets or sets the value that must be greater than <see cref="Minimum"/> in the comparison scenarios.
    /// </summary>
    [Display(Name = "Maximum value")]
    [GreaterThanProperty(nameof(Minimum))]
    public decimal Maximum { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="GreaterThanPropertyRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(GreaterThanPropertyRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult($"{operation.Minimum}:{operation.Maximum}");
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path scalar greater-than validation.
/// </summary>
public partial class GreaterThanValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the numeric value validated by the greater-than scenarios.
    /// </summary>
    [GreaterThan(10)]
    public int Count { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="GreaterThanValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(GreaterThanValidationOperation operation)
    {
        return Task.FromResult(operation.Count.ToString());
    }
}

/// <summary>
/// Benchmark operation that forces scalar greater-than validation through the runtime fallback path.
/// </summary>
public partial class GreaterThanRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the numeric value validated by the greater-than scenarios.
    /// </summary>
    [Display(Name = "Count value")]
    [GreaterThan(10)]
    public int Count { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="GreaterThanRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(GreaterThanRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Count.ToString());
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path RequiredIf validation.
/// </summary>
public partial class RequiredIfValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the flag that activates the conditional validation rule.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [RequiredIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredIfValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredIfValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces RequiredIf validation through the runtime fallback path.
/// </summary>
public partial class RequiredIfRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the flag that activates the conditional validation rule.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [RequiredIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredIfRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredIfRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path RequiredIfNot validation.
/// </summary>
public partial class RequiredIfNotValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the token identifier whose presence changes the conditional required rule.
    /// </summary>
    public string? TokenId { get; set; }

    /// <summary>
    /// Gets or sets the alternate identifier required when <see cref="TokenId"/> is absent.
    /// </summary>
    [RequiredIfNot(nameof(TokenId), null)]
    public string? PaymentIntentId { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredIfNotValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredIfNotValidationOperation operation)
    {
        return Task.FromResult(operation.PaymentIntentId ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces RequiredIfNot validation through the runtime fallback path.
/// </summary>
public partial class RequiredIfNotRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the token identifier whose presence changes the conditional required rule.
    /// </summary>
    public string? TokenId { get; set; }

    /// <summary>
    /// Gets or sets the alternate identifier required when <see cref="TokenId"/> is absent.
    /// </summary>
    [Display(Name = "Display name")]
    [RequiredIfNot(nameof(TokenId), null)]
    public string? PaymentIntentId { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="RequiredIfNotRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(RequiredIfNotRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.PaymentIntentId ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that isolates generated fast-path NotAllowedIf validation.
/// </summary>
public partial class NotAllowedIfValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the flag that activates the conditional validation rule.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [NotAllowedIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="NotAllowedIfValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(NotAllowedIfValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that forces NotAllowedIf validation through the runtime fallback path.
/// </summary>
public partial class NotAllowedIfRuntimeFallbackValidationOperation : ICommand<string>
{
    /// <summary>
    /// Gets or sets the flag that activates the conditional validation rule.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the name value exercised by the validation scenario.
    /// </summary>
    [Display(Name = "Display name")]
    [NotAllowedIf(nameof(Enabled), true)]
    public string? Name { get; set; }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="NotAllowedIfRuntimeFallbackValidationOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(NotAllowedIfRuntimeFallbackValidationOperation operation)
    {
        return Task.FromResult(operation.Name ?? string.Empty);
    }
}

/// <summary>
/// Benchmark operation that exercises the whole-object fallback required by IValidatableObject.
/// </summary>
public partial class ValidatableBenchmarkOperation : ICommand<string>, IValidatableObject
{
    /// <summary>
    /// Gets or sets the code value validated through <see cref="IValidatableObject"/>.
    /// </summary>
    public string? Code { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(this.Code))
        {
            yield return new ValidationResult("Code is required.", [nameof(this.Code)]);
        }
    }

    /// <summary>
    /// Provides the minimal successful handler body once validation for <see cref="ValidatableBenchmarkOperation"/> succeeds.
    /// </summary>
    /// <param name="operation">The validated operation instance.</param>
    /// <returns>A completed task containing a simple value so the benchmark can execute the full pipeline.</returns>
    public static Task<string> HandleAsync(ValidatableBenchmarkOperation operation)
    {
        return Task.FromResult(operation.Code ?? string.Empty);
    }
}
