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
            && paramType.IsInNamespace("Blueprint", "Runtime", "Exec"))
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
            case "BlueprintApp" when type.IsInNamespace("Blueprint", "Model"):
            case "CancellationToken" when type.IsInNamespace("System", "Threading"):
            case "IUserAuthorisationContext" when type.IsInNamespace("Blueprint", "Authorization"):
            case "HttpContext" when type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
            case "HttpRequest" when type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
            case "HttpResponse" when type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
            case "ClaimsPrincipal" when type.IsInNamespace("System", "Security", "Claims"):
            case "ClaimsIdentity" when type.IsInNamespace("System", "Security", "Claims"):
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
            case "BlueprintApp" when param.Type.IsInNamespace("Blueprint", "Model"):
                return "App";
            case "CancellationToken" when param.Type.IsInNamespace("System", "Threading"):
                return "OperationCancelled";
            case "IUserAuthorisationContext" when param.Type.IsInNamespace("Blueprint", "Authorization"):
                return "UserAuthorisationContext!";
            case "HttpContext" when param.Type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
                return "HttpContext!";
            case "HttpRequest" when param.Type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
                return "HttpContext!.Request";
            case "HttpResponse" when param.Type.IsInNamespace("Microsoft", "AspNetCore", "Http"):
                return "HttpContext!.Response";
            case "ClaimsPrincipal" when param.Type.IsInNamespace("System", "Security", "Claims"):
                return "UserPrincipal!";
            case "ClaimsIdentity" when param.Type.IsInNamespace("System", "Security", "Claims"):
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
    /// Checks if a method is a valid handler method by name.
    /// </summary>
    /// <param name="methodName">The method name to evaluate against the supported handler entry-point names.</param>
    /// <returns><see langword="true"/> when the method name is one of the supported handler entry points.</returns>
    public static bool IsHandlerMethodName(string methodName)
    {
        return HandlerMethodNames.Contains(methodName);
    }
}
