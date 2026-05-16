using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Blueprint.Migration.Analyzers.Analyzers;

/// <summary>
/// Detects files that use Blueprint types which moved from the root <c>Blueprint</c> namespace
/// to sub-namespaces in source-generated Blueprint. Reports one diagnostic per missing namespace
/// per document, enabling the companion code fix to add exactly the required <c>using</c> directives.
/// </summary>
/// <remarks>
/// <para>
/// In legacy Blueprint, types like <c>ApiOperationContext</c> and <c>ApiOperationDescriptor</c>
/// lived in the root <c>Blueprint</c> namespace. In source-generated Blueprint they moved to
/// <c>Blueprint.Runtime.Exec</c>, <c>Blueprint.Model</c>, <c>Blueprint.Authorization</c>, etc.
/// Files with <c>using Blueprint;</c> that reference these types need additional using directives.
/// </para>
/// <para>
/// Detection works by scanning identifiers whose simple name matches a known relocated type,
/// then checking via the semantic model whether the identifier is either unresolved (error type)
/// or still resolving to a type in the old root <c>Blueprint</c> namespace. Results are aggregated
/// per destination namespace to produce minimal diagnostics.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RelocatedTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Maps simple type names that moved out of the root <c>Blueprint</c> namespace to their
    /// new destination namespace. Only types actually observed in S365 usage are included.
    /// </summary>
    internal static readonly ImmutableDictionary<string, string> RelocatedTypes =
        ImmutableDictionary.CreateRange(new[]
        {
            // Blueprint → Blueprint.Runtime.Exec
            new KeyValuePair<string, string>("ApiOperationContext", "Blueprint.Runtime.Exec"),
            new KeyValuePair<string, string>("IHttpOperationContext", "Blueprint.Runtime.Exec"),

            // Blueprint → Blueprint.Model
            new KeyValuePair<string, string>("ApiOperationDescriptor", "Blueprint.Model"),
            new KeyValuePair<string, string>("ApiDataModel", "Blueprint.Model"),

            // Blueprint → Blueprint.Authorization
            new KeyValuePair<string, string>("ExecutionAllowed", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("ExecutionAllowedFailureType", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IApiAuthoriser", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IApiAuthoriserAggregator", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IOperationAuthoriser", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("ILinkVisibilityAuthoriser", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IUserAuthorisationContext", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IUserAuthorisationContextFactory", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("IResourceAuthorizationProvider", "Blueprint.Authorization"),
            new KeyValuePair<string, string>("ScopePairClaimAuthorizationProvider", "Blueprint.Authorization"),

            // Blueprint → Blueprint.Http
            new KeyValuePair<string, string>("BlueprintJsonOptions", "Blueprint.Http"),
        });

    /// <summary>
    /// Pre-computed set of all simple type names for fast syntax-level filtering.
    /// </summary>
    private static readonly ImmutableHashSet<string> RelocatedTypeNames =
        ImmutableHashSet.CreateRange(RelocatedTypes.Keys);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MigrationDiagnosticDescriptors.RelocatedTypeNeedUsing);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        var root = context.SemanticModel.SyntaxTree.GetRoot(context.CancellationToken);

        // Collect existing using directives to skip namespaces already imported
        var existingUsings = new HashSet<string>();
        var hasRootBlueprintUsing = false;

        foreach (var node in root.DescendantNodes())
        {
            if (node is not UsingDirectiveSyntax usingDirective)
            {
                continue;
            }

            if (usingDirective.StaticKeyword != default || usingDirective.Alias is not null)
            {
                continue;
            }

            var ns = usingDirective.Name?.ToString();

            if (ns != null)
            {
                existingUsings.Add(ns);

                if (ns == "Blueprint")
                {
                    hasRootBlueprintUsing = true;
                }
            }
        }

        // Only analyze files that have "using Blueprint;" — these are the ones that relied
        // on types being in the root namespace.
        if (!hasRootBlueprintUsing)
        {
            return;
        }

        // Scan identifiers to find relocated type usage. Track which destination namespaces
        // are needed and which identifier triggered the first detection (for the diagnostic location).
        var missingNamespaces = new Dictionary<string, Location>();

        foreach (var node2 in root.DescendantNodes())
        {
            if (node2 is not IdentifierNameSyntax identifier)
            {
                continue;
            }

            context.CancellationToken.ThrowIfCancellationRequested();

            var name = identifier.Identifier.ValueText;

            if (!RelocatedTypeNames.Contains(name))
            {
                continue;
            }

            if (!RelocatedTypes.TryGetValue(name, out var destinationNamespace))
            {
                continue;
            }

            // Already have the required using
            if (existingUsings.Contains(destinationNamespace))
            {
                continue;
            }

            // Already recorded this namespace
            if (missingNamespaces.ContainsKey(destinationNamespace))
            {
                continue;
            }

            // Use semantic model to verify this identifier actually refers to (or should
            // refer to) a Blueprint type — not a local type with the same name.
            var symbolInfo = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken);

            if (symbolInfo.Symbol is INamedTypeSymbol resolved)
            {
                // Type resolves — check if it's from the old Blueprint root namespace
                // (meaning the old assembly is still referenced) or already in the right namespace.
                var containingNs = resolved.ContainingNamespace?.ToDisplayString();

                if (containingNs == "Blueprint")
                {
                    // Still resolving from old Blueprint — needs the new using
                    missingNamespaces[destinationNamespace] = identifier.GetLocation();
                }

                // If it resolves to the correct destination namespace, no action needed.
                // If it resolves to something else entirely (local type), skip.
            }
            else if (symbolInfo.Symbol is null)
            {
                // Unresolved — check candidates to see if any are from Blueprint
                var hasCandidate = false;

                foreach (var candidate in symbolInfo.CandidateSymbols)
                {
                    if (candidate is INamedTypeSymbol t && t.ContainingNamespace?.ToDisplayString() == "Blueprint")
                    {
                        hasCandidate = true;
                        break;
                    }
                }

                if (hasCandidate || symbolInfo.CandidateSymbols.IsEmpty)
                {
                    // Either has candidates from old Blueprint, or totally unresolved
                    // (e.g. old Blueprint assembly removed, new one added without the root type).
                    // In both cases, the fix is to add the destination using.
                    missingNamespaces[destinationNamespace] = identifier.GetLocation();
                }
            }
        }

        // Report one diagnostic per missing namespace
        foreach (var kvp in missingNamespaces)
        {
            var properties = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, string?>("TargetNamespace", kvp.Key),
            });

            context.ReportDiagnostic(Diagnostic.Create(
                MigrationDiagnosticDescriptors.RelocatedTypeNeedUsing,
                kvp.Value,
                properties,
                kvp.Key));
        }
    }
}
