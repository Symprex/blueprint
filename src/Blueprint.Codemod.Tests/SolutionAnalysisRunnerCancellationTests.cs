using System.Diagnostics;
using System.Reflection;
using FluentAssertions;

namespace Blueprint.Codemod.Tests;

public sealed class SolutionAnalysisRunnerCancellationTests
{
    [Xunit.Fact]
    public async Task When_process_output_reading_is_cancelled_then_the_runner_stops_waiting_for_stdout_and_stderr_promptly()
    {
        using var process = StartSleepingProcess();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var readTask = InvokeWaitForProcessOutputAsync(process, cancellationSource.Token);
        var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2)));

        completedTask.Should().Be(readTask, "stream reading should observe the cancellation token instead of waiting for the process to exit naturally.");
        await AwaitCancellationAsync(readTask);
        process.WaitForExit(5000).Should().BeTrue("cancelling the runner should terminate the spawned build process instead of leaking it in the background.");
        process.HasExited.Should().BeTrue();

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    private static async Task AwaitCancellationAsync(Task readTask)
    {
        Func<Task> act = async () => await readTask;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Task InvokeWaitForProcessOutputAsync(Process process, CancellationToken cancellationToken)
    {
        var readMethod = typeof(SolutionAnalysisRunner).GetMethod(
            "WaitForProcessOutputAsync",
            BindingFlags.Static | BindingFlags.NonPublic);

        readMethod.Should().NotBeNull("SolutionAnalysisRunner should expose a private cancellation-aware process wait/output seam.");

        return (Task)readMethod!.Invoke(
            obj: null,
            parameters:
            [
                process,
                cancellationToken,
            ])!;
    }

    private static Process StartSleepingProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Write-Output 'holding'; Start-Sleep -Seconds 30");

        var process = new Process { StartInfo = startInfo };
        process.Start();
        return process;
    }
}
