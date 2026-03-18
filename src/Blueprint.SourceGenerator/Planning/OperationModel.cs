using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// A compile-time view of an operation.
/// </summary>
/// <param name="TypeSymbol">The Roslyn symbol for the operation.</param>
/// <param name="TypeName">The operation type name.</param>
/// <param name="Namespace">The containing namespace.</param>
public sealed record OperationModel(
    INamedTypeSymbol TypeSymbol,
    string TypeName,
    string Namespace)
{
    /// <summary>
    /// Gets the fully-qualified metadata name of the operation.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(this.Namespace)
        ? this.TypeName
        : this.Namespace + "." + this.TypeName;

    /// <summary>
    /// Creates an operation model from a Roslyn symbol.
    /// </summary>
    /// <param name="typeSymbol">The operation symbol.</param>
    /// <returns>The operation model.</returns>
    public static OperationModel Create(INamedTypeSymbol typeSymbol)
    {
        return new(typeSymbol, typeSymbol.Name, typeSymbol.ContainingNamespace.ToDisplayString());
    }
}
