using System.Text.Json;

namespace Blueprint.Codemod;

/// <summary>
/// Runs the codemod dry-run entry point so migration tooling can emit the current activation-diagnostics report
/// contract before any apply-mode transforms exist.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the dry-run collector for a solution path.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (!TryGetOption(args, "--solution", out var solutionPath))
        {
            Console.Error.WriteLine("Missing required '--solution <path>' option.");
            return 1;
        }

        _ = TryGetOption(args, "--diagnostic-filter", out var diagnosticFilter);
        var applyMode = HasFlag(args, "--apply");

        if (applyMode)
        {
            var applier = new TransformationApplier();
            await applier.ApplyAsync(
                new ApplyModeOptions(
                    solutionPath,
                    diagnosticFilter ?? "BPMIG*"));
        }

        var runner = new SolutionAnalysisRunner();
        var report = await runner.DryRunAsync(
            new SolutionAnalysisOptions(
                solutionPath,
                diagnosticFilter ?? "BPMIG*"));

        Console.Out.WriteLine(JsonSerializer.Serialize(report));
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
