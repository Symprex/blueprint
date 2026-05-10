using FluentAssertions;

namespace Blueprint.Rebus.Tests;

/// <summary>
/// Proves that <see cref="NoRebusTransactionAttribute"/> can be applied to operations
/// and detected at runtime for pipeline opt-out decisions.
/// </summary>
public class NoRebusTransactionAttributeTests
{
    [Fact]
    public void Attribute_can_be_applied_to_class()
    {
        var attrs = typeof(TestOperationWithNoRebusTxn)
            .GetCustomAttributes(typeof(NoRebusTransactionAttribute), false);

        attrs.Should().ContainSingle();
    }

    [Fact]
    public void IsDefined_returns_true_for_decorated_type()
    {
        typeof(TestOperationWithNoRebusTxn)
            .IsDefined(typeof(NoRebusTransactionAttribute), inherit: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsDefined_returns_false_for_undecorated_type()
    {
        typeof(TestOperationWithoutAttribute)
            .IsDefined(typeof(NoRebusTransactionAttribute), inherit: false)
            .Should().BeFalse();
    }

    [NoRebusTransaction]
    private class TestOperationWithNoRebusTxn;

    private class TestOperationWithoutAttribute;
}
