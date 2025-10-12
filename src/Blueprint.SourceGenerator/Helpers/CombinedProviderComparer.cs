namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Compares CompiledModel by comparing the Model only, as
/// we want to ignore the Compilation to avoid it being run on every input.
/// </summary>
internal class CombinedProviderComparer<TSource> : IEqualityComparer<(TSource Model, Microsoft.CodeAnalysis.Compilation Compilation)>
{
    public static readonly CombinedProviderComparer<TSource> Instance = new();

    public bool Equals((TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) x, (TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) y)
    {
        return x.Model?.Equals(y.Model) == true;
    }

    public int GetHashCode((TSource Model, Microsoft.CodeAnalysis.Compilation Compilation) obj)
    {
        return obj.Model?.GetHashCode() ?? 0;
    }
}
