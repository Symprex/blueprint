using Blueprint.SourceGenerator.Helpers;
using Blueprint.SourceGenerator.Planning;

using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Modules;

/// <summary>
/// Discovers <c>Expandable&lt;T&gt;</c> properties on operation response types at compile time
/// and generates post-execution expansion calls that resolve the appropriate
/// <c>IResourceExpander</c> from DI at runtime. Properties marked <c>[AlwaysExpand]</c>
/// expand unconditionally; all others expand only when the client requests
/// <c>?expand=PropertyPath</c>.
/// </summary>
/// <remarks>
/// <para>
/// This module replaces the legacy <c>ResourceExpandMiddleware</c> property-walker with
/// compile-time discovery: response types are walked at source-generation time rather than
/// via runtime reflection, so the generated pipeline already knows every expandable property
/// path and whether it is enumerable, always-expanded, or client-requested.
/// </para>
/// <para>
/// The actual expansion (resolving the <c>IResourceExpander</c> from DI and calling
/// <c>ExpandAsync</c>) remains a runtime concern because expander registration is DI-driven.
/// The runtime helpers in <c>Blueprint.Runtime.Expansion.ResourceExpansionRuntime</c> handle
/// query-parameter parsing and expander resolution.
/// </para>
/// </remarks>
public sealed class ResourceExpansionModule : IBlueprintSourceGeneratorModule, IPipelineContributor
{
    /// <inheritdoc />
    public string ModuleId => nameof(ResourceExpansionModule);

    /// <inheritdoc />
    public BlueprintModuleCategory Category => BlueprintModuleCategory.Core;

    /// <inheritdoc />
    public string ContributorId => nameof(ResourceExpansionModule);

    /// <inheritdoc />
    public int Order => 950;

    /// <inheritdoc />
    public bool IsUnique => true;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [nameof(HandlerExecutionModule)];

    /// <inheritdoc />
    public void ContributeToPipeline(PipelineContributorContext context)
    {
        // No handler means no response type to walk
        if (context.Handler is null)
        {
            return;
        }

        // Get the declared response type from the operation contract (IQuery<T>, ICommand<T>)
        var declaredResponseType = OperationResultContractHelper.GetDeclaredOperationResultType(
            context.Operation.TypeSymbol);

        if (declaredResponseType is not INamedTypeSymbol responseTypeSymbol)
        {
            return;
        }

        // Resolve Expandable<T> from the compilation
        var expandableOpenGeneric = context.FindTypeByMetadataName("Blueprint.Model.Expandable`1");

        if (expandableOpenGeneric is null)
        {
            return;
        }

        // If the declared response type is a Results<T1, T2> union, extract the content types
        var responseTypes = new List<INamedTypeSymbol>();

        var operationResultType = context.FindTypeByMetadataName("Blueprint.OperationResult");

        if (operationResultType is not null
            && OperationResultContractHelper.TryGetResultsUnionMembers(declaredResponseType, out var unionMembers))
        {
            foreach (var member in unionMembers)
            {
                if (member is INamedTypeSymbol namedMember
                    && !OperationResultContractHelper.IsOperationResultType(namedMember, operationResultType))
                {
                    responseTypes.Add(namedMember);
                }
            }
        }
        else
        {
            responseTypes.Add(responseTypeSymbol);
        }

        // Resolve the AlwaysExpand attribute
        var alwaysExpandAttributeType = context.FindTypeByMetadataName("Blueprint.Model.Attributes.AlwaysExpandAttribute");

        // Walk all response types for expandable properties
        var allExpandableProperties = new List<ExpandablePropertyInfo>();

        foreach (var responseType in responseTypes)
        {
            // Skip OkResult, NoResult, etc. — only walk concrete content types
            if (operationResultType is not null
                && OperationResultContractHelper.IsOperationResultType(responseType, operationResultType))
            {
                continue;
            }

            var properties = FindExpandableProperties(
                responseType,
                expandableOpenGeneric,
                alwaysExpandAttributeType,
                currentPath: null,
                visited: []);

            foreach (var prop in properties)
            {
                allExpandableProperties.Add(prop with { ResponseTypeFullName = responseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) });
            }
        }

        if (allExpandableProperties.Count == 0)
        {
            return;
        }

        // Require the handler result variable and mark async
        context.RequireVariable(
            new VariableKey(HandlerExecutionModule.HandlerResultTypeName, HandlerExecutionModule.HandlerResultPurpose),
            requiredBy: PipelineStage.PostExecution);

        context.RequireAsync();

        // Capture for the closure
        var expandableProps = allExpandableProperties;

        context.AddRenderStep(PipelineStage.PostExecution, renderContext => RenderExpansion(renderContext, expandableProps));
    }

    private static void RenderExpansion(PipelineRenderContext context, List<ExpandablePropertyInfo> expandableProperties)
    {
        context.WriteBlankLine();

        // Group by response type so we emit one cast per response type
        var byResponseType = expandableProperties
            .GroupBy(static p => p.ResponseTypeFullName)
            .ToList();

        foreach (var group in byResponseType)
        {
            var responseTypeFullName = group.Key;

            // Use a stable suffix derived from the type name to avoid variable name collisions
            var varSuffix = GetStableTypeSuffix(responseTypeFullName);
            var contentVarName = $"expansionContent_{varSuffix}";
            var okResultVarName = $"okResult_{varSuffix}";

            // Cast handler result to the typed OkResult to access Content
            context.If(
                $"handlerResult is global::Blueprint.Runtime.Exec.OkResult<{responseTypeFullName}> {okResultVarName}",
                branch =>
                {
                    branch.DeclareVariable(contentVarName, $"{okResultVarName}.Content");

                    branch.If(
                        $"{contentVarName} != null",
                        innerBranch =>
                        {
                            foreach (var prop in group)
                            {
                                // Build null-safe property access path (a?.B?.C)
                                var accessPath = BuildNullSafeAccessPath(contentVarName, prop.PropertyPath);

                                if (prop.AlwaysExpand)
                                {
                                    // Unconditional expansion
                                    innerBranch.WriteStatement(
                                        $"await global::Blueprint.Runtime.Expansion.ResourceExpansionRuntime.ExpandAsync(" +
                                        $"context, handlerResult, {accessPath}, " +
                                        $"typeof({prop.ExpandableResourceTypeName}), " +
                                        $"{(prop.IsEnumerable ? "true" : "false")});");
                                }
                                else
                                {
                                    // Client-requested expansion via ?expand= query parameter
                                    innerBranch.WriteStatement(
                                        $"await global::Blueprint.Runtime.Expansion.ResourceExpansionRuntime.TryExpandAsync(" +
                                        $"context, handlerResult, {accessPath}, " +
                                        $"\"{prop.PropertyPath}\", " +
                                        $"typeof({prop.ExpandableResourceTypeName}), " +
                                        $"{(prop.IsEnumerable ? "true" : "false")});");
                                }
                            }
                        });
                });
        }
    }

    private static string BuildNullSafeAccessPath(string rootVariable, string dotSeparatedPath)
    {
        var segments = dotSeparatedPath.Split('.');
        var result = rootVariable;

        for (var i = 0; i < segments.Length; i++)
        {
            result = $"{result}?.{segments[i]}";
        }

        return result;
    }

    /// <summary>
    /// Produces a stable alphanumeric suffix from a fully qualified type name so generated variable
    /// names are deterministic across runs (unlike <see cref="string.GetHashCode"/> which is randomised).
    /// </summary>
    private static string GetStableTypeSuffix(string fullyQualifiedTypeName)
    {
        unchecked
        {
            var hash = 2166136261;

            foreach (var c in fullyQualifiedTypeName)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash.ToString("X8");
        }
    }

    private static List<ExpandablePropertyInfo> FindExpandableProperties(
        INamedTypeSymbol type,
        INamedTypeSymbol expandableOpenGeneric,
        INamedTypeSymbol? alwaysExpandAttribute,
        string? currentPath,
        HashSet<INamedTypeSymbol> visited)
    {
        // Prevent infinite recursion on circular type references
        if (!visited.Add(type))
        {
            return [];
        }

        var results = new List<ExpandablePropertyInfo>();

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            // Only public, instance, readable properties
            if (property.DeclaredAccessibility != Accessibility.Public
                || property.IsStatic
                || property.IsWriteOnly)
            {
                continue;
            }

            var propertyType = property.Type;
            var propertyPath = currentPath is null ? property.Name : $"{currentPath}.{property.Name}";

            // Unwrap nullable
            if (propertyType is INamedTypeSymbol namedPropType
                && namedPropType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                propertyType = namedPropType.TypeArguments[0];
            }

            // Check if it's an enumerable of Expandable<T> (List<Expandable<T>>, IEnumerable<Expandable<T>>, etc.)
            var (isExpandable, isEnumerable, expandableResourceType) = CheckExpandableType(propertyType, expandableOpenGeneric);

            if (isExpandable)
            {
                var alwaysExpand = alwaysExpandAttribute is not null
                    && property.GetAttributes().Any(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, alwaysExpandAttribute));

                results.Add(new ExpandablePropertyInfo(
                    PropertyPath: propertyPath,
                    IsEnumerable: isEnumerable,
                    AlwaysExpand: alwaysExpand,
                    ExpandableResourceTypeName: expandableResourceType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ResponseTypeFullName: string.Empty)); // Filled in by caller

                continue;
            }

            // Skip primitive types and System.* namespaces — don't recurse into them
            if (propertyType.SpecialType != SpecialType.None)
            {
                continue;
            }

            if (propertyType is INamedTypeSymbol namedType)
            {
                var ns = namedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                if (ns.StartsWith("System", StringComparison.Ordinal))
                {
                    continue;
                }

                // Recurse into non-system complex types
                var subProperties = FindExpandableProperties(
                    namedType,
                    expandableOpenGeneric,
                    alwaysExpandAttribute,
                    propertyPath,
                    visited);

                results.AddRange(subProperties);
            }
        }

        // Allow revisiting this type from different paths
        visited.Remove(type);

        return results;
    }

    private static (bool IsExpandable, bool IsEnumerable, ITypeSymbol? ResourceType) CheckExpandableType(
        ITypeSymbol type,
        INamedTypeSymbol expandableOpenGeneric)
    {
        // Direct Expandable<T>
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expandableOpenGeneric))
        {
            return (true, false, named.TypeArguments[0]);
        }

        // Check for collection types: T[], List<T>, IEnumerable<T>, IList<T>, IReadOnlyList<T>, etc.
        ITypeSymbol? elementType = null;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
        }
        else if (type is INamedTypeSymbol collectionType && collectionType.IsGenericType)
        {
            var genericDef = collectionType.OriginalDefinition;
            var defName = genericDef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (defName is "global::System.Collections.Generic.List<T>"
                or "global::System.Collections.Generic.IList<T>"
                or "global::System.Collections.Generic.IReadOnlyList<T>"
                or "global::System.Collections.Generic.ICollection<T>"
                or "global::System.Collections.Generic.IReadOnlyCollection<T>"
                or "global::System.Collections.Generic.IEnumerable<T>")
            {
                elementType = collectionType.TypeArguments[0];
            }
        }

        if (elementType is INamedTypeSymbol namedElement
            && namedElement.IsGenericType
            && SymbolEqualityComparer.Default.Equals(namedElement.OriginalDefinition, expandableOpenGeneric))
        {
            return (true, true, namedElement.TypeArguments[0]);
        }

        return (false, false, null);
    }

    /// <summary>
    /// Compile-time metadata about a discovered <c>Expandable&lt;T&gt;</c> property.
    /// </summary>
    private sealed record ExpandablePropertyInfo(
        string PropertyPath,
        bool IsEnumerable,
        bool AlwaysExpand,
        string ExpandableResourceTypeName,
        string ResponseTypeFullName);
}
