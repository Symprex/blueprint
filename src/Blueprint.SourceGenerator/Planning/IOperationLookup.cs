using JetBrains.Annotations;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Planning;

/// <summary>
/// Resolves operation models from the current compilation so contributors can reason about related operations.
/// </summary>
[UsedImplicitly]
public interface IOperationLookup
{
    /// <summary>
    /// Finds an operation by its metadata name.
    /// </summary>
    /// <param name="metadataName">The fully-qualified metadata name.</param>
    /// <returns>The operation model, if found.</returns>
    OperationModel? FindByMetadataName(string metadataName);

    /// <summary>
    /// Finds an operation by its symbol.
    /// </summary>
    /// <param name="typeSymbol">The candidate symbol.</param>
    /// <returns>The operation model, if found.</returns>
    OperationModel? Find(INamedTypeSymbol typeSymbol);
}

/// <summary>
/// Compilation-backed operation lookup used by the initial foundation seam.
/// </summary>
/// <param name="compilation">The current compilation.</param>
public sealed class CompilationOperationLookup(Compilation compilation) : IOperationLookup
{
    private const string OperationMetadataName = "Blueprint.Model.IOperation";
    private readonly INamedTypeSymbol? _operationInterface = compilation.GetTypeByMetadataName(OperationMetadataName);

    /// <inheritdoc />
    public OperationModel? FindByMetadataName(string metadataName)
    {
        return this.TryCreate(compilation.GetTypeByMetadataName(metadataName));
    }

    /// <inheritdoc />
    public OperationModel? Find(INamedTypeSymbol typeSymbol)
    {
        return this.TryCreate(typeSymbol);
    }

    private OperationModel? TryCreate(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null || this._operationInterface is null)
        {
            return null;
        }

        if (typeSymbol.AllInterfaces.Contains(this._operationInterface, SymbolEqualityComparer.Default) == false)
        {
            return null;
        }

        return OperationModel.Create(typeSymbol);
    }
}
