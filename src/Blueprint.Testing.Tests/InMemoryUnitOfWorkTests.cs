using FluentAssertions;

namespace Blueprint.Testing.Tests;

public sealed class InMemoryUnitOfWorkTests
{
    [Fact]
    public void Initially_not_saved()
    {
        var uow = new InMemoryUnitOfWork();

        uow.WasSaved.Should().BeFalse();
        uow.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesAsync_increments_save_count()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.SaveChangesAsync();

        uow.WasSaved.Should().BeTrue();
        uow.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Multiple_saves_are_tracked()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.SaveChangesAsync();
        await uow.SaveChangesAsync();
        await uow.SaveChangesAsync();

        uow.SaveCount.Should().Be(3);
    }

    [Fact]
    public async Task Reset_clears_save_count()
    {
        var uow = new InMemoryUnitOfWork();

        await uow.SaveChangesAsync();
        uow.WasSaved.Should().BeTrue();

        uow.Reset();

        uow.WasSaved.Should().BeFalse();
        uow.SaveCount.Should().Be(0);
    }
}
