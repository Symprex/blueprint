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
    public void Flushing_the_body_synchronously_starts_the_response_too()
    {
        using var response = new ToolResponse(OperationToolResult.MaxTextBytes);

        response.Stream.Flush();

        response.HasStarted.Should().BeTrue();
    }
}
