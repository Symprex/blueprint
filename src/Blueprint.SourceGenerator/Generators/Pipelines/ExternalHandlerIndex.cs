using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Pre-computed index of all candidate external handler methods in a compilation.
/// Built once via <see cref="HandlerDiscovery.BuildExternalHandlerIndex"/> and reused
/// for every operation — turning O(operations × syntax trees) handler discovery into
/// O(types + methods) index build + O(1) per-operation lookup.
/// </summary>
/// <remarks>
/// The index is scoped to a single compilation and must not be reused across incremental
/// generator passes. At S365 scale (~540 operations, hundreds of handler classes) the
/// dictionary-backed lookup eliminates O(candidates) linear scans per operation.
/// </remarks>
public sealed class ExternalHandlerIndex
{
    private static readonly ImmutableArray<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)> _empty =
        ImmutableArray<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)>.Empty;

    private readonly Dictionary<INamedTypeSymbol, ImmutableArray<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)>> _index;
    private readonly Lazy<IReadOnlyDictionary<string, List<string>>> _gatewayClients;

    /// <summary>
    /// Gets the <c>[BlueprintClient]</c> classes that include each operation, by the operation's metadata name, which an
    /// <c>[Expose&lt;TInner&gt;]</c> operation's generated forward resolves its upstream from. Found once, on first use.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> GatewayClients => this._gatewayClients.Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalHandlerIndex"/> class with the pre-scanned candidates.
    /// </summary>
    /// <param name="candidates">The pre-scanned candidate handler methods.</param>
    /// <param name="compilation">The compilation, from which <see cref="GatewayClients"/> is found on first use.</param>
    internal ExternalHandlerIndex(
        List<(INamedTypeSymbol HandlerClass, IMethodSymbol Method, INamedTypeSymbol OperationType)> candidates,
        Compilation? compilation = null)
    {
        this._gatewayClients = new Lazy<IReadOnlyDictionary<string, List<string>>>(
            () => compilation is null
                ? new Dictionary<string, List<string>>(StringComparer.Ordinal)
                : Modules.Gateway.GatewayForward.BuildClientIndex(compilation));

        var grouping = new Dictionary<INamedTypeSymbol, List<(INamedTypeSymbol, IMethodSymbol)>>(SymbolEqualityComparer.Default);

        foreach (var (handlerClass, method, opType) in candidates)
        {
            if (!grouping.TryGetValue(opType, out var bucket))
            {
                bucket = [];
                grouping[opType] = bucket;
            }

            bucket.Add((handlerClass, method));
        }

        this._index = new Dictionary<INamedTypeSymbol, ImmutableArray<(INamedTypeSymbol, IMethodSymbol)>>(
            grouping.Count,
            SymbolEqualityComparer.Default);

        foreach (var (opType, bucket) in grouping)
        {
            this._index[opType] = bucket.ToImmutableArray();
        }
    }

    /// <summary>
    /// Finds all external handler methods whose first parameter matches the given operation type.
    /// </summary>
    /// <param name="operationType">The operation type to find handlers for.</param>
    /// <returns>An immutable array of matching handler class and method pairs (empty if none found).</returns>
    public ImmutableArray<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)> FindHandlersFor(
        INamedTypeSymbol operationType)
    {
        return this._index.TryGetValue(operationType, out var handlers) ? handlers : _empty;
    }
}
