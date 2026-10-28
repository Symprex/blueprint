using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Reports a pipeline's diagnostics against the syntax trees they came from, so a consumer can suppress one occurrence
/// with <c>#pragma warning disable</c>.
/// </summary>
/// <remarks>
/// Pipeline models hold <see cref="LocationInfo"/> rather than a syntax tree so they stay cacheable, which leaves the
/// tree to be found again when the diagnostic is reported. A source output that does not otherwise read the compilation
/// reports through this separate output instead, so emitting source never waits on the compilation.
/// </remarks>
internal static class DiagnosticOutput
{
    /// <summary>
    /// Registers an output that reports each item's diagnostics bound to the current compilation's syntax trees.
    /// </summary>
    /// <param name="context">The generator initialisation context.</param>
    /// <param name="diagnostics">The diagnostics each pipeline item must report.</param>
    public static void RegisterDiagnosticOutput(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EquatableList<DiagnosticInfo>> diagnostics)
    {
        var withCompilation = diagnostics
            .Where(static d => d.Count > 0)
            .Combine(context.CompilationProvider)
            .WithComparer(DiagnosticsWithCompilationComparer.Instance);

        context.RegisterSourceOutput(
            withCompilation,
            static (spc, input) =>
            {
                var (itemDiagnostics, compilation) = input;

                foreach (var diagnostic in itemDiagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic(compilation));
                }
            });
    }

    /// <summary>
    /// Treats two items as equal while their diagnostics are equal and every file they point into is still the same
    /// syntax tree, so the output replays from cache until that file is edited and is then reported against the new tree.
    /// </summary>
    private sealed class DiagnosticsWithCompilationComparer : IEqualityComparer<(EquatableList<DiagnosticInfo> Diagnostics, Compilation Compilation)>
    {
        public static readonly DiagnosticsWithCompilationComparer Instance = new();

        public bool Equals(
            (EquatableList<DiagnosticInfo> Diagnostics, Compilation Compilation) x,
            (EquatableList<DiagnosticInfo> Diagnostics, Compilation Compilation) y)
        {
            if (!x.Diagnostics.Equals(y.Diagnostics))
            {
                return false;
            }

            foreach (var diagnostic in x.Diagnostics)
            {
                if (diagnostic.Location is { } location
                    && !ReferenceEquals(location.FindSyntaxTree(x.Compilation), location.FindSyntaxTree(y.Compilation)))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode((EquatableList<DiagnosticInfo> Diagnostics, Compilation Compilation) obj)
        {
            return obj.Diagnostics.GetHashCode();
        }
    }
}
