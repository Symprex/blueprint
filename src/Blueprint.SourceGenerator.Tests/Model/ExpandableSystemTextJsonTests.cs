using System.Text.Json;
using Blueprint.Http;
using Blueprint.Model;
using FluentAssertions;

namespace Blueprint.SourceGenerator.Tests.Model;

/// <summary>
/// Pins the wire shape of <see cref="Expandable{T}"/> under Blueprint's own response serializer options, which is the
/// shape Signature365's legacy <c>Expandable&lt;T&gt;</c> had: <c>id</c>, <c>value</c> and <c>expandError</c>, with both
/// nullable members written even when null. An expander sets <c>expandError</c> when expansion fails upstream (for
/// example an authentication failure calling Microsoft Graph), and the frontend reads it to show a banner.
/// </summary>
public class ExpandableSystemTextJsonTests
{
    private static readonly JsonSerializerOptions _blueprintOptions = BlueprintJsonOptions.CreateDefaultOptions();

    [Fact]
    public void Expand_error_round_trips_through_blueprint_serializer_options()
    {
        const string json = """{"id":"group-1","value":null,"expandError":"authentication_error"}""";

        var expandable = JsonSerializer.Deserialize<Expandable<TestGroupResource>>(json, _blueprintOptions)!;
        var root = JsonDocument.Parse(JsonSerializer.Serialize(expandable, _blueprintOptions)).RootElement;

        root.GetProperty("id").GetString().Should().Be("group-1");
        root.TryGetProperty("expandError", out var expandError).Should().BeTrue("the expander's failure reason is part of the payload");
        expandError.GetString().Should().Be("authentication_error");
    }

    [Fact]
    public void Expand_error_set_by_an_expander_is_written_as_expandError_alongside_a_null_value()
    {
        var expandable = new Expandable<TestGroupResource>("group-1")
        {
            Value = null,
            ExpandError = "authentication_error",
        };

        var root = JsonDocument.Parse(JsonSerializer.Serialize(expandable, _blueprintOptions)).RootElement;

        root.GetProperty("expandError").GetString().Should().Be("authentication_error");
        root.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Unset_expand_error_is_written_as_null_rather_than_omitted()
    {
        // Legacy wrote both nullable members unconditionally (it had no WhenWritingNull), so a client can rely on the
        // keys being present on every expandable.
        var expandable = new Expandable<TestGroupResource>("group-1");

        var root = JsonDocument.Parse(JsonSerializer.Serialize(expandable, _blueprintOptions)).RootElement;

        root.EnumerateObject().Select(static p => p.Name).Should().Equal("id", "value", "expandError");
        root.GetProperty("expandError").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed class TestGroupResource : ApiResource
    {
        public override string Object => "group";
    }
}
