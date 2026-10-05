using System.Text.Json;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blueprint.Hangfire.Tests;

/// <summary>
/// Proves how <see cref="HangfireBackgroundTaskExecutor.ExecuteAsync"/> reports the outcome of a task to Hangfire, which
/// knows only whether the job method returned or threw: a task whose pipeline ended in an unhandled exception must fail
/// the job, so that Hangfire records the failure and retries it.
/// </summary>
/// <remarks>
/// Legacy Blueprint's task executor rethrew an unhandled exception result and let every other result complete the job,
/// including a validation failure, which no retry could fix.
/// </remarks>
public class HangfireBackgroundTaskExecutorResultTests
{
    [Fact]
    public async Task When_task_pipeline_ends_in_an_unhandled_exception_then_the_job_fails_with_that_exception()
    {
        var failure = new InvalidOperationException("The task failed.");
        var logger = new RecordingLogger();
        var sut = CreateExecutor(new UnhandledExceptionResult(failure), logger);

        var execute = () => sut.ExecuteAsync(CreatePayload(), CancellationToken.None);

        (await execute.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should().BeSameAs(failure, "Hangfire must record the task's own exception, not a wrapper");
        logger.Entries.Should().ContainSingle(static e => e.Level == LogLevel.Warning && e.Message.Contains(nameof(ResultTask), StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_task_pipeline_fails_validation_then_the_job_completes_as_legacy_did()
    {
        var logger = new RecordingLogger();
        var sut = CreateExecutor(
            new ValidationFailedResult([new System.ComponentModel.DataAnnotations.ValidationResult("required", ["Value"])]),
            logger);

        var execute = () => sut.ExecuteAsync(CreatePayload(), CancellationToken.None);

        await execute.Should().NotThrowAsync("retrying a task that failed validation cannot make it valid");
        logger.Entries.Should().ContainSingle(static e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task When_task_succeeds_then_the_job_completes()
    {
        var sut = CreateExecutor(NoResultOperationResult.Instance, new RecordingLogger());

        var execute = () => sut.ExecuteAsync(CreatePayload(), CancellationToken.None);

        await execute.Should().NotThrowAsync();
    }

    private static HangfireBackgroundTaskExecutor CreateExecutor(OperationResult result, RecordingLogger logger)
    {
        var app = new BlueprintApp();
        app.Register(ResultTask.Configure());

        var executor = new BlueprintAppExecutor(app);
        executor.Register(typeof(ResultTask), new FixedResultPipeline(result));

        var services = new ServiceCollection().BuildServiceProvider();

        return new HangfireBackgroundTaskExecutor(executor, services, logger);
    }

    private static string CreatePayload()
    {
        return JsonSerializer.Serialize(new
        {
            TaskTypeName = typeof(ResultTask).AssemblyQualifiedName,
            TaskJson = JsonSerializer.Serialize(new ResultTask { Value = "payload" }),
        });
    }

    public sealed class ResultTask : IBackgroundTask
    {
        public string Value { get; set; } = string.Empty;

        public static ApiOperationDescriptor Configure()
        {
            return new(typeof(ResultTask), nameof(ResultTask), "test");
        }
    }

    private sealed class FixedResultPipeline(OperationResult result) : IOperationExecutorPipeline
    {
        public ValueTask<OperationResult> ExecuteAsync(ApiOperationContext context)
        {
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingLogger : ILogger<HangfireBackgroundTaskExecutor>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
            {
                this.Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
