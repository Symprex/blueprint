using Blueprint.Authorization;
using Blueprint.Model;
using Blueprint.Runtime.Exec;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Blueprint.SourceGenerator.Tests.Integration.Authorization;

/// <summary>
/// Proves that <see cref="StateBasedLinkChecker{TOperation, TResource}"/> correctly hides links
/// when the resource state indicates the operation is unavailable, and shows links otherwise.
/// </summary>
public class StateBasedLinkCheckerTests
{
    [Fact]
    public void AppliesTo_matching_operation_returns_true()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();

        checker.AppliesTo(descriptor).Should().BeTrue();
    }

    [Fact]
    public void AppliesTo_different_operation_returns_false()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<ActivateCommand>();

        checker.AppliesTo(descriptor).Should().BeFalse();
    }

    [Fact]
    public async Task CanExecuteOperationAsync_always_returns_yes()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();
        var context = CreateContext(descriptor);

        var result = await checker.CanExecuteOperationAsync(context, descriptor, new DeactivateCommand());

        result.IsAllowed.Should().BeTrue("state-based checks only apply to link visibility, not execution");
    }

    [Fact]
    public async Task CanShowLinkAsync_when_resource_in_valid_state_returns_yes()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();
        var context = CreateContext(descriptor);
        var resource = new TestResource { IsActive = true };

        var result = await checker.CanShowLinkAsync(context, descriptor, resource);

        result.IsAllowed.Should().BeTrue("active resources can be deactivated");
    }

    [Fact]
    public async Task CanShowLinkAsync_when_resource_in_invalid_state_returns_no()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();
        var context = CreateContext(descriptor);
        var resource = new TestResource { IsActive = false };

        var result = await checker.CanShowLinkAsync(context, descriptor, resource);

        result.IsAllowed.Should().BeFalse("already-deactivated resources cannot be deactivated again");
        result.FailureType.Should().Be(ExecutionAllowedFailureType.Authorisation);
    }

    [Fact]
    public async Task CanShowLinkAsync_when_resource_is_wrong_type_returns_yes()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();
        var context = CreateContext(descriptor);
        var wrongResource = new OtherResource();

        var result = await checker.CanShowLinkAsync(context, descriptor, wrongResource);

        result.IsAllowed.Should().BeTrue("checker only applies to TResource; other types are ignored");
    }

    [Fact]
    public void YesTask_is_reused_for_zero_allocation_happy_path()
    {
        var checker = new TestDeactivateChecker();
        var descriptor = CreateDescriptor<DeactivateCommand>();
        var context = CreateContext(descriptor);
        var resource = new TestResource { IsActive = true };

        var task = checker.CanShowLinkAsync(context, descriptor, resource);

        task.Should().BeSameAs(ExecutionAllowed.YesTask, "the cached YesTask should be returned for zero allocations");
    }

    private static ApiOperationDescriptor CreateDescriptor<T>()
        where T : class, new()
    {
        return new ApiOperationDescriptor(typeof(T), typeof(T).Name, "test");
    }

    private static ApiOperationContext CreateContext(ApiOperationDescriptor descriptor)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new BlueprintApp();
        app.Register(descriptor);

        return new ApiOperationContext(services, app, descriptor, CancellationToken.None);
    }

    // Simple test types — not real operations, just used for type identity checks
    private class DeactivateCommand;

    private class ActivateCommand;

    private class TestResource
    {
        public bool IsActive { get; set; }
    }

    private class OtherResource;

    private class TestDeactivateChecker : StateBasedLinkChecker<DeactivateCommand, TestResource>
    {
        protected override bool IsLinkAvailableForOperation(ApiOperationContext operationContext, TestResource resource)
        {
            return resource.IsActive;
        }
    }
}
