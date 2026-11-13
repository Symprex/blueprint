using System.Text;
using ModelContextProtocol.Protocol;

namespace Blueprint.Mcp.Tests;

public sealed class OperationToolResultTests
{
    [Fact]
    public void A_JSON_object_body_is_the_text_and_the_structured_content_of_a_successful_result()
    {
        var result = OperationToolResult.Create(200, "application/json; charset=utf-8", Body("application/json; charset=utf-8", Encoding.UTF8.GetBytes("""{"id":"a"}""")));

        result.IsError.Should().BeFalse();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(200);
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("""{"id":"a"}""");
        result.StructuredContent!.Value.GetProperty("id").GetString().Should().Be("a");
    }

    [Fact]
    public void A_JSON_array_body_is_text_only_because_structured_content_is_an_object()
    {
        var result = OperationToolResult.Create(200, "application/json", Body("application/json", Encoding.UTF8.GetBytes("""[{"id":"a"}]""")));

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

        var result = OperationToolResult.Create(status, "application/problem+json", Body("application/problem+json", Encoding.UTF8.GetBytes(problem)));

        result.IsError.Should().BeTrue();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(status);
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(problem);
    }

    [Fact]
    public void A_3xx_status_is_not_a_tool_error()
    {
        var result = OperationToolResult.Create(304, null, Body(null, []));

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public void An_empty_body_gives_no_content()
    {
        var result = OperationToolResult.Create(204, null, Body(null, []));

        result.IsError.Should().BeFalse();
        result.Content.Should().BeEmpty();
        result.Meta!["httpStatus"]!.GetValue<int>().Should().Be(204);
    }

    [Fact]
    public void A_body_that_is_not_text_is_described_rather_than_given_as_text()
    {
        var result = OperationToolResult.Create(200, "application/pdf", Body("application/pdf", [0x25, 0x50, 0x44, 0x46]));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(
            "The operation returned 4 bytes of application/pdf, which a tool result does not carry.");
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public void A_body_that_claims_to_be_JSON_and_is_not_is_still_given_as_its_text()
    {
        var result = OperationToolResult.Create(200, "application/json", Body("application/json", Encoding.UTF8.GetBytes("not json")));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("not json");
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public void A_body_that_is_not_text_is_counted_without_being_kept()
    {
        var body = new ToolResponseBody(static () => "application/octet-stream", maxTextBytes: 16);

        body.Write(new byte[1000]);
        body.Write(new byte[24]);

        body.BytesWritten.Should().Be(1024);
        body.IsText.Should().BeFalse();
        body.GetText().Length.Should().Be(0);
    }

    [Fact]
    public void Text_past_the_limit_is_kept_only_to_the_limit_and_the_result_describes_it_instead()
    {
        var body = new ToolResponseBody(static () => "application/json", maxTextBytes: 4);

        body.Write(Encoding.UTF8.GetBytes("""{"a":"""));
        body.Write(Encoding.UTF8.GetBytes("""1}"""));

        body.GetText().ToArray().Should().Equal(Encoding.UTF8.GetBytes("{\"a\""));

        var result = OperationToolResult.Create(200, "application/json", body);

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be(
            "The operation returned 7 bytes of application/json, more than the 4 bytes of text a tool result carries.");
        result.StructuredContent.Should().BeNull();
    }

    [Fact]
    public void A_text_body_is_decoded_by_the_charset_its_content_type_declares()
    {
        const string contentType = "text/plain; charset=utf-16";

        var result = OperationToolResult.Create(200, contentType, Body(contentType, Encoding.Unicode.GetBytes("café")));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("café");
    }

    [Fact]
    public void A_JSON_body_in_another_charset_is_still_its_structured_content()
    {
        const string contentType = "application/json; charset=utf-16";

        var result = OperationToolResult.Create(200, contentType, Body(contentType, Encoding.Unicode.GetBytes("""{"name":"café"}""")));

        result.StructuredContent!.Value.GetProperty("name").GetString().Should().Be("café");
    }

    [Fact]
    public void A_body_whose_charset_is_unknown_is_read_as_UTF8()
    {
        const string contentType = "text/plain; charset=no-such-charset";

        var result = OperationToolResult.Create(200, contentType, Body(contentType, Encoding.UTF8.GetBytes("café")));

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("café");
    }

    private static ToolResponseBody Body(string? contentType, byte[] bytes)
    {
        var body = new ToolResponseBody(() => contentType, OperationToolResult.MaxTextBytes);
        body.Write(bytes);

        return body;
    }
}
