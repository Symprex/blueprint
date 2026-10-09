using Blueprint.Messaging;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Proves that the <c>MessageOutboxModule</c> coordinates outbox commits through the generated
/// pipeline: commit is called on command success, skipped on validation failure, skipped when
/// <c>[DoNotPublishMessages]</c> is applied, and entirely absent for query operations.
/// </summary>
public class MessageOutboxModuleIntegrationTests
{
    [Fact]
    public async Task When_command_handler_succeeds_then_message_outbox_commits()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var serviceProvider = new MessagingTestServiceProvider(outbox);

        var pipeline = executor.GetPipeline(typeof(MessagingCommand));
        var context = new ApiOperationContext(serviceProvider, app, MessagingCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((MessagingCommand)context.Operation).Name = "valid-name";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();
        outbox.CommitCallCount.Should().Be(1, "outbox should be committed exactly once after successful command execution");
    }

    [Fact]
    public async Task When_command_fails_validation_then_message_outbox_does_not_commit()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var serviceProvider = new MessagingTestServiceProvider(outbox);

        var pipeline = executor.GetPipeline(typeof(MessagingCommand));
        var context = new ApiOperationContext(serviceProvider, app, MessagingCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        // Leave Name null to trigger Required validation failure
        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<ValidationFailedResult>();
        outbox.CommitCallCount.Should().Be(0, "outbox should NOT be committed when validation fails");
    }

    [Fact]
    public async Task When_no_message_outbox_registered_then_command_pipeline_completes_without_error()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var serviceProvider = new MessagingTestServiceProvider(outbox: null);

        var pipeline = executor.GetPipeline(typeof(MessagingCommand));
        var context = new ApiOperationContext(serviceProvider, app, MessagingCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((MessagingCommand)context.Operation).Name = "no-outbox";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task When_query_operation_succeeds_then_message_outbox_is_not_invoked()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var serviceProvider = new MessagingTestServiceProvider(outbox);

        var pipeline = executor.GetPipeline(typeof(MessagingQueryProof));
        var context = new ApiOperationContext(serviceProvider, app, MessagingQueryProof.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeNull();
        outbox.CommitCallCount.Should().Be(0, "message outbox should NOT participate in query pipelines");
    }

    [Fact]
    public async Task When_command_has_DoNotPublishMessages_then_message_outbox_is_not_invoked()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var serviceProvider = new MessagingTestServiceProvider(outbox);

        var pipeline = executor.GetPipeline(typeof(DoNotPublishMessagesCommand));
        var context = new ApiOperationContext(serviceProvider, app, DoNotPublishMessagesCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((DoNotPublishMessagesCommand)context.Operation).Name = "should-not-commit";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().NotBeOfType<ValidationFailedResult>();
        outbox.CommitCallCount.Should().Be(0, "outbox should NOT be committed when [DoNotPublishMessages] is applied");
    }

    [Fact]
    public async Task When_handler_throws_exception_then_message_outbox_does_not_commit()
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var serviceProvider = new MessagingTestServiceProvider(outbox);

        var pipeline = executor.GetPipeline(typeof(ThrowingMessagingCommand));
        var context = new ApiOperationContext(serviceProvider, app, ThrowingMessagingCommand.Configure(), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };
        ((ThrowingMessagingCommand)context.Operation).Name = "will-throw";

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>(
            "handler exception should be translated to UnhandledExceptionResult");
        outbox.CommitCallCount.Should().Be(0,
            "outbox must NOT be committed when handler throws — uncommitted messages are discarded for data consistency");
    }

    [Fact]
    public async Task When_background_task_succeeds_then_message_outbox_commits()
    {
        // Legacy gave background tasks the same outbox as commands.
        var (context, pipeline, outbox) = CreateTaskPipeline<MessagingBackgroundTask>();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        outbox.CommitCallCount.Should().Be(1, "a background task's messages are committed once it has succeeded");
    }

    [Fact]
    public async Task When_background_task_throws_then_message_outbox_does_not_commit()
    {
        var (context, pipeline, outbox) = CreateTaskPipeline<ThrowingMessagingBackgroundTask>();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<UnhandledExceptionResult>();
        outbox.CommitCallCount.Should().Be(0, "a failed background task's messages must be discarded");
    }

    [Fact]
    public async Task When_background_task_has_DoNotPublishMessages_then_message_outbox_is_not_invoked()
    {
        var (context, pipeline, outbox) = CreateTaskPipeline<DoNotPublishMessagesBackgroundTask>();

        var result = await pipeline.ExecuteAsync(context);

        result.Should().BeOfType<NoResultOperationResult>();
        outbox.CommitCallCount.Should().Be(0);
    }

    private static (ApiOperationContext Context, IOperationExecutorPipeline Pipeline, TestMessageOutbox Outbox) CreateTaskPipeline<TTask>()
        where TTask : Blueprint.Tasks.IBackgroundTask
    {
        var app = new IntegrationTestBlueprintApp();
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, app, NullServiceProvider.Instance);
        var outbox = new TestMessageOutbox();
        var context = new ApiOperationContext(new MessagingTestServiceProvider(outbox), app, app.FindOperation(typeof(TTask)), CancellationToken.None)
        {
            SkipAuthorisation = true,
        };

        return (context, executor.GetPipeline(typeof(TTask)), outbox);
    }

    private sealed class MessagingTestServiceProvider : IServiceProvider
    {
        private readonly IMessageOutbox? _outbox;

        public MessagingTestServiceProvider(IMessageOutbox? outbox)
        {
            this._outbox = outbox;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IMessageOutbox))
            {
                return this._outbox;
            }

            return null;
        }
    }
}

/// <summary>
/// Test message outbox that records commit calls for assertion.
/// </summary>
public sealed class TestMessageOutbox : IMessageOutbox
{
    /// <summary>Gets the number of times CommitAsync was called.</summary>
    public int CommitCallCount { get; private set; }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        this.CommitCallCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A command that participates in messaging outbox coordination.
/// Proves that the generated pipeline calls IMessageOutbox.CommitAsync on success.
/// </summary>
public partial class MessagingCommand : ICommand
{
    /// <summary>Gets or sets the required name field (triggers validation when null).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(MessagingCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A query that proves the message outbox module does NOT participate in query pipelines.
/// </summary>
public partial class MessagingQueryProof : IQuery<string>
{
    /// <summary>Handles the query and returns a string result.</summary>
    /// <param name="operation">The query instance.</param>
    /// <returns>A string result.</returns>
    public static Task<string> HandleAsync(MessagingQueryProof operation)
    {
        return Task.FromResult("query-result");
    }
}

/// <summary>
/// A command with <c>[DoNotPublishMessages]</c> that proves the opt-out marker
/// prevents <c>MessageOutboxModule</c> from contributing to the pipeline.
/// </summary>
[Blueprint.Messaging.DoNotPublishMessages]
public partial class DoNotPublishMessagesCommand : ICommand
{
    /// <summary>Gets or sets the name field.</summary>
    public string? Name { get; set; }

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(DoNotPublishMessagesCommand operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A command whose handler throws an exception, proving that the generated pipeline
/// does NOT commit the message outbox when the handler fails with an unhandled exception.
/// </summary>
public partial class ThrowingMessagingCommand : ICommand
{
    /// <summary>Gets or sets the name field.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    /// <summary>Handles the command by throwing an exception.</summary>
    /// <param name="operation">The command instance.</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task HandleAsync(ThrowingMessagingCommand operation)
    {
        throw new InvalidOperationException("Simulated handler failure for outbox rollback test");
    }
}

/// <summary>A background task that succeeds, so its messages should be committed.</summary>
public partial class MessagingBackgroundTask : Blueprint.Tasks.IBackgroundTask
{
    /// <summary>Handles the task.</summary>
    /// <param name="operation">The task instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(MessagingBackgroundTask operation)
    {
        return Task.CompletedTask;
    }
}

/// <summary>A background task whose handler throws, so its messages must not be committed.</summary>
public partial class ThrowingMessagingBackgroundTask : Blueprint.Tasks.IBackgroundTask
{
    /// <summary>Handles the task by throwing.</summary>
    /// <param name="operation">The task instance.</param>
    /// <returns>Never returns — always throws.</returns>
    public static Task HandleAsync(ThrowingMessagingBackgroundTask operation)
    {
        throw new InvalidOperationException("Simulated background task failure for outbox rollback test");
    }
}

/// <summary>A background task that opts out of the outbox with <c>[DoNotPublishMessages]</c>.</summary>
[Blueprint.Messaging.DoNotPublishMessages]
public partial class DoNotPublishMessagesBackgroundTask : Blueprint.Tasks.IBackgroundTask
{
    /// <summary>Handles the task.</summary>
    /// <param name="operation">The task instance.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(DoNotPublishMessagesBackgroundTask operation)
    {
        return Task.CompletedTask;
    }
}
