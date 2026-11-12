using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Blueprint.SourceGenerator.Modules.Gateway;

/// <summary>
/// D11's imperative tier: a gateway operation's <c>OnMap(TInner inner)</c> or <c>OnMapAsync(TInner inner, …)</c>, which
/// the generated forward calls after copying and binding the inner operation and before sending it.
/// </summary>
/// <remarks>
/// The hook is an instance or static method the generated pipeline can call, so <c>internal</c> or <c>public</c>; a static
/// one is called on the operation type, so a hook that reads nothing of the operation needs no CA1822 suppression. It may be split
/// across partial declarations. <c>OnMap</c> returns <c>void</c> and is not <c>async</c>; <c>OnMapAsync</c> returns <c>ValueTask</c> or <c>Task</c>. Parameters after the first are injected as
/// a handler's are (<c>ParameterAnalyzer</c>). An exception it throws, such as <c>ForbiddenException</c>, stops the forward.
/// </remarks>
internal static class GatewayMapHook
{
    /// <summary>
    /// The synchronous hook's name.
    /// </summary>
    public const string SyncName = "OnMap";

    /// <summary>
    /// The asynchronous hook's name.
    /// </summary>
    public const string AsyncName = "OnMapAsync";

    /// <summary>
    /// Finds the operation's map hook, reporting BPGW014 for a method of a hook's name that cannot be one.
    /// </summary>
    /// <param name="operationType">The gateway operation.</param>
    /// <param name="innerType">The inner operation it forwards to.</param>
    /// <param name="diagnostics">Receives BPGW014, or <see langword="null"/> to find the hook without reporting.</param>
    /// <returns>The hook, or <see langword="null"/> when the operation has no valid one.</returns>
    public static IMethodSymbol? Find(INamedTypeSymbol operationType, INamedTypeSymbol innerType, List<DiagnosticInfo>? diagnostics)
    {
        IMethodSymbol? found = null;

        foreach (var member in operationType.GetMembers())
        {
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, Name: SyncName or AsyncName } listed)
            {
                continue;
            }

            // GetMembers lists a partial method once, by its defining part; the implementation part is the one the
            // pipeline calls, and the one whose modifiers (async among them) matter.
            var method = listed.PartialImplementationPart ?? listed;

            if (!IsValid(method, innerType) || found is not null)
            {
                diagnostics?.Add(new DiagnosticInfo(
                    GatewayDiagnosticDescriptors.InvalidMapHook,
                    method.Locations.FirstOrDefault() is { } location ? LocationInfo.CreateFrom(location) : null,
                    [operationType.Name, method.Name, innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]));
                continue;
            }

            found = method;
        }

        return found;
    }

    /// <summary>
    /// Whether the hook's own body assigns <c>inner.<paramref name="propertyName"/></c>, by any assignment operator, through
    /// its first parameter (#175).
    /// </summary>
    /// <remarks>
    /// Syntax only, so it does not follow a call to a helper or tell a conditional assignment from an unconditional one: the
    /// forward's null check backs it at run time where null is never a value the hook meant.
    /// </remarks>
    /// <param name="hook">The hook <see cref="Find"/> returned.</param>
    /// <param name="propertyName">The inner operation's property.</param>
    /// <returns><see langword="true"/> when an assignment to it appears in the hook.</returns>
    public static bool Assigns(IMethodSymbol hook, string propertyName)
    {
        var parameterName = hook.Parameters[0].Name;

        foreach (var reference in hook.DeclaringSyntaxReferences)
        {
            foreach (var assignment in reference.GetSyntax().DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target, Name: var name }
                    && target.Identifier.ValueText == parameterName
                    && name.Identifier.ValueText == propertyName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValid(IMethodSymbol method, INamedTypeSymbol innerType)
    {
        // Static or instance: a hook that reads nothing of the operation can be static, which CA1822 asks for (#176).
        if (method.IsGenericMethod
            || method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
            || method.Parameters.Length == 0
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, innerType)
            || method.Parameters.Any(static p => p.RefKind != RefKind.None))
        {
            return false;
        }

        // An async void OnMap would return at its first await, before its mapping or guard ran, and the forward would send
        // the inner operation without it; its exceptions would escape the pipeline.
        if (method.Name == SyncName)
        {
            return method.ReturnsVoid && !method.IsAsync;
        }

        return method.ReturnType is INamedTypeSymbol { Arity: 0, Name: "ValueTask" or "Task" } returnType
            && returnType.IsInNamespace("System", "Threading", "Tasks");
    }
}
