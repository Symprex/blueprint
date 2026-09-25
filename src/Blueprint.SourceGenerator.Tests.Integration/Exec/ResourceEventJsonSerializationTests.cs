using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;

namespace Blueprint.SourceGenerator.Tests.Integration.Exec;

/// <summary>
/// Verifies that <see cref="ResourceEvent"/> and <see cref="ResourceEvent{TResource}"/>
/// serialize to JSON with the correct property names, matching the legacy S365 wire format
/// that TypeScript clients and downstream consumers depend on.
/// </summary>
public class ResourceEventJsonSerializationTests
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void ResourceEvent_serializes_dollar_object_as_event()
    {
        var resource = new TestResource { Id = "res-1", Name = "Test" };
        var resourceEvent = new ResourceEvent(
            ResourceEventChangeType.Created,
            "test.created",
            resource);

        var json = JsonSerializer.Serialize(resourceEvent, _options);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("$object", out var objectProp), "JSON must contain '$object' property");
        Assert.Equal("event", objectProp.GetString());
    }

    [Fact]
    public void ResourceEvent_serializes_resourceObject_as_camelCase()
    {
        var resource = new TestResource { Id = "res-2", Name = "Named" };
        var resourceEvent = new ResourceEvent(
            ResourceEventChangeType.Updated,
            "test.updated",
            resource);

        var json = JsonSerializer.Serialize(resourceEvent, _options);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("resourceObject", out var resourceObjectProp), "JSON must contain 'resourceObject' property");
        Assert.Equal("test", resourceObjectProp.GetString());
    }

    [Fact]
    public void ResourceEvent_serializes_all_expected_properties()
    {
        var resource = new TestResource { Id = "res-3", Name = "Full" };
        var resourceEvent = new ResourceEvent(
            ResourceEventChangeType.Created,
            "test.provisioned",
            resource)
        {
            Href = "https://api.example.com/tests/res-3",
            CorrelationId = "corr-123",
        };
        resourceEvent.WithMetadata("key", "value");

        var json = JsonSerializer.Serialize(resourceEvent, _options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("event", root.GetProperty("$object").GetString());
        Assert.Equal("test.provisioned", root.GetProperty("eventId").GetString());
        Assert.Equal("Created", root.GetProperty("changeType").GetString());
        Assert.True(root.TryGetProperty("created", out _), "JSON must contain 'created' property");
        Assert.Equal("test", root.GetProperty("resourceObject").GetString());
        Assert.Equal("https://api.example.com/tests/res-3", root.GetProperty("href").GetString());
        Assert.Equal("corr-123", root.GetProperty("correlationId").GetString());
        Assert.True(root.TryGetProperty("data", out var dataProp), "JSON must contain 'data' property");
        Assert.Equal("res-3", dataProp.GetProperty("id").GetString());
        Assert.True(root.TryGetProperty("metadata", out var metaProp), "JSON must contain 'metadata' property");
        Assert.Equal("value", metaProp.GetProperty("key").GetString());
    }

    [Fact]
    public void Generic_ResourceEvent_serializes_dollar_object_as_event()
    {
        var resource = new TestApiResource { Name = "ApiRes" };
        var resourceEvent = new ResourceEvent<TestApiResource>(
            ResourceEventChangeType.Updated,
            "testApiResource.updated",
            resource);

        var json = JsonSerializer.Serialize(resourceEvent, _options);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("$object", out var objectProp), "JSON must contain '$object' property");
        Assert.Equal("event", objectProp.GetString());
        Assert.Equal("test", doc.RootElement.GetProperty("resourceObject").GetString());
    }

    [Fact]
    public void ResourceEvent_without_metadata_or_secure_data_writes_both_as_empty_objects()
    {
        // Legacy initialised both dictionaries (Signature365 8b40ecc961, Blueprint.Http/ResourceEvent.cs:140, :146) and its
        // converter wrote each whenever it was not null (JsonConverterFactoryForResourceEventOfT.cs:112-122), so every
        // event on the wire had both, empty or not (ResourceEventJsonTests.Can_serialise_to_JSON.verified.txt:19-26).
        var resource = new TestResource { Id = "res-4", Name = "Minimal" };
        var resourceEvent = new ResourceEvent(
            ResourceEventChangeType.Deleted,
            "test.deleted",
            resource);

        var json = JsonSerializer.Serialize(resourceEvent, _options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("metadata", out var metadata), "legacy always wrote metadata");
        Assert.Equal(JsonValueKind.Object, metadata.ValueKind);
        Assert.Empty(metadata.EnumerateObject());
        Assert.True(root.TryGetProperty("secureData", out var secureData), "legacy always wrote secureData");
        Assert.Equal(JsonValueKind.Object, secureData.ValueKind);
        Assert.Empty(secureData.EnumerateObject());
    }

    private sealed class TestResource
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestApiResource : ApiResource
    {
        public override string Object => "test";

        public string Name { get; set; } = string.Empty;
    }
}
