using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Pre-computed index of all candidate external handler methods in a compilation.
/// Built once via <see cref="HandlerDiscovery.BuildExternalHandlerIndex"/> and reused
/// for every operation — turning O(operations × syntax trees) handler discovery into
/// O(types + methods) index build + O(candidates) per-operation lookup.
/// </summary>
public sealed class ExternalHandlerIndex
{
    private readonly List<(INamedTypeSymbol HandlerClass, IMethodSymbol Method, INamedTypeSymbol OperationType)> _candidates;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalHandlerIndex"/> class with the pre-scanned candidates.
    /// </summary>
    /// <param name="candidates">The pre-scanned candidate handler methods.</param>
    internal ExternalHandlerIndex(
        List<(INamedTypeSymbol HandlerClass, IMethodSymbol Method, INamedTypeSymbol OperationType)> candidates)
    {
        this._candidates = candidates;
    }

    /// <summary>
    /// Finds all external handler methods whose first parameter matches the given operation type.
    /// </summary>
    /// <param name="operationType">The operation type to find handlers for.</param>
    /// <returns>A list of matching handler class and method pairs.</returns>
    public List<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)> FindHandlersFor(
        INamedTypeSymbol operationType)
    {
        var results = new List<(INamedTypeSymbol, IMethodSymbol)>();

        foreach (var (handlerClass, method, opType) in this._candidates)
        {
            if (SymbolEqualityComparer.Default.Equals(opType, operationType))
            {
                results.Add((handlerClass, method));
            }
        }

        return results;
    }
}
