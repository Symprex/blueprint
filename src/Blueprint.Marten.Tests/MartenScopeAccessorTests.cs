using FluentAssertions;

namespace Blueprint.Marten.Tests;

/// <summary>
/// Proves the <see cref="MartenScopeAccessor"/> AsyncLocal behavior provides per-async-context
/// isolation and does not leak between concurrent contexts.
/// </summary>
public class MartenScopeAccessorTests
{
    [Fact]
    public void Current_defaults_to_null()
    {
        MartenScopeAccessor.Current = null; // Reset
        MartenScopeAccessor.Current.Should().BeNull();
    }

    [Fact]
    public void Current_can_be_set_and_read_back()
    {
        var sp = new TestServiceProvider();
        MartenScopeAccessor.Current = sp;

        try
        {
            MartenScopeAccessor.Current.Should().BeSameAs(sp);
        }
        finally
        {
            MartenScopeAccessor.Current = null;
        }
    }

    [Fact]
    public async Task Current_is_isolated_per_async_context()
    {
        MartenScopeAccessor.Current = null;

        var sp1 = new TestServiceProvider();
        var sp2 = new TestServiceProvider();

        IServiceProvider? capturedInContext1 = null;
        IServiceProvider? capturedInContext2 = null;

        var task1 = Task.Run(() =>
        {
            MartenScopeAccessor.Current = sp1;
            Thread.Sleep(50); // Simulate some work
            capturedInContext1 = MartenScopeAccessor.Current;
        });

        var task2 = Task.Run(() =>
        {
            MartenScopeAccessor.Current = sp2;
            Thread.Sleep(50); // Simulate some work
            capturedInContext2 = MartenScopeAccessor.Current;
        });

        await Task.WhenAll(task1, task2);

        capturedInContext1.Should().BeSameAs(sp1);
        capturedInContext2.Should().BeSameAs(sp2);

        // The original context should still be null
        MartenScopeAccessor.Current.Should().BeNull();
    }

    private class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
