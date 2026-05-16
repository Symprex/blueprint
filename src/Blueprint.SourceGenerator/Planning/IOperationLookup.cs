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

    /// <summary>
    /// Finds any named type by its metadata name.
    /// </summary>
    /// <param name="metadataName">The fully-qualified metadata name.</param>
    /// <returns>The resolved type symbol, if found.</returns>
    INamedTypeSymbol? FindTypeByMetadataName(string metadataName);

    /// <summary>
    /// Finds all self-link URL templates declared for the given resource type across the compilation.
    /// Returns URL templates from <c>[SelfLink(typeof(resourceType), url)]</c> attributes on operations.
    /// </summary>
    /// <param name="resourceType">The resource type symbol to look up self-link templates for.</param>
    /// <returns>The self-link bindings found, which may be empty if no self-links target this resource type.</returns>
    IReadOnlyList<SelfLinkBindingInfo> FindSelfLinksForResourceType(INamedTypeSymbol resourceType);

    /// <summary>
    /// Gets the semantic model for the given syntax tree, enabling symbol resolution from syntax nodes.
    /// Returns <see langword="null"/> when the implementation does not have access to a compilation.
    /// </summary>
    /// <param name="syntaxTree">The syntax tree to get a semantic model for.</param>
    /// <returns>The semantic model, or <see langword="null"/>.</returns>
    SemanticModel? GetSemanticModel(SyntaxTree syntaxTree);
}

/// <summary>
/// Captures a resolved self-link URL template targeting a specific resource type.
/// </summary>
/// <param name="UrlTemplate">The URL template (e.g., "/templates/{Id}").</param>
/// <param name="ResourceType">The resource type this self-link targets.</param>
/// <param name="DeclaringOperationType">The operation type that declares the self-link.</param>
/// <param name="Rel">The link relation name (defaults to "self" when not overridden).</param>
public sealed record SelfLinkBindingInfo(string UrlTemplate, INamedTypeSymbol ResourceType, INamedTypeSymbol DeclaringOperationType, string Rel = "self");

/// <summary>
/// Compilation-backed operation lookup used by the initial foundation seam.
/// </summary>
/// <param name="compilation">The current compilation.</param>
public sealed class CompilationOperationLookup(Compilation compilation) : IOperationLookup
{
    private const string OperationMetadataName = "Blueprint.Model.IOperation";
    private readonly INamedTypeSymbol? _operationInterface = compilation.GetTypeByMetadataName(OperationMetadataName);
    private Dictionary<INamedTypeSymbol, List<SelfLinkBindingInfo>>? _selfLinkCache;
    private Dictionary<string, INamedTypeSymbol?>? _typeByMetadataNameCache;

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

    /// <inheritdoc />
    public INamedTypeSymbol? FindTypeByMetadataName(string metadataName)
    {
        this._typeByMetadataNameCache ??= new Dictionary<string, INamedTypeSymbol?>(StringComparer.Ordinal);

        if (!this._typeByMetadataNameCache.TryGetValue(metadataName, out var cached))
        {
            cached = compilation.GetTypeByMetadataName(NormalizeMetadataName(metadataName));
            this._typeByMetadataNameCache[metadataName] = cached;
        }

        return cached;
    }

    /// <inheritdoc />
    public IReadOnlyList<SelfLinkBindingInfo> FindSelfLinksForResourceType(INamedTypeSymbol resourceType)
    {
        this.EnsureSelfLinkCache();

        return this._selfLinkCache!.TryGetValue(resourceType, out var bindings)
            ? bindings
            : [];
    }

    /// <inheritdoc />
    public SemanticModel? GetSemanticModel(SyntaxTree syntaxTree)
    {
        return compilation.ContainsSyntaxTree(syntaxTree)
            ? compilation.GetSemanticModel(syntaxTree)
            : null;
    }

    private void EnsureSelfLinkCache()
    {
        if (this._selfLinkCache is not null)
        {
            return;
        }

        this._selfLinkCache = new Dictionary<INamedTypeSymbol, List<SelfLinkBindingInfo>>(SymbolEqualityComparer.Default);

        if (this._operationInterface is null)
        {
            return;
        }

        foreach (var type in this.GetAllTypesFromCompilation())
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.IsGenericType)
            {
                continue;
            }

            if (!type.AllInterfaces.Contains(this._operationInterface, SymbolEqualityComparer.Default))
            {
                continue;
            }

            foreach (var attribute in type.GetAttributes())
            {
                // Match by class name rather than symbol equality because each assembly
                // gets its own internal copy of the source-generated marker attribute.
                if (attribute.AttributeClass?.Name != "SelfLinkAttribute")
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                var resourceTypeArg = attribute.ConstructorArguments[0];
                var urlArg = attribute.ConstructorArguments[1];

                if (resourceTypeArg.Value is not INamedTypeSymbol targetResourceType || urlArg.Value is not string url)
                {
                    continue;
                }

                if (!this._selfLinkCache.TryGetValue(targetResourceType, out var list))
                {
                    list = [];
                    this._selfLinkCache[targetResourceType] = list;
                }

                // Extract Rel from named argument (defaults to "self" when not specified)
                var rel = "self";
                foreach (var namedArg in attribute.NamedArguments)
                {
                    if (namedArg.Key == "Rel" && namedArg.Value.Value is string relValue)
                    {
                        rel = relValue;
                        break;
                    }
                }

                list.Add(new SelfLinkBindingInfo(url, targetResourceType, type, rel));
            }
        }
    }

    private IEnumerable<INamedTypeSymbol> GetAllTypesFromCompilation()
    {
        return GetTypesFromNamespace(compilation.Assembly.GlobalNamespace)
            .Concat(compilation.References
                .Select(compilation.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>()
                .SelectMany(static a => GetTypesFromNamespace(a.GlobalNamespace)));
    }

    private static IEnumerable<INamedTypeSymbol> GetTypesFromNamespace(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            foreach (var type in GetTypesFromNamespace(childNs))
            {
                yield return type;
            }
        }
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

    private static string NormalizeMetadataName(string metadataName)
    {
        return metadataName.StartsWith("global::", StringComparison.Ordinal)
            ? metadataName.Substring("global::".Length)
            : metadataName;
    }
}
