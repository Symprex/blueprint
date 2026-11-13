using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// Whether the hook's own body assigns <c>inner.<paramref name="propertyName"/></c> unconditionally, by any assignment
    /// operator, through its first parameter (#175).
    /// </summary>
    /// <remarks>
    /// Syntax only. An assignment counts when it is the hook's expression body or a statement of its body, directly or in a
    /// nested plain block, so it runs whenever the hook returns normally. One under an <c>if</c>, a loop, a <c>try</c>, a
    /// lambda or a local function, or after a <c>return</c> or <c>goto</c> of the hook's own, might never run, and a call to a
    /// helper cannot be seen, so neither counts. Nor does any where the hook rebinds its parameter, by assignment or as a
    /// <c>ref</c> or <c>out</c> argument, since it may then assign an instance the forward does not send. A required property
    /// with no null check behind it, such as a <c>required int?</c>, would otherwise be sent as its initial value.
    /// </remarks>
    /// <param name="hook">The hook <see cref="Find"/> returned.</param>
    /// <param name="propertyName">The inner operation's property.</param>
    /// <returns><see langword="true"/> when the hook always assigns it.</returns>
    public static bool Assigns(IMethodSymbol hook, string propertyName)
    {
        var parameterName = hook.Parameters[0].Name;

        foreach (var reference in hook.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not MethodDeclarationSyntax declaration)
            {
                continue;
            }

            // A hook that rebinds its parameter may assign another instance than the one the forward sends, so none of its
            // assignments counts.
            var assignments = declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToList();
            if (assignments.Any(a => Rebinds(a.Left, parameterName))
                || declaration.DescendantNodes().OfType<ArgumentSyntax>().Any(a => !a.RefKindKeyword.IsKind(SyntaxKind.None)
                    && !a.RefKindKeyword.IsKind(SyntaxKind.InKeyword)
                    && IsParameter(a.Expression, parameterName)))
            {
                return false;
            }

            foreach (var assignment in assignments)
            {
                if (assignment.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target, Name: var name }
                    && target.Identifier.ValueText == parameterName
                    && name.Identifier.ValueText == propertyName
                    && AlwaysRuns(assignment, declaration))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an assignment's target is the parameter itself, alone or as an element of a deconstruction such as
    /// <c>(inner, _) = …</c>, rather than one of its members.
    /// </summary>
    private static bool Rebinds(ExpressionSyntax target, string parameterName)
    {
        return target switch
        {
            TupleExpressionSyntax tuple => tuple.Arguments.Any(a => Rebinds(a.Expression, parameterName)),
            _ => IsParameter(target, parameterName),
        };
    }

    private static bool IsParameter(ExpressionSyntax expression, string parameterName)
    {
        return expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameterName;
    }

    private static bool AlwaysRuns(AssignmentExpressionSyntax assignment, MethodDeclarationSyntax declaration)
    {
        // A return or goto of the hook's own before the assignment may skip it; a lambda's or local function's may not.
        var earlyReturn = declaration.DescendantNodes(static n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .Where(static n => n is ReturnStatementSyntax or GotoStatementSyntax)
            .Any(r => r.SpanStart < assignment.SpanStart);
        if (earlyReturn)
        {
            return false;
        }

        if (assignment.Parent == declaration.ExpressionBody)
        {
            return true;
        }

        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }

        for (var current = statement.Parent; current is BlockSyntax block; current = block.Parent)
        {
            if (block == declaration.Body)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the compiler may leave out a call to the hook: a <c>[Conditional]</c> one is called only where its symbol is
    /// defined, so the forward would send the inner operation without its mapping or guard elsewhere.
    /// </summary>
    private static bool IsConditional(IMethodSymbol method)
    {
        return method.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute");
    }

    private static bool IsValid(IMethodSymbol method, INamedTypeSymbol innerType)
    {
        // Static or instance: a hook that reads nothing of the operation can be static, which CA1822 asks for (#176).
        if (method.IsGenericMethod
            || method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)
            || method.Parameters.Length == 0
            || !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, innerType)
            || method.Parameters.Any(static p => p.RefKind != RefKind.None)
            || IsConditional(method))
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
