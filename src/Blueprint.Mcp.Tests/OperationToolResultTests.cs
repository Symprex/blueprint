using System.Text;
using ModelContextProtocol.Protocol;

namespace Blueprint.Mcp.Tests;

public sealed class OperationToolResultTests
{
    [Fact]
    public void A_JSON_object_body_is_the_text_and_the_structured_content_of_a_successful_result()
    {
        var result = OperationToolResult.Create(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"id":"a"}"""));

        result.IsError.Should().BeFalse();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(200);
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("""{"id":"a"}""");
        result.StructuredContent!.Value.GetProperty("id").GetString().Should().Be("a");
    }

    [Fact]
    public void A_JSON_array_body_is_text_only_because_structured_content_is_an_object()
    {
        var result = OperationToolResult.Create(200, "application/json", Encoding.UTF8.GetBytes("""[{"id":"a"}]"""));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("""[{"id":"a"}]""");
        result.StructuredContent.Should().BeNull();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(422)]
    [InlineData(500)]
    public void A_4xx_or_5xx_status_is_a_tool_error_carrying_the_problem_body(int status)
    {
        var problem = $$"""{"title":"t","status":{{status}}}""";

        var result = OperationToolResult.Create(status, "application/problem+json", Encoding.UTF8.GetBytes(problem));

        result.IsError.Should().BeTrue();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(status);
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(problem);
    }

    [Fact]
    public void A_3xx_status_is_not_a_tool_error()
    {
        var result = OperationToolResult.Create(304, null, []);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public void An_empty_body_gives_no_content()
    {
        var result = OperationToolResult.Create(204, null, []);

        result.IsError.Should().BeFalse();
        result.Content.Should().BeEmpty();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(204);
    }

    [Fact]
    public void A_body_that_is_not_text_is_described_rather_than_given_as_text()
    {
        var result = OperationToolResult.Create(200, "application/pdf", [0x25, 0x50, 0x44, 0x46]);

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(
            "The operation returned 4 bytes of application/pdf, which a tool result does not carry.");
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public void A_body_that_claims_to_be_JSON_and_is_not_is_still_given_as_its_text()
    {
        var result = OperationToolResult.Create(200, "application/json", Encoding.UTF8.GetBytes("not json"));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("not json");
        result.StructuredContent.Should().BeNull();
    }
}
