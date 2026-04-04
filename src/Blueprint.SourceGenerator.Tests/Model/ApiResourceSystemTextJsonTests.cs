using System.Text.Json;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

public class ApiResourceSystemTextJsonTests
{
    [Fact]
    public void System_text_json_serialization_preserves_resource_metadata_names_and_omits_null_link_type()
    {
        var resource = new TestApiResource
        {
            IsPartial = true,
        };
        resource.AddLink("self", new Link
        {
            Href = "/resources/123",
            Type = null,
        });

        var doc = JsonDocument.Parse(JsonSerializer.Serialize(resource));
        var root = doc.RootElement;
        var selfLink = root.GetProperty("$links").GetProperty("self");

        root.GetProperty("$object").GetString().Should().Be("test-resource");
        root.GetProperty("$partial").GetBoolean().Should().BeTrue();
        root.TryGetProperty("ResourceKey", out _).Should().BeFalse();
        selfLink.GetProperty("Href").GetString().Should().Be("/resources/123");
        selfLink.TryGetProperty("Type", out _).Should().BeFalse();
    }

    private sealed class TestApiResource : ApiResource
    {
        public override string Object => "test-resource";
    }
}
