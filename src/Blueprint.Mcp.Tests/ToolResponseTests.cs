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
    public void Flushing_the_body_synchronously_starts_the_response_too()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);

        response.Stream.Flush();

        response.HasStarted.Should().BeTrue();
    }
}
