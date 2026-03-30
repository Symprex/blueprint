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
    /// Gets the flattened declared semantic result types for the supplied contract.
    /// </summary>
    /// <param name="declaredContractType">The declared operation result contract.</param>
    /// <param name="operationResultType">The resolved <c>Blueprint.OperationResult</c> base type.</param>
    /// <returns>The flattened semantic result types for the contract.</returns>
    public static ImmutableArray<ITypeSymbol> GetFlattenedSemanticResultTypes(ITypeSymbol declaredContractType, INamedTypeSymbol operationResultType)
    {
        if (TryGetResultsUnionMembers(declaredContractType, out var declaredUnionMembers))
        {
            return declaredUnionMembers;
        }

        return IsOperationResultType(declaredContractType, operationResultType)
            ? [declaredContractType]
            : [];
    }

    /// <summary>
    /// Determines whether the given type is a concrete semantic result type rooted at <c>Blueprint.OperationResult</c>.
    /// </summary>
    /// <param name="candidateType">The candidate result type.</param>
    /// <param name="operationResultType">The resolved <c>Blueprint.OperationResult</c> base type.</param>
    /// <returns>Whether the candidate inherits from the semantic result base type.</returns>
    public static bool IsOperationResultType(ITypeSymbol candidateType, INamedTypeSymbol operationResultType)
    {
        return IsTypeOrDerivedFrom(candidateType, operationResultType);
    }

    /// <summary>
    /// Determines whether a declared <c>Results&lt;T1, T2&gt;</c> member is a supported semantic result type rather than a
    /// nested union or a plain CLR payload type.
    /// </summary>
    /// <param name="memberType">The union member type to inspect.</param>
    /// <param name="operationResultType">The resolved <c>Blueprint.OperationResult</c> base type.</param>
    /// <returns>Whether the member can participate directly in semantic result unions and flattened outcome projection.</returns>
    public static bool IsSupportedResultsUnionMember(ITypeSymbol memberType, INamedTypeSymbol operationResultType)
    {
        return TryGetResultsUnionMembers(memberType, out _) == false
            && IsOperationResultType(memberType, operationResultType);
    }

    /// <summary>
    /// Determines whether the HTTP operation result executor can translate the supplied semantic result type.
    /// </summary>
    /// <param name="resultType">The semantic result type to inspect.</param>
    /// <param name="okResultType">The resolved <c>Blueprint.Runtime.Exec.OkResult</c> base type.</param>
    /// <param name="acceptedResultType">The resolved <c>Blueprint.Runtime.Exec.AcceptedResult</c> base type when available.</param>
    /// <param name="conflictResultType">The resolved <c>Blueprint.Runtime.Exec.ConflictResult</c> base type when available.</param>
    /// <param name="createdResultType">The resolved <c>Blueprint.Runtime.Exec.CreatedResult</c> base type when available.</param>
    /// <param name="noResultType">The resolved <c>Blueprint.Runtime.Exec.NoResultOperationResult</c> type.</param>
    /// <param name="keyOnlyResourceEventResultType">The resolved <c>Blueprint.Runtime.Exec.KeyOnlyResourceEventResult</c> type when available.</param>
    /// <param name="httpNoContentResultType">The resolved <c>Blueprint.Runtime.Http.HttpNoContentResult</c> type when available.</param>
    /// <returns>Whether the current HTTP executor can translate the result type.</returns>
    public static bool IsSupportedByHttpOperationResultExecutor(
        ITypeSymbol resultType,
        INamedTypeSymbol okResultType,
        INamedTypeSymbol? acceptedResultType,
        INamedTypeSymbol? conflictResultType,
        INamedTypeSymbol? createdResultType,
        INamedTypeSymbol noResultType,
        INamedTypeSymbol? keyOnlyResourceEventResultType,
        INamedTypeSymbol? httpNoContentResultType)
    {
        return IsTypeOrDerivedFrom(resultType, okResultType)
            || IsTypeOrDerivedFromOptional(resultType, acceptedResultType)
            || IsTypeOrDerivedFromOptional(resultType, conflictResultType)
            || IsTypeOrDerivedFromOptional(resultType, createdResultType)
            || SymbolEqualityComparer.Default.Equals(resultType, noResultType)
            || SymbolEqualityComparer.Default.Equals(resultType, keyOnlyResourceEventResultType)
            || SymbolEqualityComparer.Default.Equals(resultType, httpNoContentResultType);
    }

    /// <summary>
    /// Tries to map a semantic result type onto the current HTTP executor response shape.
    /// </summary>
    /// <param name="resultType">The semantic result type to inspect.</param>
    /// <param name="okResultType">The resolved <c>Blueprint.Runtime.Exec.OkResult</c> base type.</param>
    /// <param name="acceptedResultType">The resolved <c>Blueprint.Runtime.Exec.AcceptedResult</c> base type when available.</param>
    /// <param name="conflictResultType">The resolved <c>Blueprint.Runtime.Exec.ConflictResult</c> base type when available.</param>
    /// <param name="createdResultType">The resolved <c>Blueprint.Runtime.Exec.CreatedResult</c> base type when available.</param>
    /// <param name="noResultType">The resolved <c>Blueprint.Runtime.Exec.NoResultOperationResult</c> type.</param>
    /// <param name="keyOnlyResourceEventResultType">The resolved <c>Blueprint.Runtime.Exec.KeyOnlyResourceEventResult</c> type when available.</param>
    /// <param name="httpNoContentResultType">The resolved <c>Blueprint.Runtime.Http.HttpNoContentResult</c> type when available.</param>
    /// <param name="statusCode">The HTTP status code for the current executor path.</param>
    /// <param name="hasResponseBody">Whether the current executor path writes a response body.</param>
    /// <returns>Whether the current executor path has a deterministic response shape for the semantic result type.</returns>
    public static bool TryGetHttpOperationResultResponseShape(
        ITypeSymbol resultType,
        INamedTypeSymbol okResultType,
        INamedTypeSymbol? acceptedResultType,
        INamedTypeSymbol? conflictResultType,
        INamedTypeSymbol? createdResultType,
        INamedTypeSymbol noResultType,
        INamedTypeSymbol? keyOnlyResourceEventResultType,
        INamedTypeSymbol? httpNoContentResultType,
        out int statusCode,
        out bool hasResponseBody)
    {
        if (IsTypeOrDerivedFrom(resultType, okResultType))
        {
            statusCode = 200;
            hasResponseBody = true;
            return true;
        }

        if (IsTypeOrDerivedFromOptional(resultType, acceptedResultType))
        {
            statusCode = 202;
            hasResponseBody = true;
            return true;
        }

        if (IsTypeOrDerivedFromOptional(resultType, conflictResultType))
        {
            statusCode = 409;
            hasResponseBody = true;
            return true;
        }

        if (IsTypeOrDerivedFromOptional(resultType, createdResultType))
        {
            statusCode = 201;
            hasResponseBody = true;
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(resultType, noResultType))
        {
            statusCode = 200;
            hasResponseBody = false;
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(resultType, keyOnlyResourceEventResultType))
        {
            statusCode = 201;
            hasResponseBody = false;
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(resultType, httpNoContentResultType))
        {
            statusCode = 204;
            hasResponseBody = false;
            return true;
        }

        statusCode = default;
        hasResponseBody = default;
        return false;
    }

    /// <summary>
    /// Determines whether a symbol is the same as or derives from the supplied base type.
    /// </summary>
    /// <param name="candidateType">The candidate type to inspect.</param>
    /// <param name="baseType">The base type to compare against.</param>
    /// <returns>Whether the candidate is the same type as or derives from the base type.</returns>
    public static bool IsTypeOrDerivedFrom(ITypeSymbol candidateType, INamedTypeSymbol baseType)
    {
        for (var current = candidateType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeOrDerivedFromOptional(ITypeSymbol candidateType, INamedTypeSymbol? baseType)
    {
        return baseType is not null && IsTypeOrDerivedFrom(candidateType, baseType);
    }
}
