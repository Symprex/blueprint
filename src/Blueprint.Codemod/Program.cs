using System.Text.Json;
using Blueprint.Migration.Analyzers;

namespace Blueprint.Codemod;

/// <summary>
/// Runs the codemod entry point so migration tooling can emit the canonical migration-diagnostics report to
/// standard output and, when requested, write reviewable artifact files beside dry-run or apply-mode execution.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the dry-run collector or apply-mode transform workflow for a solution path.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            return await RunAsync(
                args,
                static (options, cancellationToken) =>
                {
                    var applier = new TransformationApplier();
                    return applier.ApplyAsync(options, cancellationToken);
                },
                static (options, cancellationToken) =>
                {
                    var runner = new SolutionAnalysisRunner();
                    return runner.DryRunAsync(options, cancellationToken);
                },
                static (report, outputDirectory, cancellationToken) =>
                {
                    var artifactWriter = new CodemodArtifactWriter();
                    return artifactWriter.WriteAsync(report, outputDirectory, cancellationToken);
                },
                Console.Out,
                Console.Error,
                cancellationSource.Token);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Runs the codemod orchestration against injected collaborators so tests can prove CLI behavior without reflection or global console capture.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="applyAsync">The apply-mode implementation.</param>
    /// <param name="dryRunAsync">The dry-run implementation.</param>
    /// <param name="writeArtifactsAsync">The artifact writer implementation.</param>
    /// <param name="standardOutput">The standard-output writer that receives the canonical JSON report.</param>
    /// <param name="standardError">The standard-error writer that receives argument validation failures.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process exit code.</returns>
    internal static async Task<int> RunAsync(
        string[] args,
        Func<ApplyModeOptions, CancellationToken, Task> applyAsync,
        Func<SolutionAnalysisOptions, CancellationToken, Task<MigrationReport>> dryRunAsync,
        Func<MigrationReport, string, CancellationToken, Task> writeArtifactsAsync,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(applyAsync);
        ArgumentNullException.ThrowIfNull(dryRunAsync);
        ArgumentNullException.ThrowIfNull(writeArtifactsAsync);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (!TryGetOption(args, "--solution", out var solutionPath))
        {
            await standardError.WriteLineAsync("Missing required '--solution <path>' option.");
            return 1;
        }

        _ = TryGetOption(args, "--diagnostic-filter", out var diagnosticFilter);
        _ = TryGetOption(args, "--output", out var outputDirectory);
        var applyMode = HasFlag(args, "--apply");

        if (applyMode)
        {
            await applyAsync(
                new ApplyModeOptions(
                    solutionPath,
                    diagnosticFilter ?? "BPMIG*"),
                cancellationToken);
        }

        var report = await dryRunAsync(
            new SolutionAnalysisOptions(
                solutionPath,
                diagnosticFilter ?? "BPMIG*"),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            await writeArtifactsAsync(report, outputDirectory, cancellationToken);
        }

        await standardOutput.WriteLineAsync(JsonSerializer.Serialize(report));
        return 0;
    }

    private static bool TryGetOption(string[] args, string optionName, out string value)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.Ordinal))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool HasFlag(string[] args, string flagName)
    {
        return args.Any(argument => string.Equals(argument, flagName, StringComparison.Ordinal));
    }
}
