using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Helpers;

/// <summary>
/// Shared helpers for reading declared operation result contracts and semantic result shapes from Roslyn symbols.
/// </summary>
public static class OperationResultContractHelper
{
    /// <summary>
    /// Gets the declared operation result contract from the generic Blueprint operation interface, if one exists.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The declared result contract type, if present.</returns>
    public static ITypeSymbol? GetDeclaredOperationResultType(INamedTypeSymbol operationType)
    {
        var operationResultInterface = operationType.AllInterfaces.FirstOrDefault(
            static candidate => candidate.TypeArguments.Length == 1
                && candidate.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
                && (candidate.Name == "ICommand" || candidate.Name == "IQuery" || candidate.Name == "IOperation"));

        return operationResultInterface?.TypeArguments[0];
    }

    /// <summary>
    /// Tries to unwrap a declared <c>Results&lt;T1, T2&gt;</c> contract into its member types.
    /// </summary>
    /// <param name="candidateType">The candidate contract type.</param>
    /// <param name="memberTypes">The flattened union members.</param>
    /// <returns>Whether the candidate is a supported two-member results union.</returns>
    public static bool TryGetResultsUnionMembers(ITypeSymbol candidateType, out ImmutableArray<ITypeSymbol> memberTypes)
    {
        if (candidateType is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && namedType.ContainingNamespace.ToDisplayString() == "Blueprint.Model"
            && namedType.Name == "Results"
            && namedType.TypeArguments.Length == 2)
        {
            memberTypes = namedType.TypeArguments;
            return true;
        }

        memberTypes = [];
        return false;
    }

    /// <summary>
    /// Determines whether the given type is a concrete semantic result type rooted at <c>Blueprint.OperationResult</c>.
    /// </summary>
    /// <param name="candidateType">The candidate result type.</param>
    /// <param name="operationResultType">The resolved <c>Blueprint.OperationResult</c> base type.</param>
    /// <returns>Whether the candidate inherits from the semantic result base type.</returns>
    public static bool IsOperationResultType(ITypeSymbol candidateType, INamedTypeSymbol operationResultType)
    {
        for (var current = candidateType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, operationResultType))
            {
                return true;
            }
        }

        return false;
    }
}
