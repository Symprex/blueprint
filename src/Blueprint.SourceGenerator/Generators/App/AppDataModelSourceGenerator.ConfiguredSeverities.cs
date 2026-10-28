using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.SourceGenerator.Generators.App;

/// <content>
/// The severities a consumer configured for BP012 and BP013.
/// </content>
public sealed partial class AppDataModelSourceGenerator
{
    /// <summary>
    /// A configured severity: whether to report the diagnostic at all and, if so, at what severity.
    /// </summary>
    /// <param name="Report"><see langword="false" /> for <c>none</c>.</param>
    /// <param name="Severity">The severity to report at.</param>
    private readonly record struct ConfiguredSeverity(bool Report, DiagnosticSeverity Severity);

    /// <summary>
    /// The <c>blueprint_diagnostic.BP012.severity</c> and <c>blueprint_diagnostic.BP013.severity</c> values in effect for
    /// the app's file, as raw strings so the value stays equatable and the pipeline cacheable.
    /// </summary>
    /// <remarks>
    /// The compiler applies a global configuration's (<c>.globalconfig</c>) <c>dotnet_diagnostic.&lt;id&gt;.severity</c> to a
    /// generator's diagnostics, but not an <c>.editorconfig</c>'s <c>[*.cs]</c> section, which is where a consumer phases a
    /// new rule in (warning first), and it does not pass that key on to the generator either. So the generator reads
    /// Blueprint's own key, which the compiler does pass on, from the options of the file declaring the app. Only the two
    /// open-typed-member rules are configurable this way; they are the ones an existing codebase trips on upgrade.
    /// </remarks>
    /// <param name="Bp012">The configured BP012 severity, or <see langword="null" />.</param>
    /// <param name="Bp013">The configured BP013 severity, or <see langword="null" />.</param>
    private sealed record ConfiguredSeverities(string? Bp012, string? Bp013)
    {
        public static ConfiguredSeverities None { get; } = new(null, null);

        public static ConfiguredSeverities Read(AnalyzerConfigOptions options)
        {
            return new ConfiguredSeverities(Get(options, "BP012"), Get(options, "BP013"));
        }

        // Not dotnet_diagnostic.<id>.severity: the compiler consumes that key itself and never passes it to a generator.
        private static string? Get(AnalyzerConfigOptions options, string id)
        {
            return options.TryGetValue($"blueprint_diagnostic.{id}.severity", out var value) ? value : null;
        }

        /// <summary>
        /// The configured severity for a diagnostic id, or <see langword="null" /> to report it at its default.
        /// </summary>
        public ConfiguredSeverity? For(string id)
        {
            var value = id switch
            {
                "BP012" => this.Bp012,
                "BP013" => this.Bp013,
                _ => null,
            };

            return value?.Trim().ToLowerInvariant() switch
            {
                "error" => new ConfiguredSeverity(true, DiagnosticSeverity.Error),
                "warning" => new ConfiguredSeverity(true, DiagnosticSeverity.Warning),
                "suggestion" => new ConfiguredSeverity(true, DiagnosticSeverity.Info),
                "silent" => new ConfiguredSeverity(true, DiagnosticSeverity.Hidden),
                "none" => new ConfiguredSeverity(false, DiagnosticSeverity.Hidden),
                _ => null,
            };
        }
    }
}
