using System.Text.RegularExpressions;

namespace Blueprint.SourceGenerator.Tests.Scale;

/// <summary>
/// Shared Verify configuration for the Scale report snapshots.
/// </summary>
internal static class ScaleSnapshotVerifyExtensions
{
    private static readonly Regex GeneratorVersionLine = new(
        @"^(?<prefix>\s*""version""\s*:\s*)""\d+\.\d+\.\d+\.\d+""(?<suffix>,?\s*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Scrubs the host assembly's <c>version</c> field out of a Scale report snapshot. That field is set by MinVer
    /// from the assembly's <c>AssemblyVersion</c> and is orthogonal to the build-telemetry/invalidation data the
    /// test is actually asserting, so pinning its current value into the baseline would break the snapshot on every
    /// version change (including the very first git tag) rather than on a genuine manifest regression.
    /// </summary>
    /// <param name="settings">The Verify settings task to scrub.</param>
    /// <returns>The same settings task, with the generator version normalised to a stable placeholder.</returns>
    public static SettingsTask ScrubGeneratorVersion(this SettingsTask settings)
    {
        return settings.ScrubLinesWithReplace(static line =>
        {
            var match = GeneratorVersionLine.Match(line);
            return match.Success
                ? $"{match.Groups["prefix"].Value}\"{{Scrubbed}}\"{match.Groups["suffix"].Value}"
                : line;
        });
    }
}
