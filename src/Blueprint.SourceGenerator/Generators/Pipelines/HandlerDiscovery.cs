using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Discovers handlers for operations, both inline (on the operation class) and external (separate handler classes).
/// </summary>
public static class HandlerDiscovery
{
    /// <summary>
    /// Attempts to find a handler for the given operation type using a pre-built external handler index.
    /// The index should be built once via <see cref="BuildExternalHandlerIndex"/> and reused across all
    /// operations in a compilation to avoid repeated full-compilation scans.
    /// </summary>
    /// <param name="operationType">The operation type to find a handler for.</param>
    /// <param name="externalHandlerIndex">Pre-built index mapping operation types to candidate handler methods.</param>
    /// <returns>The discovered handler model, or failure with diagnostic if no handler was found.</returns>
    [MustUseReturnValue]
    public static Result<OperationHandlerModel> FindHandler(
        INamedTypeSymbol operationType,
        ExternalHandlerIndex externalHandlerIndex)
    {
        // First, try to find an inline handler
        var (inlineValue, inlineDiagnostic) = FindInlineHandler(operationType);
        if (inlineDiagnostic != null)
        {
            return Result<OperationHandlerModel>.Failure(inlineDiagnostic);
        }

        if (inlineValue != null)
        {
            return Result.Success(inlineValue);
        }

        // If no inline handler, look up from the pre-built index
        var (externalValue, externalDiagnostic) = FindExternalHandlerFromIndex(operationType, externalHandlerIndex);
        if (externalDiagnostic != null)
        {
            return Result<OperationHandlerModel>.Failure(externalDiagnostic);
        }

        if (externalValue != null)
        {
            return Result.Success(externalValue);
        }

        // No handler found - create diagnostic
        var diagnostic = new DiagnosticInfo(
            DiagnosticDescriptors.NoHandlerFound,
            LocationInfo.CreateFrom(operationType.Locations.FirstOrDefault()!),
            new EquatableList<string>(new[] { operationType.Name }));

        return Result<OperationHandlerModel>.Failure(diagnostic);
    }

    /// <summary>
    /// Legacy overload that builds a per-call external handler index. Prefer the overload
    /// accepting <see cref="ExternalHandlerIndex"/> for batch handler discovery.
    /// </summary>
    /// <param name="operationType">The operation type to find a handler for.</param>
    /// <param name="compilation">The compilation to search in.</param>
    /// <returns>The discovered handler model, or failure with diagnostic if no handler was found.</returns>
    [MustUseReturnValue]
    public static Result<OperationHandlerModel> FindHandler(
        INamedTypeSymbol operationType,
        Compilation compilation)
    {
        var index = BuildExternalHandlerIndex(compilation);
        return FindHandler(operationType, index);
    }

    /// <summary>
    /// Builds an index of all candidate external handler methods in the compilation by traversing
    /// assembly namespaces (symbol-based). This is O(types + methods) and should be called once
    /// per compilation, then reused for all operations — avoiding the O(operations × syntax trees)
    /// cost of per-operation syntax tree scanning.
    /// </summary>
    /// <param name="compilation">The compilation to index.</param>
    /// <returns>A reusable index for per-operation handler lookups.</returns>
    [MustUseReturnValue]
    public static ExternalHandlerIndex BuildExternalHandlerIndex(Compilation compilation)
    {
        var candidates = new List<(INamedTypeSymbol HandlerClass, IMethodSymbol Method, INamedTypeSymbol OperationType)>();

        foreach (var type in GetAllTypesFromCompilation(compilation))
        {
            if (type.IsAbstract || type.IsStatic || type.TypeKind != TypeKind.Class)
            {
                continue;
            }

            foreach (var member in type.GetMembers())
            {
                if (member is not IMethodSymbol method || !ParameterAnalyzer.IsHandlerMethodName(method.Name))
                {
                    continue;
                }

                if (method.Parameters.Length > 0 &&
                    method.Parameters[0].Type is INamedTypeSymbol firstParamType)
                {
                    candidates.Add((type, method, firstParamType));
                }
            }
        }

        return new ExternalHandlerIndex(candidates);
    }

    /// <summary>
    /// Finds an inline handler (method directly on the operation class).
    /// </summary>
    private static (OperationHandlerModel? Value, DiagnosticInfo? Diagnostic) FindInlineHandler(INamedTypeSymbol operationType)
    {
        var handlerMethods = operationType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => ParameterAnalyzer.IsHandlerMethodName(m.Name))
            .ToArray();

        if (handlerMethods.Length == 0)
        {
            // No inline handler found, not an error - might have external handler
            return (null, null);
        }

        if (handlerMethods.Length > 1)
        {
            // Multiple handlers found - this is an error
            var handlerNames = string.Join(", ", handlerMethods.Select(static m => m.Name));
            var diagnostic = new DiagnosticInfo(
                DiagnosticDescriptors.MultipleHandlersFound,
                LocationInfo.CreateFrom(operationType.Locations.FirstOrDefault()!),
                new EquatableList<string>(new[] { operationType.Name, handlerNames }));

            return (null, diagnostic);
        }

        var handlerMethod = handlerMethods[0];

        // Analyze parameters
        var parameters = ParameterAnalyzer.AnalyzeParameters(handlerMethod, operationType, isExternalHandler: false);

        // Determine return type
        var returnType = handlerMethod.ReturnType;
        var returnTypeName = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Check if async
        var isAsync = returnType.Name == "Task" ||
                      (returnType is INamedTypeSymbol namedReturn &&
                       namedReturn.IsGenericType &&
                       namedReturn.ConstructedFrom.Name == "Task");

        var model = new OperationHandlerModel(
            OperationTypeName: operationType.Name,
            OperationTypeFullName: operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            OperationNamespace: operationType.ContainingNamespace.ToDisplayString(),
            HandlerType: HandlerType.Inline,
            HandlerMethodName: handlerMethod.Name,
            IsHandlerStatic: handlerMethod.IsStatic,
            IsHandlerAsync: isAsync,
            HandlerReturnTypeName: returnTypeName,
            HandlerClassName: null,
            HandlerClassFullName: null,
            HandlerClassNamespace: null,
            Parameters: parameters);

        return (model, null);
    }

    /// <summary>
    /// Finds an external handler from the pre-built index by matching the operation type
    /// against the first parameter of candidate handler methods.
    /// </summary>
    private static (OperationHandlerModel? Value, DiagnosticInfo? Diagnostic) FindExternalHandlerFromIndex(
        INamedTypeSymbol operationType,
        ExternalHandlerIndex index)
    {
        var foundHandlers = index.FindHandlersFor(operationType);

        if (foundHandlers.Length == 0)
        {
            return (null, null);
        }

        if (foundHandlers.Length > 1)
        {
            var handlerNames = string.Join(", ", foundHandlers.Select(static h => $"{h.HandlerClass.Name}.{h.HandlerMethod.Name}"));
            var diagnostic = new DiagnosticInfo(
                DiagnosticDescriptors.MultipleHandlersFound,
                LocationInfo.CreateFrom(operationType.Locations.FirstOrDefault()!),
                new EquatableList<string>(new[] { operationType.Name, handlerNames }));

            return (null, diagnostic);
        }

        var (handlerClass, handlerMethod) = foundHandlers[0];

        var parameters = ParameterAnalyzer.AnalyzeParameters(handlerMethod, operationType, isExternalHandler: true);

        var returnType = handlerMethod.ReturnType;
        var returnTypeName = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var isAsync = returnType.Name == "Task" ||
                      (returnType is INamedTypeSymbol namedReturn &&
                       namedReturn.IsGenericType &&
                       namedReturn.ConstructedFrom.Name == "Task");

        var model = new OperationHandlerModel(
            OperationTypeName: operationType.Name,
            OperationTypeFullName: operationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            OperationNamespace: operationType.ContainingNamespace.ToDisplayString(),
            HandlerType: HandlerType.External,
            HandlerMethodName: handlerMethod.Name,
            IsHandlerStatic: handlerMethod.IsStatic,
            IsHandlerAsync: isAsync,
            HandlerReturnTypeName: returnTypeName,
            HandlerClassName: handlerClass.Name,
            HandlerClassFullName: handlerClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            HandlerClassNamespace: handlerClass.ContainingNamespace.ToDisplayString(),
            Parameters: parameters,
            ConstructorParameters: ExtractConstructorParameters(handlerClass),
            PropertyInjections: DetectPropertyInjections(handlerClass));

        return (model, null);
    }

    /// <summary>
    /// Enumerates all named types from the compilation's own assembly only.
    /// External handler classes must be in source code being compiled — they cannot
    /// live in referenced (already-compiled) assemblies because the source generator
    /// only runs on source in the current compilation.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetAllTypesFromCompilation(Compilation compilation)
    {
        foreach (var type in GetTypesFromNamespace(compilation.Assembly.GlobalNamespace))
        {
            yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypesFromNamespace(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamedTypeSymbol namedType)
            {
                yield return namedType;

                // Check nested types
                foreach (var nested in GetNestedTypes(namedType))
                {
                    yield return nested;
                }
            }
            else if (member is INamespaceSymbol childNs)
            {
                foreach (var type in GetTypesFromNamespace(childNs))
                {
                    yield return type;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
    {
        foreach (var member in type.GetTypeMembers())
        {
            yield return member;

            foreach (var nested in GetNestedTypes(member))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Extracts the constructor parameters of an external handler class so the pipeline can
    /// generate a direct <c>new Handler(GetRequiredService&lt;X&gt;(), ...)</c> call instead of
    /// <c>ActivatorUtilities.CreateInstance</c>. Returns <c>null</c> when the constructor
    /// cannot be unambiguously resolved (abstract class, multiple public constructors, etc.).
    /// </summary>
    private static EquatableList<ConstructorParameterInfo>? ExtractConstructorParameters(INamedTypeSymbol handlerClass)
    {
        if (handlerClass.IsAbstract)
        {
            return null;
        }

        var publicCtors = handlerClass.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public && !c.IsImplicitlyDeclared)
            .ToArray();

        // Parameterless constructor: no DI needed, just `new Handler()`
        if (publicCtors.Length == 0)
        {
            // Only implicit default constructor exists
            return new EquatableList<ConstructorParameterInfo>([]);
        }

        if (publicCtors.Length != 1)
        {
            // Multiple public constructors — ActivatorUtilities uses its own heuristics;
            // fall back to the reflection path to avoid mismatching behaviour.
            return null;
        }

        var ctor = publicCtors[0];
        var parameters = new List<ConstructorParameterInfo>(ctor.Parameters.Length);

        foreach (var param in ctor.Parameters)
        {
            parameters.Add(new ConstructorParameterInfo(
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return new EquatableList<ConstructorParameterInfo>(parameters.ToArray());
    }

    /// <summary>
    /// Detects settable properties on an external handler class that match well-known pipeline types.
    /// These properties will be assigned from pipeline context after handler instantiation, supporting
    /// the Signature 365 <c>ApiOperations</c> pattern where a consumer base class derives from
    /// <c>BlueprintApiOperations</c> and exposes <c>Context</c>, <c>Identity</c>, <c>Logger</c>,
    /// and <c>CancellationToken</c> as injected properties.
    /// </summary>
    private static EquatableList<HandlerPropertyInjection>? DetectPropertyInjections(INamedTypeSymbol handlerClass)
    {
        var injections = new List<HandlerPropertyInjection>();

        // Walk the type hierarchy to find settable properties (including inherited ones)
        var currentType = handlerClass;
        while (currentType != null)
        {
            foreach (var member in currentType.GetMembers())
            {
                if (member is not IPropertySymbol property)
                {
                    continue;
                }

                // Must be a public settable instance property
                if (property.IsStatic || property.IsReadOnly || property.SetMethod is null)
                {
                    continue;
                }

                if (property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                var sourceExpression = GetPropertyInjectionSource(property);
                if (sourceExpression != null)
                {
                    injections.Add(new HandlerPropertyInjection(property.Name, sourceExpression));
                }
            }

            currentType = currentType.BaseType;
        }

        return injections.Count > 0
            ? new EquatableList<HandlerPropertyInjection>(injections.ToArray())
            : null;
    }

    /// <summary>
    /// Maps well-known handler property types to pipeline source expressions using structural
    /// symbol checks (MetadataName + namespace) to avoid <c>ToDisplayString()</c> allocations.
    /// </summary>
    private static string? GetPropertyInjectionSource(IPropertySymbol property)
    {
        var type = property.Type;
        var metadataName = type.MetadataName;

        return metadataName switch
        {
            "ApiOperationContext" when type.IsInNamespace("Blueprint", "Runtime", "Exec")
                => "context",
            "IUserAuthorisationContext" when type.IsInNamespace("Blueprint", "Authorization")
                => "context.UserAuthorisationContext ?? Blueprint.Authorization.AnonymousUserAuthorisationContext.Instance",
            "CancellationToken" when type.IsInNamespace("System", "Threading")
                => "context.OperationCancelled",
            "ILogger" when type.IsInNamespace("Microsoft", "Extensions", "Logging")
                => "this.Logger",
            "HttpContext" when type.IsInNamespace("Microsoft", "AspNetCore", "Http")
                => "context.HttpContext",
            "HttpRequest" when type.IsInNamespace("Microsoft", "AspNetCore", "Http")
                => "context.HttpContext.Request",
            "HttpResponse" when type.IsInNamespace("Microsoft", "AspNetCore", "Http")
                => "context.HttpContext.Response",
            "ClaimsPrincipal" when type.IsInNamespace("System", "Security", "Claims")
                => "context.UserPrincipal",
            _ => null,
        };
    }
}
