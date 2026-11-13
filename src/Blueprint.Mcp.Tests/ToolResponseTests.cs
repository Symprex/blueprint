using System.Buffers;

namespace Blueprint.Mcp.Tests;

public sealed class ToolResponseTests
{
    [Fact]
    public async Task Flushing_the_body_starts_the_response_and_runs_its_OnStarting_callbacks_as_a_server_does()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        var started = false;
        response.OnStarting(
            _ =>
            {
                started = true;
                return Task.CompletedTask;
            },
            new object());

        await response.Stream.FlushAsync();

        response.HasStarted.Should().BeTrue();
        started.Should().BeTrue();
    }

    [Fact]
    public async Task Flushing_the_body_writer_with_nothing_written_starts_the_response_and_runs_its_OnStarting_callbacks_as_a_server_does()
    {
        // The writer used to skip the body when it had nothing buffered, so a headers-only flush left the response
        // unstarted and its OnStarting callbacks unrun, where Kestrel sends the headers.
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        var started = false;
        response.OnStarting(
            _ =>
            {
                started = true;
                return Task.CompletedTask;
            },
            new object());

        await response.Writer.FlushAsync();

        response.HasStarted.Should().BeTrue();
        started.Should().BeTrue();
    }

    [Fact]
    public async Task Bytes_written_through_the_body_writer_reach_the_body_when_it_is_flushed()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);

        await response.Writer.WriteAsync("{}"u8.ToArray());

        response.HasStarted.Should().BeTrue();
        response.Captured.BytesWritten.Should().Be(2);
    }

    [Fact]
    public async Task A_write_or_flush_whose_token_is_cancelled_is_refused_as_a_server_refuses_it()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var write = () => response.Stream.WriteAsync(new byte[] { 1 }, cancelled.Token).AsTask();
        var flush = () => response.Stream.FlushAsync(cancelled.Token);

        await write.Should().ThrowAsync<OperationCanceledException>();
        await flush.Should().ThrowAsync<OperationCanceledException>();
        response.Captured.BytesWritten.Should().Be(0);
    }

    [Fact]
    public async Task A_text_body_with_a_content_encoding_is_described_rather_than_read_as_text()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        response.Headers.ContentType = "application/json";
        response.Headers.ContentEncoding = "gzip";

        await response.Stream.WriteAsync(new byte[] { 0x1F, 0x8B, 0x08, 0x00 });
        var result = OperationToolResult.Create(200, "application/json", response.Captured);

        response.Captured.IsText.Should().BeFalse();
        result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text.Should().Be(
            "The operation returned 4 bytes of application/json, which a tool result does not carry.");
    }

    [Fact]
    public async Task Completing_the_response_writes_the_writers_buffered_bytes_and_completes_it_returning_its_buffers()
    {
        // The writer used to be flushed but never completed, so the pooled buffers it rents were never returned.
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        response.Writer.Write("{}"u8);

        await response.CompleteAsync();

        response.Captured.BytesWritten.Should().Be(2);
        var write = () => response.Writer.GetMemory();
        write.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Disposing_a_response_never_completed_completes_its_writer_returning_its_buffers()
    {
        // As when the operation throws, so the response is not completed.
        var response = new ToolResponse(OperationToolResult.MaxTextBytes);
        var writer = response.Writer;
        writer.Write("{}"u8);

        response.Dispose();

        var write = () => writer.GetMemory();
        write.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Flushing_the_body_synchronously_starts_the_response_too()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);

        response.Stream.Flush();

        response.HasStarted.Should().BeTrue();
    }
}
