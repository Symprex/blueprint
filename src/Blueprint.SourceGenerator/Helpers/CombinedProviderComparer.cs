namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Compares combined incremental values by their model component so Roslyn recompilations do not force extra work.
/// </summary>
/// <typeparam name="TSource">The model type carried alongside the compilation.</typeparam>
internal class CombinedProviderComparer<TSource> : IEqualityComparer<(TSource Model, Microsoft.CodeAnalysis.Compilation Compilation)>
{
    /// <summary>
    /// Reusable comparer instance for the current model type.
    /// </summary>
    public static readonly CombinedProviderComparer<TSource> Instance = new();

    /// <summary>
    /// Determines whether two combined values carry equivalent model instances.
    /// </summary>
    /// <param name="x">The first combined value.</param>
    /// <param name="y">The second combined value.</param>
    /// <returns><c>true</c> when the model portions are equal; otherwise, <c>false</c>.</returns>
    public bool Equals((TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) x, (TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) y)
    {
        return x.Model?.Equals(y.Model) == true;
    }

    /// <summary>
    /// Computes a hash code from the model portion of the combined value.
    /// </summary>
    /// <param name="obj">The combined value to hash.</param>
    /// <returns>A hash code derived from the model.</returns>
    public int GetHashCode((TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) obj)
    {
        return obj.Model?.GetHashCode() ?? 0;
    }
}
