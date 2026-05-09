using Blueprint.Model;
using Blueprint.Runtime.Exec;

namespace Blueprint.SourceGenerator.Tests.Integration.Exec;

/// <summary>
/// Tests for <see cref="ResourceCreated{T}"/>, <see cref="ResourceUpdated{T}"/>,
/// and <see cref="ResourceDeleted{T}"/> convenience result types.
/// </summary>
public class ResourceEventConvenienceTypeTests
{
    [Fact]
    public void ResourceCreated_with_explicit_event_id()
    {
        var result = new ResourceCreated<TestAccount>("account.provisioned", "acc-123", new TestAccount { Id = "acc-123", Name = "Test" });

        Assert.Equal(ChangeType.Created, result.ChangeType);
        Assert.Equal("account.provisioned", result.EventId);
        Assert.Equal("acc-123", result.ResourceId);
        Assert.Equal("Test", result.Content.Name);
    }

    [Fact]
    public void ResourceCreated_with_conventional_event_id()
    {
        var result = new ResourceCreated<TestAccount>("acc-456", new TestAccount { Id = "acc-456", Name = "Auto" });

        Assert.Equal(ChangeType.Created, result.ChangeType);
        Assert.Equal("TestAccount.created", result.EventId);
        Assert.Equal("acc-456", result.ResourceId);
    }

    [Fact]
    public void ResourceUpdated_with_explicit_event_id()
    {
        var result = new ResourceUpdated<TestAccount>("account.settings.changed", "acc-789", new TestAccount { Id = "acc-789", Name = "Updated" });

        Assert.Equal(ChangeType.Updated, result.ChangeType);
        Assert.Equal("account.settings.changed", result.EventId);
        Assert.Equal("acc-789", result.ResourceId);
        Assert.Equal("Updated", result.Content.Name);
    }

    [Fact]
    public void ResourceUpdated_with_conventional_event_id()
    {
        var result = new ResourceUpdated<TestAccount>("acc-111", new TestAccount { Id = "acc-111", Name = "Modified" });

        Assert.Equal(ChangeType.Updated, result.ChangeType);
        Assert.Equal("TestAccount.updated", result.EventId);
    }

    [Fact]
    public void ResourceDeleted_with_explicit_event_id()
    {
        var result = new ResourceDeleted<TestAccount>("account.archived", "acc-222", new TestAccount { Id = "acc-222", Name = "Gone" });

        Assert.Equal(ChangeType.Deleted, result.ChangeType);
        Assert.Equal("account.archived", result.EventId);
        Assert.Equal("acc-222", result.ResourceId);
    }

    [Fact]
    public void ResourceDeleted_with_conventional_event_id()
    {
        var result = new ResourceDeleted<TestAccount>("acc-333", new TestAccount { Id = "acc-333", Name = "Removed" });

        Assert.Equal(ChangeType.Deleted, result.ChangeType);
        Assert.Equal("TestAccount.deleted", result.EventId);
    }

    [Fact]
    public void All_types_inherit_from_ResourceEventResult()
    {
        ResourceEventResult created = new ResourceCreated<TestAccount>("id1", new TestAccount { Id = "id1", Name = "A" });
        ResourceEventResult updated = new ResourceUpdated<TestAccount>("id2", new TestAccount { Id = "id2", Name = "B" });
        ResourceEventResult deleted = new ResourceDeleted<TestAccount>("id3", new TestAccount { Id = "id3", Name = "C" });

        Assert.IsAssignableFrom<ResourceEventResult>(created);
        Assert.IsAssignableFrom<ResourceEventResult>(updated);
        Assert.IsAssignableFrom<ResourceEventResult>(deleted);
    }

    [Fact]
    public void Unwrap_returns_typed_content()
    {
        var result = new ResourceCreated<TestAccount>("x", new TestAccount { Id = "x", Name = "Unwrap" });

        var unwrapped = result.Unwrap<TestAccount>();

        Assert.Equal("Unwrap", unwrapped.Name);
    }

    private sealed class TestAccount
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
