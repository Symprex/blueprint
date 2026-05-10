using Marten;
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
}
