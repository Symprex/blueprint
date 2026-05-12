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
    /// Attempts to find a handler for the given operation type.
    /// </summary>
    /// <param name="operationType">The operation type to find a handler for.</param>
    /// <param name="compilation">The compilation to search in.</param>
    /// <returns>The discovered handler model, or failure with diagnostic if no handler was found.</returns>
    [MustUseReturnValue]
    public static Result<OperationHandlerModel> FindHandler(
        INamedTypeSymbol operationType,
        Compilation compilation)
    {
        // First, try to find an inline handler
        var (inlineValue, inlineDiagnostic) = FindInlineHandler(operationType);
        if (inlineDiagnostic != null)
        {
            // Multiple inline handlers or other error
            return Result<OperationHandlerModel>.Failure(inlineDiagnostic);
        }

        if (inlineValue != null)
        {
            return Result.Success(inlineValue);
        }

        // If no inline handler, search for external handler
        var (externalValue, externalDiagnostic) = FindExternalHandler(operationType, compilation);
        if (externalDiagnostic != null)
        {
            // Multiple external handlers or other error
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
            var handlerNames = string.Join(", ", handlerMethods.Select(m => m.Name));
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
    /// Finds an external handler (method on a separate handler class where first parameter is the operation).
    /// </summary>
    private static (OperationHandlerModel? Value, DiagnosticInfo? Diagnostic) FindExternalHandler(
        INamedTypeSymbol operationType,
        Compilation compilation)
    {
        var foundHandlers = new List<(INamedTypeSymbol HandlerClass, IMethodSymbol HandlerMethod)>();

        // Search all types in the compilation
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            // Get all type symbols in this syntax tree
            var types = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .Select(c => semanticModel.GetDeclaredSymbol(c))
                .Where(t => t != null)
                .Cast<INamedTypeSymbol>();

            foreach (var type in types)
            {
                // Look for handler methods
                var methods = type.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => ParameterAnalyzer.IsHandlerMethodName(m.Name));

                foreach (var method in methods)
                {
                    // Check if first parameter matches the operation type
                    if (method.Parameters.Length > 0 &&
                        SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, operationType))
                    {
                        foundHandlers.Add((type, method));
                    }
                }
            }
        }

        if (foundHandlers.Count == 0)
        {
            // No external handler found
            return (null, null);
        }

        if (foundHandlers.Count > 1)
        {
            // Multiple external handlers found - error
            var handlerNames = string.Join(", ", foundHandlers.Select(h => $"{h.HandlerClass.Name}.{h.HandlerMethod.Name}"));
            var diagnostic = new DiagnosticInfo(
                DiagnosticDescriptors.MultipleHandlersFound,
                LocationInfo.CreateFrom(operationType.Locations.FirstOrDefault()!),
                new EquatableList<string>(new[] { operationType.Name, handlerNames }));

            return (null, diagnostic);
        }

        var (handlerClass, handlerMethod) = foundHandlers[0];

        // Analyze parameters (skip first parameter which is the operation)
        var parameters = ParameterAnalyzer.AnalyzeParameters(handlerMethod, operationType, isExternalHandler: true);

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
    /// the Signature 365 <c>ApiOperations</c> base class pattern where <c>Context</c>, <c>Identity</c>,
    /// <c>Logger</c>, and <c>CancellationToken</c> are injected as properties.
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

                var typeName = property.Type.ToDisplayString();
                var sourceExpression = GetPropertyInjectionSource(property.Name, typeName);
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
    /// Maps well-known handler property types and names to pipeline source expressions.
    /// </summary>
    private static string? GetPropertyInjectionSource(string propertyName, string typeName)
    {
        // Exact type matches
        return typeName switch
        {
            "Blueprint.Runtime.Exec.ApiOperationContext" => "context",
            "Blueprint.Authorization.IUserAuthorisationContext" => "context.UserAuthorisationContext",
            "System.Threading.CancellationToken" => "context.OperationCancelled",
            "Microsoft.Extensions.Logging.ILogger" => "this.Logger",
            _ => null,
        };
    }
}
