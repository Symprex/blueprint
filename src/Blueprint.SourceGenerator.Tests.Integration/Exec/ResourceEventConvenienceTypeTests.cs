using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Tests.Integration.Exec;

/// <summary>
/// Tests for <see cref="ResourceEventResult"/> and <see cref="ResourceEventResult{T}"/> base types.
/// The convenience subclasses (ResourceCreated, ResourceUpdated, ResourceDeleted) live in
/// <c>Blueprint.Http</c> alongside <see cref="Blueprint.Http.ResourceEvent{TResource}"/>.
/// </summary>
public class ResourceEventResultTests
{
    [Theory]
    [InlineData(ChangeType.Created)]
    [InlineData(ChangeType.Updated)]
    [InlineData(ChangeType.Deleted)]
    public void Typed_result_exposes_change_type_and_identity(ChangeType changeType)
    {
        var result = new ResourceEventResult<TestAccount>(changeType, "account.changed", "acc-123", new TestAccount { Id = "acc-123", Name = "Test" });

        Assert.Equal(changeType, result.ChangeType);
        Assert.Equal("account.changed", result.EventId);
        Assert.Equal("acc-123", result.ResourceId);
        Assert.Equal("Test", result.Content.Name);
    }

    [Fact]
    public void WithMetadata_chains_metadata_entries()
    {
        var result = new ResourceEventResult<TestAccount>(ChangeType.Created, "evt", "r1", new TestAccount { Id = "r1", Name = "Meta" });

        result.WithMetadata("source", "test").WithMetadata("version", 2);

        Assert.NotNull(result.Metadata);
        Assert.Equal("test", result.Metadata["source"]);
        Assert.Equal(2, result.Metadata["version"]);
    }

    [Fact]
    public void Unwrap_returns_typed_content()
    {
        var result = new ResourceEventResult<TestAccount>(ChangeType.Created, "evt", "x", new TestAccount { Id = "x", Name = "Unwrap" });

        var unwrapped = result.Unwrap<TestAccount>();

        Assert.Equal("Unwrap", unwrapped.Name);
    }

    [Fact]
    public void Unwrap_throws_for_wrong_type()
    {
        var result = new ResourceEventResult<TestAccount>(ChangeType.Created, "evt", "x", new TestAccount { Id = "x", Name = "Wrong" });

        Assert.Throws<InvalidCastException>(() => result.Unwrap<string>());
    }

    [Fact]
    public void WasSuccessful_is_true()
    {
        var result = new ResourceEventResult<TestAccount>(ChangeType.Updated, "evt", "r1", new TestAccount { Id = "r1", Name = "OK" });

        Assert.True(result.WasSuccessful);
    }

    private sealed class TestAccount
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
