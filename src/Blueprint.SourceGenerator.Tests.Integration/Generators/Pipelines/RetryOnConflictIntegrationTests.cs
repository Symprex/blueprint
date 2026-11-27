using System.Net;
using Blueprint.Auditing;
using Blueprint.Messaging;
using Blueprint.Model;
using Blueprint.Persistence;
using Blueprint.Rebus;
using Blueprint.Runtime.Exec;
using Blueprint.SourceGenerator.Tests.Integration.Generators.OperationDescriptors;
using Blueprint.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Rebus.Transport;

namespace Blueprint.SourceGenerator.Tests.Integration.Generators.Pipelines;

/// <summary>
/// Issue #200: an operation with <c>[RetryOnConflict]</c> whose unit of work save is refused is rerun in a fresh scope.
/// What the refused attempt buffered in its scope — its Rebus transaction, its <see cref="IMessageOutbox"/>, its
/// background tasks — is never committed, nothing is audited for it, and after the last rerun the answer is the
/// operation's 409 (ADR 0001).
/// </summary>
public class RetryOnConflictIntegrationTests
{
    [Fact]
    public async Task A_refused_save_is_rerun_in_a_fresh_scope_and_only_the_rerun_commits_its_messages_tasks_and_audit()
    {
        await using var provider = BuildProvider(refusals: 1);
        using var hostScope = provider.CreateScope();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider);
        var log = provider.GetRequiredService<RetryLog>();

        var result = await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        result.WasSuccessful.Should().BeTrue();
        log.Entries.Should().Equal(
            "handler in scope 1",
            "save refused in scope 1",
            "handler in scope 2",
            "save in scope 2",
            "outbox commit in scope 2",
            "audit",
            "task flush in scope 2",
            "rebus commit in scope 2");
    }

    [Fact]
    public async Task A_save_refused_on_every_rerun_is_answered_with_the_operations_409_and_commits_nothing()
    {
        await using var provider = BuildProvider(refusals: int.MaxValue);
        using var hostScope = provider.CreateScope();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider);
        var log = provider.GetRequiredService<RetryLog>();

        var result = await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        var problem = result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Subject;
        problem.HttpStatus.Should().Be(409);
        problem.Type.Should().Be("reference_claimed");
        problem.InnerException.Should().BeOfType<ConcurrencyConflictException>();
        log.Entries.Should().Equal(
            "handler in scope 1",
            "save refused in scope 1",
            "handler in scope 2",
            "save refused in scope 2",
            "handler in scope 3",
            "save refused in scope 3");
    }

    [Fact]
    public async Task Each_rerun_scope_is_disposed_and_the_hosts_provider_restored_when_there_is_no_http_response()
    {
        await using var provider = BuildProvider(refusals: 2);
        using var hostScope = provider.CreateScope();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider);
        var log = provider.GetRequiredService<RetryLog>();

        await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        context.ServiceProvider.Should().BeSameAs(hostScope.ServiceProvider);
        log.DisposedScopes.Should().Equal(2, 3);
    }

    [Fact]
    public async Task The_last_rerun_scope_stays_the_contexts_until_the_http_response_is_done_with_it()
    {
        await using var provider = BuildProvider(refusals: 1);
        using var hostScope = provider.CreateScope();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider);
        context.HttpContext = new DefaultHttpContext();
        var log = provider.GetRequiredService<RetryLog>();

        await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        context.ServiceProvider.Should().NotBeSameAs(hostScope.ServiceProvider);
        context.ServiceProvider.GetRequiredService<RetryProbe>().Scope.Should().Be(2);
        log.DisposedScopes.Should().BeEmpty("the response disposes the scope once it has been written");
    }

    [Fact]
    public async Task Any_other_failure_of_the_save_is_not_rerun()
    {
        await using var provider = BuildProvider(refusals: 0, failure: new InvalidOperationException("The database is down."));
        using var hostScope = provider.CreateScope();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider);
        var log = provider.GetRequiredService<RetryLog>();

        var result = await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<InvalidOperationException>();
        log.Entries.Should().Equal("handler in scope 1", "save failed in scope 1");
    }

    [Fact]
    public async Task A_refused_save_of_an_operation_without_the_attribute_is_not_rerun()
    {
        await using var provider = BuildProvider(refusals: 1);
        using var hostScope = provider.CreateScope();
        var context = CreateContext<NotRetryingEnsureCommand>(hostScope.ServiceProvider);
        var log = provider.GetRequiredService<RetryLog>();

        var result = await ExecuteAsync<NotRetryingEnsureCommand>(provider, context);

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Which.HttpStatus.Should().Be(409);
        log.Entries.Should().Equal("handler in scope 1", "save refused in scope 1");
    }

    [Fact]
    public async Task A_nested_execution_lets_the_refusal_through_to_its_root_rather_than_rerun_itself()
    {
        await using var provider = BuildProvider(refusals: 1);
        using var hostScope = provider.CreateScope();
        var root = CreateContext<NotRetryingEnsureCommand>(hostScope.ServiceProvider);
        var nested = root.CreateNested(new RetryingEnsureCommand(), skipAuthorisation: true);
        var log = provider.GetRequiredService<RetryLog>();

        var result = await ExecuteAsync<RetryingEnsureCommand>(provider, nested);

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Which.Type.Should().Be("reference_claimed");
        log.Entries.Should().Equal("handler in scope 1", "save refused in scope 1");
    }

    [Fact]
    public async Task A_cancelled_operation_is_not_rerun()
    {
        await using var provider = BuildProvider(refusals: 1);
        using var hostScope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var context = CreateContext<RetryingEnsureCommand>(hostScope.ServiceProvider, cancellation.Token);
        var log = provider.GetRequiredService<RetryLog>();
        log.OnRefusal = cancellation.Cancel;

        var result = await ExecuteAsync<RetryingEnsureCommand>(provider, context);

        result.Should().BeOfType<UnhandledExceptionResult>().Which.Exception.Should().BeOfType<ApiException>().Which.HttpStatus.Should().Be(409);
        log.Entries.Should().Equal("handler in scope 1", "save refused in scope 1");
    }

    private static ApiOperationContext CreateContext<T>(IServiceProvider services, CancellationToken cancellationToken = default)
        where T : IOperation
    {
        var app = new IntegrationTestBlueprintApp();

        return new ApiOperationContext(services, app, app.FindOperation(typeof(T)), cancellationToken)
        {
            SkipAuthorisation = true,
        };
    }

    private static async Task<OperationResult> ExecuteAsync<T>(IServiceProvider provider, ApiOperationContext context)
    {
        var executor = new IntegrationTestBlueprintAppExecutor(NullLoggerFactory.Instance, (IntegrationTestBlueprintApp)context.App, provider);

        return await executor.GetPipeline(typeof(T)).ExecuteAsync(context);
    }

    private static ServiceProvider BuildProvider(int refusals, Exception? failure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(new RetryLog { RefusalsLeft = refusals, Failure = failure });
        services.AddScoped<RetryProbe>();

        // The pipeline opens its Rebus transaction only when an IBus is registered; the enlistment stands in for the
        // transport's send callback, which a message sent in the transaction goes out from.
        services.AddSingleton(Substitute.For<IBus>());
        services.AddScoped<IRebusTransactionEnlistment, ProbeEnlistment>();
        services.AddScoped<IUnitOfWork, ProbeUnitOfWork>();
        services.AddScoped<IMessageOutbox, ProbeMessageOutbox>();
        services.AddScoped<IBackgroundTaskScheduler, ProbeBackgroundTaskScheduler>();

        // A root singleton, as the pipeline shell resolves it from the root provider.
        services.AddSingleton<IAuditSink, ProbeAuditSink>();

        return services.BuildServiceProvider();
    }

    private sealed class ProbeEnlistment(RetryProbe probe) : IRebusTransactionEnlistment
    {
        public ValueTask EnlistAsync(ITransactionContext transactionContext, IServiceProvider services, CancellationToken cancellationToken)
        {
            transactionContext.OnCommit(_ =>
            {
                probe.Add("rebus commit");
                return Task.CompletedTask;
            });

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeUnitOfWork(RetryProbe probe, RetryLog log) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (log.Failure is { } failure)
            {
                probe.Add("save failed");
                return Task.FromException(failure);
            }

            if (log.RefusalsLeft > 0)
            {
                log.RefusalsLeft--;
                probe.Add("save refused");
                log.OnRefusal?.Invoke();

                return Task.FromException(new ConcurrencyConflictException("refused", new InvalidOperationException("the store's own")));
            }

            probe.Add("save");
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeMessageOutbox(RetryProbe probe) : IMessageOutbox
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            probe.Add("outbox commit");
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeBackgroundTaskScheduler(RetryProbe probe) : IBackgroundTaskScheduler
    {
        public IScheduledBackgroundTask Enqueue(IBackgroundTask task)
        {
            throw new NotSupportedException();
        }

        public IScheduledBackgroundTask Schedule(IBackgroundTask task, TimeSpan delay)
        {
            throw new NotSupportedException();
        }

        public ValueTask RunNowAsync()
        {
            probe.Add("task flush");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProbeAuditSink(RetryLog log) : IAuditSink
    {
        public Task RecordAsync(ApiOperationContext context, OperationResult result, CancellationToken cancellationToken)
        {
            log.Entries.Add("audit");
            return Task.CompletedTask;
        }
    }
}

/// <summary>What the operations under test did, in order, across every scope they ran in.</summary>
public sealed class RetryLog
{
    private int _scopes;

    /// <summary>Gets the entries, each naming the scope it was made in.</summary>
    public List<string> Entries { get; } = [];

    /// <summary>Gets the numbers of the scopes disposed, in order.</summary>
    public List<int> DisposedScopes { get; } = [];

    /// <summary>Gets or sets how many more saves to refuse.</summary>
    public int RefusalsLeft { get; set; }

    /// <summary>Gets or sets a failure every save fails with instead.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets or sets what to do when a save is refused.</summary>
    public Action? OnRefusal { get; set; }

    /// <summary>Numbers the next scope.</summary>
    /// <returns>The scope's number, from one.</returns>
    public int NextScope()
    {
        return Interlocked.Increment(ref this._scopes);
    }
}

/// <summary>One per scope: records into the <see cref="RetryLog"/> which scope it was made in.</summary>
public sealed class RetryProbe(RetryLog log) : IDisposable
{
    /// <summary>Gets the number of this probe's scope.</summary>
    public int Scope { get; } = log.NextScope();

    /// <summary>Records <paramref name="entry"/> as made in this scope.</summary>
    /// <param name="entry">What happened.</param>
    public void Add(string entry)
    {
        log.Entries.Add($"{entry} in scope {this.Scope}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        log.DisposedScopes.Add(this.Scope);
    }
}

/// <summary>A find-or-create that is rerun twice after a refused save.</summary>
[RetryOnConflict(2)]
[ConcurrencyConflictError(nameof(Claimed))]
public partial class RetryingEnsureCommand : ICommand
{
    /// <summary>Gets the error for a reference another request claimed first.</summary>
    public static ApiExceptionFactory Claimed => new("Reference claimed", "reference_claimed", HttpStatusCode.Conflict);

    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <param name="probe">The scope's probe.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(RetryingEnsureCommand operation, RetryProbe probe)
    {
        probe.Add("handler");
        return Task.CompletedTask;
    }
}

/// <summary>The same find-or-create without <c>[RetryOnConflict]</c>.</summary>
public partial class NotRetryingEnsureCommand : ICommand
{
    /// <summary>Handles the command.</summary>
    /// <param name="operation">The command instance.</param>
    /// <param name="probe">The scope's probe.</param>
    /// <returns>A completed task.</returns>
    public static Task HandleAsync(NotRetryingEnsureCommand operation, RetryProbe probe)
    {
        probe.Add("handler");
        return Task.CompletedTask;
    }
}
