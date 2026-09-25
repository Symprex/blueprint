using Blueprint.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Handlers;
using Rebus.Transport;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="BlueprintApiActivator.GetHandlers{TMessage}"/> keeps the DI scope a plain (non-operation)
/// <see cref="IHandleMessages{TMessage}"/> is resolved from alive until Rebus disposes the transaction context, and
/// disposes it then on every path.
/// </summary>
/// <remarks>
/// This used to dispose the scope as <c>GetHandlers</c> returned, before Rebus called <c>Handle</c>, so any scoped
/// <see cref="IDisposable"/> dependency of the handler (a Marten <c>IDocumentSession</c>, for example) was already
/// disposed by the time the handler used it.
/// </remarks>
public class BlueprintApiActivatorTests
{
    [Fact]
    public async Task GetHandlers_for_plain_handler_keeps_scoped_dependency_alive_until_transaction_context_disposed()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, ResourceUsingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<ResourceUsingHandler>(activator, transactionScope.TransactionContext);

        var handle = () => handler.Handle(new PlainMessage());
        await handle.Should().NotThrowAsync("Rebus invokes the handler only after GetHandlers has returned");

        await transactionScope.CompleteAsync();
        handler.Resource.DisposeCount.Should().Be(0, "commit and ack callbacks still run inside the transaction");

        transactionScope.Dispose();
        handler.Resource.DisposeCount.Should().Be(1, "the scope ends when Rebus disposes the transaction context");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_handler_throws_and_transaction_aborted_disposes_scoped_dependency()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, ThrowingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<ThrowingHandler>(activator, transactionScope.TransactionContext);

        var handle = () => handler.Handle(new PlainMessage());
        await handle.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage(ThrowingHandler.FailureMessage);

        // Disposed without SetResult or Complete, so Rebus runs the rollback and disposal callbacks but none of the
        // commit, ack or nack callbacks.
        transactionScope.Dispose();
        handler.Resource.DisposeCount.Should().Be(1, "an aborted transaction must still end the handler's scope");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_with_async_only_disposable_dependency_awaits_its_disposal_when_transaction_context_disposed()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, AsyncResourceUsingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var handler = await ResolveSingleHandlerAsync<AsyncResourceUsingHandler>(activator, transactionScope.TransactionContext);
        var resource = handler.Resource;

        var handle = () => handler.Handle(new PlainMessage());
        await handle.Should().NotThrowAsync();

        try
        {
            // Bounded, because a disposal started from a commit or ack callback would hold completion on the gate.
            var complete = () => transactionScope.CompleteAsync();
            await complete.Should().CompleteWithinAsync(
                TimeSpan.FromSeconds(10),
                "commit and ack callbacks must not wait on the handler scope's disposal");
            resource.DisposalStarted.IsCompleted.Should().BeFalse("commit and ack callbacks still run inside the transaction");

            // Disposed on another thread, because disposing the transaction context must block until the service's
            // disposal finishes, and that cannot happen until this test releases it.
            var disposeTransaction = Task.Run(() =>
            {
                transactionScope.Dispose();

                return resource.DisposeCompleted;
            });

            var disposalStarted = () => resource.DisposalStarted;
            await disposalStarted.Should().CompleteWithinAsync(
                TimeSpan.FromSeconds(10),
                "disposing the transaction context must start the IAsyncDisposable-only service's disposal");

            // The disposal is held, so a Dispose that returns now did not wait for it. The window only gives such an
            // implementation time to return; a Dispose that does wait cannot return within it, however long it is.
            var transactionDisposed = () => disposeTransaction;
            await transactionDisposed.Should().NotCompleteWithinAsync(
                TimeSpan.FromMilliseconds(250),
                "disposing the transaction context must wait for the service's asynchronous disposal to finish");

            resource.ReleaseDisposal();

            (await disposeTransaction.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue(
                "the service's disposal must have finished by the time disposing the transaction context returns");
        }
        finally
        {
            // Never leave a disposal blocked on the gate if an assertion above failed.
            resource.ReleaseDisposal();
        }
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_when_resolution_fails_disposes_already_resolved_dependency_with_transaction_context()
    {
        ScopedResource? resolvedBeforeFailure = null;

        using var provider = BuildProvider(services =>
            services.AddScoped<IHandleMessages<PlainMessage>>(sp =>
            {
                resolvedBeforeFailure = sp.GetRequiredService<ScopedResource>();

                throw new InvalidOperationException("The handler could not be constructed.");
            }));
        var activator = new BlueprintApiActivator(provider);
        using var transactionScope = new RebusTransactionScope();

        var getHandlers = () => activator.GetHandlers(new PlainMessage(), transactionScope.TransactionContext);
        await getHandlers.Should().ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("The handler could not be constructed.");

        transactionScope.Dispose();
        resolvedBeforeFailure.Should().NotBeNull();
        resolvedBeforeFailure!.DisposeCount.Should().Be(1, "a dependency resolved before the failure must not leak");
    }

    [Fact]
    public async Task GetHandlers_for_plain_handler_in_separate_transaction_contexts_ties_each_scope_to_its_own_context()
    {
        using var provider = BuildProvider(static services =>
            services.AddScoped<IHandleMessages<PlainMessage>, ResourceUsingHandler>());
        var activator = new BlueprintApiActivator(provider);
        using var firstTransaction = new RebusTransactionScope();
        using var secondTransaction = new RebusTransactionScope();

        var first = await ResolveSingleHandlerAsync<ResourceUsingHandler>(activator, firstTransaction.TransactionContext);
        var second = await ResolveSingleHandlerAsync<ResourceUsingHandler>(activator, secondTransaction.TransactionContext);

        second.Resource.Should().NotBeSameAs(first.Resource, "each message is handled in its own DI scope");

        firstTransaction.Dispose();
        first.Resource.DisposeCount.Should().Be(1);
        second.Resource.DisposeCount.Should().Be(0, "ending one transaction must not end another message's scope");

        var handleSecond = () => second.Handle(new PlainMessage());
        await handleSecond.Should().NotThrowAsync();

        secondTransaction.Dispose();
        second.Resource.DisposeCount.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        // An empty app, so every message type takes the plain (non-operation) handler path.
        services.AddSingleton(new BlueprintApp());
        services.AddScoped<ScopedResource>();
        services.AddScoped<AsyncOnlyResource>();

        configure(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<THandler> ResolveSingleHandlerAsync<THandler>(
        BlueprintApiActivator activator,
        ITransactionContext transactionContext)
        where THandler : IHandleMessages<PlainMessage>
    {
        var handlers = await activator.GetHandlers(new PlainMessage(), transactionContext);

        return handlers.Should().ContainSingle().Which.Should().BeOfType<THandler>().Subject;
    }

    private sealed class PlainMessage;

    /// <summary>
    /// Stands in for a scoped, disposable dependency such as a Marten <c>IDocumentSession</c>: using it after it has
    /// been disposed throws, exactly as the real session does.
    /// </summary>
    private sealed class ScopedResource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Use()
        {
            ObjectDisposedException.ThrowIf(this.DisposeCount > 0, this);
        }

        public void Dispose()
        {
            this.DisposeCount++;
        }
    }

    /// <summary>
    /// A scoped dependency that implements only <see cref="IAsyncDisposable"/>, whose disposal cannot finish until the
    /// test releases it, so disposal that is started but not waited for is observable without depending on timing.
    /// </summary>
    private sealed class AsyncOnlyResource : IAsyncDisposable
    {
        private readonly TaskCompletionSource _disposalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposalReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _disposeCompleted;

        public Task DisposalStarted => this._disposalStarted.Task;

        public bool DisposeCompleted => this._disposeCompleted;

        public void Use()
        {
            ObjectDisposedException.ThrowIf(this.DisposalStarted.IsCompleted, this);
        }

        public void ReleaseDisposal()
        {
            this._disposalReleased.TrySetResult();
        }

        public async ValueTask DisposeAsync()
        {
            this._disposalStarted.TrySetResult();

            await this._disposalReleased.Task.ConfigureAwait(false);

            this._disposeCompleted = true;
        }
    }

    private sealed class ResourceUsingHandler(ScopedResource resource) : IHandleMessages<PlainMessage>
    {
        public ScopedResource Resource => resource;

        public Task Handle(PlainMessage message)
        {
            resource.Use();

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler(ScopedResource resource) : IHandleMessages<PlainMessage>
    {
        public const string FailureMessage = "The handler failed.";

        public ScopedResource Resource => resource;

        public Task Handle(PlainMessage message)
        {
            resource.Use();

            throw new InvalidOperationException(FailureMessage);
        }
    }

    private sealed class AsyncResourceUsingHandler(AsyncOnlyResource resource) : IHandleMessages<PlainMessage>
    {
        public AsyncOnlyResource Resource => resource;

        public Task Handle(PlainMessage message)
        {
            resource.Use();

            return Task.CompletedTask;
        }
    }
}
