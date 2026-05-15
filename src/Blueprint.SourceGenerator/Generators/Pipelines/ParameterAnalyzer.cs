using Blueprint.SourceGenerator.Generators.Pipelines.Model;
using Blueprint.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Blueprint.SourceGenerator.Generators.Pipelines;

/// <summary>
/// Helper class for analyzing handler method parameters and determining how they should be resolved.
/// </summary>
public static class ParameterAnalyzer
{
    /// <summary>
    /// Well-known handler method names.
    /// </summary>
    public static readonly string[] HandlerMethodNames = { "Invoke", "InvokeAsync", "Handle", "HandleAsync" };

    /// <summary>
    /// Analyzes the parameters of a handler method and creates HandlerParameter instances.
    /// </summary>
    /// <param name="handlerMethod">The handler method to analyze.</param>
    /// <param name="operationType">The operation type symbol.</param>
    /// <param name="isExternalHandler">Whether this is an external handler (first param is operation).</param>
    /// <returns>List of analyzed parameters.</returns>
    public static EquatableList<HandlerParameter> AnalyzeParameters(
        IMethodSymbol handlerMethod,
        INamedTypeSymbol operationType,
        bool isExternalHandler)
    {
        var parameters = new List<HandlerParameter>();
        var methodParams = handlerMethod.Parameters;

        // For external handlers, skip the first parameter (it's the operation itself)
        var startIndex = isExternalHandler ? 1 : 0;

        for (var i = startIndex; i < methodParams.Length; i++)
        {
            var param = methodParams[i];
            var source = DetermineParameterSource(param, operationType);
            var contextPropertyName = source == ParameterSource.FromContextProperty
                ? GetContextPropertyName(param)
                : null;

            parameters.Add(new HandlerParameter(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                source,
                contextPropertyName));
        }

        return new EquatableList<HandlerParameter>(parameters.ToArray());
    }

    /// <summary>
    /// Determines how a parameter should be resolved using structural symbol checks
    /// (MetadataName + namespace) to avoid <c>ToDisplayString()</c> allocations.
    /// </summary>
    private static ParameterSource DetermineParameterSource(IParameterSymbol param, INamedTypeSymbol operationType)
    {
        var paramType = param.Type;

        // Check if it's the operation itself
        if (SymbolEqualityComparer.Default.Equals(paramType, operationType))
        {
            return ParameterSource.Operation;
        }

        // Check for ApiOperationContext via MetadataName (no allocation)
        if (paramType.MetadataName == "ApiOperationContext"
            && IsInNamespace(paramType, "Exec", "Runtime", "Blueprint"))
        {
            return ParameterSource.FromContext;
        }

        // Check for known context properties via MetadataName
        if (IsContextPropertyBySymbol(paramType, param.Name))
        {
            return ParameterSource.FromContextProperty;
        }

        // Everything else is resolved from DI
        return ParameterSource.FromDependencyInjection;
    }

    /// <summary>
    /// Checks if a parameter type represents a context property using structural symbol
    /// checks (MetadataName + namespace) to avoid <c>ToDisplayString()</c> allocations.
    /// </summary>
    private static bool IsContextPropertyBySymbol(ITypeSymbol type, string paramName)
    {
        // Match by MetadataName first (property access, no allocation), then verify namespace
        switch (type.MetadataName)
        {
            case "BlueprintApp" when IsInNamespace(type, "Model", "Blueprint"):
            case "CancellationToken" when IsInNamespace(type, "Threading", "System"):
            case "IUserAuthorisationContext" when IsInNamespace(type, "Authorization", "Blueprint"):
            case "HttpContext" when IsInNamespace(type, "Http", "AspNetCore", "Microsoft"):
            case "HttpRequest" when IsInNamespace(type, "Http", "AspNetCore", "Microsoft"):
            case "HttpResponse" when IsInNamespace(type, "Http", "AspNetCore", "Microsoft"):
            case "ClaimsPrincipal" when IsInNamespace(type, "Claims", "Security", "System"):
            case "ClaimsIdentity" when IsInNamespace(type, "Claims", "Security", "System"):
                return true;
        }

        // Match by parameter name convention (case-insensitive)
        return paramName.Equals("datamodel", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("app", StringComparison.OrdinalIgnoreCase)
            || paramName.Equals("cancellationtoken", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the property name to access from ApiOperationContext for a given parameter
    /// using structural symbol checks to avoid <c>ToDisplayString()</c> allocations.
    /// </summary>
    private static string GetContextPropertyName(IParameterSymbol param)
    {
        // Match by MetadataName first (cheap property access)
        switch (param.Type.MetadataName)
        {
            case "BlueprintApp" when IsInNamespace(param.Type, "Model", "Blueprint"):
                return "App";
            case "CancellationToken" when IsInNamespace(param.Type, "Threading", "System"):
                return "OperationCancelled";
            case "IUserAuthorisationContext" when IsInNamespace(param.Type, "Authorization", "Blueprint"):
                return "UserAuthorisationContext!";
            case "HttpContext" when IsInNamespace(param.Type, "Http", "AspNetCore", "Microsoft"):
                return "HttpContext!";
            case "HttpRequest" when IsInNamespace(param.Type, "Http", "AspNetCore", "Microsoft"):
                return "HttpContext!.Request";
            case "HttpResponse" when IsInNamespace(param.Type, "Http", "AspNetCore", "Microsoft"):
                return "HttpContext!.Response";
            case "ClaimsPrincipal" when IsInNamespace(param.Type, "Claims", "Security", "System"):
                return "UserPrincipal!";
            case "ClaimsIdentity" when IsInNamespace(param.Type, "Claims", "Security", "System"):
                return "(System.Security.Claims.ClaimsIdentity)UserPrincipal!.Identity!";
        }

        return param.Name.ToLowerInvariant() switch
        {
            "datamodel" or "app" => "App",
            "cancellationtoken" => "OperationCancelled",
            _ => "App", // Default fallback
        };
    }

    /// <summary>
    /// Verifies a type symbol's containing namespace matches the expected hierarchy without
    /// calling <c>ToDisplayString()</c>. Arguments are innermost-first (e.g. "Exec", "Runtime", "Blueprint").
    /// </summary>
    private static bool IsInNamespace(ITypeSymbol type, params string[] parts)
    {
        var ns = type.ContainingNamespace;

        foreach (var part in parts)
        {
            if (ns == null || ns.IsGlobalNamespace || ns.Name != part)
            {
                return false;
            }

            ns = ns.ContainingNamespace;
        }

        return ns is { IsGlobalNamespace: true };
    }

    /// <summary>
    /// Checks if a method is a valid handler method by name.
    /// </summary>
    /// <param name="methodName">The method name to evaluate against the supported handler entry-point names.</param>
    /// <returns><see langword="true"/> when the method name is one of the supported handler entry points.</returns>
    public static bool IsHandlerMethodName(string methodName)
    {
        return HandlerMethodNames.Contains(methodName);
    }
}
