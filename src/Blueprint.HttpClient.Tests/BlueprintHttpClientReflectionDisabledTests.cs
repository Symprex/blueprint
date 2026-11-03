using System.Diagnostics;
using FluentAssertions;

namespace Blueprint.HttpClient.Tests;

/// <summary>
/// Proves the transport works in a process whose System.Text.Json reflection is turned off, as a trimmed or AOT-published
/// consumer's is.
/// </summary>
/// <remarks>
/// That switch is read once for the process, so it cannot be turned off for one test: a probe program built with it off is
/// run in a child process and its exit code read.
/// </remarks>
public sealed class BlueprintHttpClientReflectionDisabledTests
{
    [Fact]
    public Task A_transport_given_source_generated_metadata_is_usable_when_reflection_is_disabled()
    {
        return RunProbeAsync();
    }

    [Fact]
    public Task A_registered_client_declaring_a_serializer_context_resolves_and_shares_its_options_over_default_options_when_reflection_is_disabled()
    {
        return RunProbeAsync("registration");
    }

    private static async Task RunProbeAsync(string? mode = null)
    {
        // Built beside this project's own output, whatever the configuration and target framework folders are called.
        var probe = Path.Combine(
            AppContext.BaseDirectory.Replace(
                $"{Path.DirectorySeparatorChar}Blueprint.HttpClient.Tests{Path.DirectorySeparatorChar}",
                $"{Path.DirectorySeparatorChar}Blueprint.HttpClient.ReflectionDisabledProbe{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal),
            "Blueprint.HttpClient.ReflectionDisabledProbe.dll");
        File.Exists(probe).Should().BeTrue("the test project builds the probe");

        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{probe}\" {mode}")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0, "the probe reports its failure on stderr: {0}", error);
    }
}
