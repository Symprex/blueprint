using FluentAssertions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves that <see cref="MartenUnitOfWork"/> correctly delegates to the Marten
/// <see cref="IDocumentSession.SaveChangesAsync"/> and propagates cancellation tokens.
/// </summary>
public class MartenUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_delegates_to_session()
    {
        var session = Substitute.For<IDocumentSession>();
        var sut = new MartenUnitOfWork(session);

        await sut.SaveChangesAsync(CancellationToken.None);

        await session.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SaveChangesAsync_passes_cancellation_token()
    {
        var session = Substitute.For<IDocumentSession>();
        using var cts = new CancellationTokenSource();
        var sut = new MartenUnitOfWork(session);

        await sut.SaveChangesAsync(cts.Token);

        await session.Received(1).SaveChangesAsync(cts.Token);
    }

    [Fact]
    public async Task SaveChangesAsync_makes_its_own_scope_current_for_the_session_listeners()
    {
        // A plain Rebus handler's unit of work is saved from a transaction callback, where no pipeline has set the
        // accessor: without this the domain event listener finds no scope and dispatches nothing.
        var (provider, session) = BuildProvider();
        await using var _ = provider;
        await using var scope = provider.CreateAsyncScope();
        IServiceProvider? currentDuringSave = null;
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentDuringSave = MartenScopeAccessor.Current;

            return Task.CompletedTask;
        });

        MartenScopeAccessor.Current = null;
        var sut = scope.ServiceProvider.GetRequiredService<Persistence.IUnitOfWork>();

        await sut.SaveChangesAsync();

        currentDuringSave.Should().BeSameAs(scope.ServiceProvider);
    }

    [Fact]
    public async Task SaveChangesAsync_uses_its_own_scope_even_when_another_is_current()
    {
        // The listener resolves the domain event handlers from the current scope, and a handler that writes must write
        // to the session being saved: the unit of work's own scope, not whichever happens to be ambient.
        var (provider, session) = BuildProvider();
        await using var _ = provider;
        await using var outer = provider.CreateAsyncScope();
        await using var scope = provider.CreateAsyncScope();
        IServiceProvider? currentDuringSave = null;
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentDuringSave = MartenScopeAccessor.Current;

            return Task.CompletedTask;
        });

        MartenScopeAccessor.Current = outer.ServiceProvider;

        try
        {
            await scope.ServiceProvider.GetRequiredService<Persistence.IUnitOfWork>().SaveChangesAsync();

            currentDuringSave.Should().BeSameAs(scope.ServiceProvider);

            // Guards against a rewrite that stops SaveChangesAsync being an async method, which would leak the value.
            MartenScopeAccessor.Current.Should().BeSameAs(outer.ServiceProvider, "the caller's current scope is not changed");
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task SaveChangesAsync_constructed_without_a_scope_leaves_the_current_scope_alone()
    {
        var session = Substitute.For<IDocumentSession>();
        IServiceProvider? currentDuringSave = null;
        session.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            currentDuringSave = MartenScopeAccessor.Current;

            return Task.CompletedTask;
        });

        MartenScopeAccessor.Current = null;

        await new MartenUnitOfWork(session).SaveChangesAsync();

        currentDuringSave.Should().BeNull();
    }

    private static (ServiceProvider Provider, IDocumentSession Session) BuildProvider()
    {
        var session = Substitute.For<IDocumentSession>();
        var services = new ServiceCollection();
        services.AddBlueprintMarten();
        services.AddScoped(_ => session);

        return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), session);
    }
}
